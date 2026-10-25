using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class Bindings
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Require(args.Length == 3 && args[0] is "generate" or "verify" && args[1] == "--cache",
                "Usage: bindings <generate|verify> --cache <external-source-cache>");

            string sourceCache = Path.GetFullPath(args[2]);
            await BindingGeneration.GenerateAsync(sourceCache);

            if (args[0] == "verify")
            {
                await PackageVerification.BuildPackageAsync();
                await PackageVerification.RunTestsAsync(BindingPaths.ProtocolTests, "protocol");
                await PackageVerification.VerifyCleanConsumerAsync();
            }

            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
