using System.Collections.ObjectModel;
using Google.Protobuf;

namespace Envoy.ControlPlane.Resources;

internal sealed class SnapshotDeltaResources
{
    internal static readonly SnapshotDeltaResources Empty = new(null);

    private readonly Dictionary<string, ByteString> payloads;

    internal SnapshotDeltaResources(IReadOnlyDictionary<string, ResourceEntry>? resources)
    {
        int count = resources?.Count ?? 0;
        payloads = new Dictionary<string, ByteString>(count, StringComparer.Ordinal);
        var versions = new Dictionary<string, string>(count, StringComparer.Ordinal);
        if (resources is not null)
        {
            foreach (var (name, entry) in resources)
            {
                var payload = ResourceSerialization.SerializeByteString(entry.Resource);
                payloads.Add(name, payload);
                versions.Add(name, ResourceSerialization.ComputeHash(payload.Span));
            }
        }
        Versions = new SnapshotResourceVersions(versions);
    }

    internal SnapshotResourceVersions Versions { get; }

    internal ByteString GetPayload(string name) => payloads[name];
}

/// <summary>Owns a privately built version map that streams can share without retaining serialized payloads.</summary>
/// <param name="versions">The ordinal dictionary transferred to this instance.</param>
internal sealed class SnapshotResourceVersions(Dictionary<string, string> versions) : ReadOnlyDictionary<string, string>(versions);
