namespace Envoy.ControlPlane.Tools;

internal static class ValidationPaths
{
    internal const string AnnotationsProject = "src/Envoy.ControlPlane.Validation.Annotations/Envoy.ControlPlane.Validation.Annotations.csproj";
    internal const string EvaluatorProject = "src/Envoy.ControlPlane.Validation/Envoy.ControlPlane.Validation.csproj";
    internal const string TestsProject = "tests/Envoy.ControlPlane.Validation.Tests/Envoy.ControlPlane.Validation.Tests.csproj";
    internal const string AnnotationsRoot = "src/Envoy.ControlPlane.Validation.Annotations/obj";
    internal const string EvaluatorRoot = "src/Envoy.ControlPlane.Validation/obj";
    internal const string MetadataRoot = "src/Envoy.ControlPlane.Protocol/obj/validation";
    internal const string SourcesLock = "schemas/validation/sources.lock.json";
    internal const string BindingsLock = "schemas/validation/bindings.lock.json";
}
