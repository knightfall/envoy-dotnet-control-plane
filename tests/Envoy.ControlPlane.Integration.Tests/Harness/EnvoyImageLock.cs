using System.Text.Json;

namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal sealed class EnvoyImageLock
{
    private readonly Dictionary<string, EnvoyPlatformImages> platforms;

    private EnvoyImageLock(string envoyIndex, string sdkIndex, Dictionary<string, EnvoyPlatformImages> platforms)
    {
        EnvoyIndex = envoyIndex;
        SdkIndex = sdkIndex;
        this.platforms = platforms;
    }

    internal string EnvoyIndex { get; }
    internal string SdkIndex { get; }

    internal EnvoyPlatformImages ForPlatform(string platform) =>
        platforms.TryGetValue(platform, out var images)
            ? images
            : throw new NotSupportedException($"No pinned Envoy and SDK images for '{platform}'.");

    internal static EnvoyImageLock Load()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "envoy-images.lock.json"));
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var platforms = root.GetProperty("platforms").EnumerateObject().ToDictionary(platform => platform.Name,
            platform => new EnvoyPlatformImages(
                ReadImage(platform.Value, "envoyImage", "envoyproxy/envoy"),
                ReadImage(platform.Value, "sdkImage", "mcr.microsoft.com/dotnet/sdk")), StringComparer.Ordinal);
        return new EnvoyImageLock(ReadImage(root, "envoyIndex", "envoyproxy/envoy"),
            ReadImage(root, "sdkIndex", "mcr.microsoft.com/dotnet/sdk"), platforms);
    }

    private static string ReadImage(JsonElement images, string key, string repository)
    {
        string? image = images.GetProperty(key).GetString();
        string prefix = repository + "@sha256:";
        if (image?.StartsWith(prefix, StringComparison.Ordinal) != true
            || image.Length != prefix.Length + 64 || !image.Skip(prefix.Length).All(Uri.IsHexDigit))
        {
            throw new InvalidDataException($"Image lock entry '{key}' must pin {repository} by SHA-256.");
        }
        return image;
    }
}

internal sealed record EnvoyPlatformImages(string EnvoyImage, string SdkImage);
