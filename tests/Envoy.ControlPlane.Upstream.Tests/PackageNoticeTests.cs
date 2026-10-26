using System.IO.Compression;
using System.Text;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class PackageNoticeTests
{
    [Theory]
    [InlineData("original")]
    [InlineData("changed")]
    [InlineData("missing")]
    public void PackagedNotice_MustContainTheExactLockedBytes(string contents)
    {
        using var bytes = new MemoryStream();
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Update, leaveOpen: true);
        if (contents != "missing")
        {
            using var stream = archive.CreateEntry("licenses/upstream/LICENSE").Open();
            stream.Write(Encoding.UTF8.GetBytes(contents));
        }
        string expected = BindingIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("original"));
        if (contents == "original")
        {
            PackageContentsVerification.VerifyFile(archive, "licenses/upstream/LICENSE", expected);
        }
        else
        {
            var error = Assert.Throws<InvalidOperationException>(() => PackageContentsVerification.VerifyFile(archive, "licenses/upstream/LICENSE", expected));
            Assert.Contains(contents == "missing" ? "Missing packaged notice/input" : "differs from source lock", error.Message, StringComparison.Ordinal);
        }
    }
}
