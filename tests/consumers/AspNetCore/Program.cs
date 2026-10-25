using Envoy.ControlPlane.PackageConsumers;
using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

ConsumerChecks.Verify("Envoy.ControlPlane.AspNetCore", hosting: true);
var builder = WebApplication.CreateBuilder();
builder.Services.AddSingleton<IConfigWatcher>(new SnapshotCache());
builder.Services.AddEnvoyControlPlane();
await using var application = builder.Build();
application.MapEnvoyControlPlane();
Console.WriteLine("ASP.NET package consumer registered the engine and mapped its generated gRPC services.");
