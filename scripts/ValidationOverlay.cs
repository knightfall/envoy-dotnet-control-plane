#!/usr/bin/env -S dotnet --
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:package Roslynator.Analyzers
#:project ../src/Envoy.ControlPlane.Protocol/Envoy.ControlPlane.Protocol.csproj
#:include validation/OverlayTransform.cs

using Envoy.ControlPlane.Tools;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ValidationOverlay.cs <original-descriptors> <migrated-descriptors> <output-descriptors>");
    return 1;
}
OverlayTransform.Create(args[0], args[1], args[2]);
return 0;
