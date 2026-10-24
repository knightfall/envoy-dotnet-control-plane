using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static class RepositoryPaths
{
    internal static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "schemas/upstream.lock.json")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Run from the .NET repository root or a child directory");
    }

    internal static string ResolveDirectoryLinks(string path)
    {
        path = Path.GetFullPath(path);
        var parent = Directory.GetParent(path);
        if (parent is null)
        {
            return path;
        }
        string resolvedParent = ResolveDirectoryLinks(parent.FullName);
        string result = Path.Combine(resolvedParent, Path.GetFileName(path));
        if (!Directory.Exists(result))
        {
            return result;
        }
        return new DirectoryInfo(result).ResolveLinkTarget(true)?.FullName ?? result;
    }

    internal static bool IsWithinDirectory(string path, string parent)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(parent, comparison) || path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }
}
