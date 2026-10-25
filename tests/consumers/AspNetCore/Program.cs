using Envoy.ControlPlane.PackageConsumers;
using Microsoft.AspNetCore.Builder;

ConsumerChecks.Verify("Envoy.ControlPlane.AspNetCore", hosting: true);
await using var application = WebApplication.CreateBuilder().Build();
Console.WriteLine("ASP.NET shared-framework consumer built its application host.");
