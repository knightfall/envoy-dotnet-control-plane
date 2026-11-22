namespace Envoy.ControlPlane.Cache;

public sealed partial class SnapshotCache
{
    private readonly Dictionary<string, NodePublication> publications = new(StringComparer.Ordinal);

    private async ValueTask<NodePublication> EnterPublicationAsync(string nodeId, CancellationToken cancellationToken)
    {
        NodePublication publication;
        lock (gate)
        {
            if (!publications.TryGetValue(nodeId, out publication!))
            {
                publication = new NodePublication();
                publications.Add(nodeId, publication);
            }
            publication.Users++;
        }
        try
        {
            // An uncontended setter retains the install-before-cancellation behavior.
            if (!publication.Semaphore.Wait(0))
            {
                await publication.Semaphore.WaitAsync(cancellationToken);
            }
            return publication;
        }
        catch
        {
            ReleasePublicationReference(nodeId, publication);
            throw;
        }
    }

    private void ExitPublication(string nodeId, NodePublication publication)
    {
        publication.Semaphore.Release();
        ReleasePublicationReference(nodeId, publication);
    }

    private void ReleasePublicationReference(string nodeId, NodePublication publication)
    {
        lock (gate)
        {
            if (--publication.Users == 0)
            {
                publications.Remove(nodeId);
                publication.Semaphore.Dispose();
            }
        }
    }

    private sealed class NodePublication
    {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Counts the current writer and queued callers independently of the cache index.</summary>
        internal int Users { get; set; }
    }
}
