namespace Envoy.ControlPlane.Tools;

internal static class WorkflowSecurity
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["verify", "--cache", var cache])
        {
            Console.Error.WriteLine("Usage: workflows verify --cache <external-tool-cache>");
            return 1;
        }
        string actionlint = await PinnedReleaseTool.GetAsync("actionlint", cache);
        await BuildProcess.RunAsync(actionlint, ["-shellcheck="]);
        string zizmor = await PinnedReleaseTool.GetAsync("zizmor", cache);
        await BuildProcess.RunAsync(zizmor, ["--offline", "--min-severity", "low", ".github/workflows"]);
        string actionsLock = await PinnedReleaseTool.GetAsync("gh-actions-lock", cache);
        await BuildProcess.RunAsync(actionsLock, ["--verify"]);
        return 0;
    }
}
