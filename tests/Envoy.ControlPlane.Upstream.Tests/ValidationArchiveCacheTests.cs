using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class ValidationArchiveCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidDownload_DoesNotReplaceCache_AndVerifiedRetryRepairsIt(bool existingBadArchive)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string cache = Path.Combine(directory.FullName, "cache");
            string destination = Path.Combine(directory.FullName, "destination");
            Directory.CreateDirectory(cache);
            Directory.CreateDirectory(destination);
            string archive = Path.Combine(cache, "source.tar.gz");
            string sentinel = Path.Combine(destination, "sentinel");
            await File.WriteAllTextAsync(sentinel, "original", TestContext.Current.CancellationToken);
            if (existingBadArchive)
            {
                await File.WriteAllTextAsync(archive, "old corrupt archive", TestContext.Current.CancellationToken);
            }
            byte[] valid = MakeArchive();
            var source = Source("https://example.invalid/source.tar.gz", valid);

            await Assert.ThrowsAsync<InvalidOperationException>(() => VerifiedValidationSources.RetrieveAsync(
                cache, source, destination, _ => Task.FromResult("invalid download"u8.ToArray())));
            Assert.Equal(existingBadArchive, File.Exists(archive));
            if (existingBadArchive)
            {
                Assert.Equal("old corrupt archive", await File.ReadAllTextAsync(archive, TestContext.Current.CancellationToken));
            }
            Assert.True(File.Exists(sentinel));

            string extracted = await VerifiedValidationSources.RetrieveAsync(cache, source, destination, _ => Task.FromResult(valid));
            Assert.Equal(valid, await File.ReadAllBytesAsync(archive, TestContext.Current.CancellationToken));
            Assert.Equal("verified", await File.ReadAllTextAsync(Path.Combine(extracted, "proof"), TestContext.Current.CancellationToken));
            Assert.False(File.Exists(sentinel));
            Assert.Single(Directory.GetFiles(cache));

            await VerifiedValidationSources.RetrieveAsync(cache, source, destination,
                _ => throw new InvalidOperationException("A verified cache entry should avoid download."));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "https://example.invalid/source.tar.gz", "outside the repository")]
    [InlineData(false, "http://example.invalid/source.tar.gz", "must use HTTPS")]
    public async Task InvalidSourceLocation_IsRejectedBeforeWriting(bool relativeCache, string url, string reason)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string name = Guid.NewGuid().ToString("N");
            string cache = relativeCache ? Path.Combine(".", name) : Path.Combine(directory.FullName, name);
            string destination = Path.Combine(directory.FullName, "destination");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifiedValidationSources.RetrieveAsync(
                cache, Source(url, []), destination, _ => throw new NotSupportedException()));

            Assert.Contains(reason, error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(cache));
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static JsonObject Source(string url, byte[] bytes) => new()
    {
        ["archiveUrl"] = url,
        ["archiveName"] = "source.tar.gz",
        ["archiveSha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        ["archivePrefix"] = "source",
    };

    private static byte[] MakeArchive()
    {
        using var archive = new MemoryStream();
        using (var gzip = new GZipStream(archive, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "source"));
            using var contents = new MemoryStream("verified"u8.ToArray());
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "source/proof") { DataStream = contents });
        }
        return archive.ToArray();
    }
}
