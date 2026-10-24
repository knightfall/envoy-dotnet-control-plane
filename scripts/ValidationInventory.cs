#!/usr/bin/env -S dotnet --
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:package Roslynator.Analyzers
#:project ../src/Envoy.ControlPlane.Protocol/Envoy.ControlPlane.Protocol.csproj
#:include ../tests/Envoy.ControlPlane.Protocol.Tests/Support/PgvRuleInventory.cs

using System.Text.Json;
using Envoy.ControlPlane.Protocol.Tests;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Validate;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: ValidationInventory.cs <output-json>");
    return 1;
}

var registry = new ExtensionRegistry
{
    ValidateExtensions.Rules, ValidateExtensions.Required,
    ValidateExtensions.Disabled, ValidateExtensions.Ignored,
};
var assembly = typeof(FieldRules).Assembly;
using var stream = assembly.GetManifestResourceStream("Envoy.ControlPlane.Protocol.catalog.pb")
    ?? throw new InvalidOperationException("Protocol has no source descriptor set");
var source = FileDescriptorSet.Parser.WithExtensionRegistry(registry).ParseFrom(stream);
var inventory = PgvRuleInventory.Create(source);
File.WriteAllText(args[0], inventory.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Inventoried {inventory["fieldRuleCount"]} annotated fields across {inventory["fileCount"]} schemas.");
return 0;
