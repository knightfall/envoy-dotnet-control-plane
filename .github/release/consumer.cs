using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
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

System.Console.WriteLine("Protocol, annotations, Core, hosting and validator loaded and exercised.");
