using System.Text.RegularExpressions;

namespace Envoy.ControlPlane.Tools;

internal static partial class MigrationCorrections
{
    internal static void Apply(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.proto", SearchOption.AllDirectories))
        {
            string original = File.ReadAllText(path);
            string corrected = DerivedFieldOption().Replace(original, CorrectKnownRegexNames);
            // Setting the required subfield also composes with a numeric wrapper rule.
            // The migration tool's whole-message assignment conflicts with that rule.
            corrected = corrected.Replace("(buf.validate.field) = {required: true}",
                "(buf.validate.field).required = true", StringComparison.Ordinal);
            if (corrected != original)
            {
                File.WriteAllText(path, corrected);
            }
        }
    }

    private static string CorrectKnownRegexNames(Match option) =>
        LegacyKnownRegexName().Replace(option.Value, "KNOWN_REGEX_HTTP_HEADER_$1");

    [GeneratedRegex(@"(\(buf\.validate\.field\)[\s\S]*?)(?=,\s*\(|\];)")]
    private static partial Regex DerivedFieldOption();

    [GeneratedRegex(@"(?<![A-Z_])HTTP_HEADER_(NAME|VALUE)")]
    private static partial Regex LegacyKnownRegexName();
}
