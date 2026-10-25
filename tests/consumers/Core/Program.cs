using Envoy.ControlPlane.PackageConsumers;
using Envoy.ControlPlane.Resources;
using Envoy.Config.Listener.V3;
using Google.Protobuf;

ConsumerChecks.Verify("Envoy.ControlPlane", hosting: false);
var listener = new Listener { Name = "consumer-listener" };
var snapshot = new Snapshot("consumer-version", new Dictionary<string, IEnumerable<IMessage?>>
{
    [ResourceTypeUrls.Listener] = [listener],
});
snapshot.EnsureConsistent();
snapshot.ConstructVersionMap();
if (snapshot.GetVersion(ResourceTypeUrls.Listener) != "consumer-version"
    || !ReferenceEquals(listener, snapshot.GetResources(ResourceTypeUrls.Listener)!["consumer-listener"])
    || snapshot.GetVersionMap(ResourceTypeUrls.Listener)!["consumer-listener"].Length != 64)
{
    throw new InvalidOperationException("Core snapshot package behavior failed");
}
Console.WriteLine("Core package consumer constructed a stable snapshot and explicit Delta versions.");
