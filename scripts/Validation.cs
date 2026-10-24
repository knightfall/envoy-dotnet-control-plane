#!/usr/bin/env -S dotnet --
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:package Roslynator.Analyzers
#:include bindings/BindingIntegrity.cs
#:include bindings/BindingPaths.cs
#:include bindings/BuildProcess.cs
#:include bindings/PackageVerification.cs
#:include validation/ValidationPaths.cs
#:include validation/VerifiedValidationSources.cs
#:include validation/ValidationVerification.cs

using Envoy.ControlPlane.Tools;

try
{
    if (args is ["verify"])
    {
        await ValidationVerification.RunAsync();
    }
    else
    {
        Console.Error.WriteLine("Usage: Validation.cs verify (run Bindings.cs generate first)");
        return 1;
    }
    return 0;
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
