using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Example;

var builder = WebApplication.CreateBuilder(args);
var cache = new SnapshotCache();
builder.Services.AddSingleton(cache);
builder.Services.AddSingleton<IConfigWatcher>(cache);
builder.Services.AddEnvoyControlPlane();
builder.Services.AddHostedService<BackendPublisher>();
var application = builder.Build();
application.MapEnvoyControlPlane();
await application.RunAsync();
