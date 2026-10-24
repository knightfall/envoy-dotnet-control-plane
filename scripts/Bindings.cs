#!/usr/bin/env -S dotnet --
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:package Roslynator.Analyzers
#:include bindings/BindingPaths.cs
#:include bindings/BindingIntegrity.cs
#:include bindings/BindingGeneration.cs
#:include bindings/BufTool.cs
#:include bindings/PackageVerification.cs
#:include bindings/BuildProcess.cs
#:include validation/ValidationPaths.cs
#:include validation/VerifiedValidationSources.cs
#:include validation/ValidationGeneration.cs
#:include validation/MigrationCorrections.cs

using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class Bindings
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Require(args.Length == 3 && args[0] is "generate" or "verify" && args[1] == "--cache",
                "Usage: Bindings.cs <generate|verify> --cache <external-source-cache>");

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
