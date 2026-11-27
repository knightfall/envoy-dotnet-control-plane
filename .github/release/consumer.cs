using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Envoy.Config.Core.V3;
using Envoy.Type.V3;
using Microsoft.Extensions.DependencyInjection;
using ProtoValidate;

var services = new ServiceCollection();
services.AddSingleton<IConfigWatcher>(new SnapshotCache());
services.AddEnvoyControlPlane();

var validator = new Validator();
var validResult = validator.Validate(new Percent { Value = 50 }, failFast: false);
var invalidResult = validator.Validate(new Percent { Value = 101 }, failFast: false);
if (!validResult.IsSuccess || invalidResult.IsSuccess)
{
    throw new System.InvalidOperationException("Optional validator behavior differs");
}

var validHeader = validator.Validate(new HeaderValue { Key = "x-example", Value = "value" }, failFast: false);
var invalidHeader = validator.Validate(new HeaderValue { Key = "x-example", Value = "value\n" }, failFast: false);
if (!validHeader.IsSuccess || invalidHeader.IsSuccess)
{
    throw new System.InvalidOperationException("Packaged validator header rules differ");
}

System.Console.WriteLine("Protocol, annotations, Core, hosting and validator loaded and exercised.");
