using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal sealed record UpstreamOptions(string Command, string CacheDirectory, bool Offline, bool WriteCatalog)
{
    internal static UpstreamOptions Parse(string[] arguments)
    {
        Require(arguments[0] is "sync" or "inspect" or "export", "Expected sync, inspect or export");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (int index = 1; index < arguments.Length; index++)
        {
            string option = arguments[index];
            Require(option is "--cache" or "--offline" or "--write-catalog", "Unknown argument: " + option);
            Require(!values.ContainsKey(option), "Duplicate argument: " + option);

            if (option == "--cache")
            {
                Require(index + 1 < arguments.Length, "--cache requires a path");
                values.Add(option, arguments[++index]);
            }
            else
            {
                values.Add(option, null);
            }
        }

        Require(values.ContainsKey("--cache"), "--cache is required");
        Require(arguments[0] == "sync" || !values.ContainsKey("--write-catalog"), "--write-catalog applies to sync only");
        return new UpstreamOptions(arguments[0], values["--cache"]!,
            values.ContainsKey("--offline"), values.ContainsKey("--write-catalog"));
    }
}
