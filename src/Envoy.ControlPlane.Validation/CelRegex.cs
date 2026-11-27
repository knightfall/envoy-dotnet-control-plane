using System.Text;
using System.Text.RegularExpressions;
using Cel;

namespace Envoy.ControlPlane.Validation;

internal static class CelRegex
{
    private const int MaxPatternBytes = 16 * 1024;
    internal static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Exact patterns from the pinned Envoy catalog and the predefined header and UUID rules.
    /// The managed forms use strict end anchors and RE2 ASCII character classes.
    /// </summary>
    private static readonly Dictionary<string, Func<string, bool>> CatalogMatches = new(StringComparer.Ordinal)
    {
        ["^[^?#]+[^?#/]$"] = MatchPathSeparatedPrefix,
        ["""^[^\x00-\x1f\x7f]*$"""] = Compile("""\A[^\x00-\x1f\x7f]*\z"""),
        ["""^[a-z0-9_\-]*$"""] = Compile("""\A[a-z0-9_-]*\z"""),
        ["^[^/]*$"] = Compile("""\A[^/]*\z"""),
        ["""^[^\s,]+$"""] = Compile("""\A[^\t\n\f\r ,]+\z"""),
        ["""^$|^/[^\x00-\x1f\x7f ",;<>\\]*$"""] = Compile("""\A(?:/[^\x00-\x1f\x7f ",;<>\\]*)?\z"""),
        ["""^$|^[^\x00-\x1f\x7f ",;<>\\]+$"""] = Compile("""\A[^\x00-\x1f\x7f ",;<>\\]*\z"""),
        ["""^$|^.*\.mmdb$"""] = Compile("""\A(?:.*\.mmdb)?\z"""),
        ["""^[0-9a-zA-Z_\-\.~:]+$"""] = Compile("""\A[0-9a-zA-Z_.~:-]+\z"""),
        ["""^[0-9a-zA-Z_\-\./~:]+$"""] = Compile("""\A[0-9a-zA-Z_./~:-]+\z"""),
        ["^:?[0-9a-zA-Z!#$%&'*+-.^_|~`]+$"] = Compile("""\A:?[0-9a-zA-Z!#$%&'*+-.^_|~`]+\z"""),
        ["^[^\0\n\r]+$"] = Compile("""\A[^\x00\n\r]+\z"""),
        ["^[^\0-\x08\x0A-\x1F\x7F]*$"] = Compile("""\A[^\x00-\x08\x0A-\x1F\x7F]*\z"""),
        ["^[^\0\n\r]*$"] = Compile("""\A[^\x00\n\r]*\z"""),
        ["^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"] =
            Compile("""\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z"""),
        ["^[0-9a-fA-F]{32}$"] = Compile("""\A[0-9a-fA-F]{32}\z"""),
    };

    internal static void Register(CelEnvironment environment, bool requireCatalogPattern, TimeSpan customMatchTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(customMatchTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(customMatchTimeout, DefaultMatchTimeout);
        // The pinned CEL runtime prefers the last registration for both call forms.
        environment.RegisterFunction("matches", [typeof(string), typeof(string)],
            arguments => Match(arguments, requireCatalogPattern, customMatchTimeout));
    }

    private static object Match(object?[] arguments, bool requireCatalogPattern, TimeSpan customMatchTimeout)
    {
        var input = (string)arguments[0]!;
        var pattern = (string)arguments[1]!;
        if (CatalogMatches.TryGetValue(pattern, out var matches))
        {
            return matches(input);
        }
        if (requireCatalogPattern)
        {
            throw new ArgumentException("The CEL regex pattern is outside the supported Envoy catalog.");
        }
        if (pattern.Length > MaxPatternBytes || Encoding.UTF8.GetByteCount(pattern) > MaxPatternBytes)
        {
            throw new ArgumentException("The CEL regex pattern exceeds 16 KiB.");
        }
        // Keep the pinned CEL runtime's .NET syntax for application-defined patterns.
        return Regex.IsMatch(input, pattern, RegexOptions.None, customMatchTimeout);
    }

    private static Func<string, bool> Compile(string pattern) =>
        new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, DefaultMatchTimeout).IsMatch;

    private static bool MatchPathSeparatedPrefix(string input)
    {
        // This rule needs at least two Unicode code points and excludes a final slash.
        return input.Length >= 2 && (input.Length != 2 || !char.IsSurrogatePair(input, 0)) &&
            input.AsSpan().IndexOfAny('?', '#') < 0 && input[^1] != '/';
    }
}
