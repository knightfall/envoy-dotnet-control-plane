using System.Text;
using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.Tools;

internal static class TransportSummary
{
    internal static string Format(JsonObject environment, JsonArray checks, JsonArray measurements)
    {
        var text = new StringBuilder("# gRPC/Kestrel transport probe\n\n");
        text.AppendLine($"Revision: `{environment["revision"]}`. OS: {environment["os"]}; architecture: {environment["architecture"]}.")
            .AppendLine($"Runtime processor hint: {environment["runtimeProcessorHint"]}; host logical processors: {environment["hostProcessors"]}. No CPU affinity or quota is applied.\n")
            .AppendLine("| Transport | Mode | Functional checks passed |")
            .AppendLine("|---|---|---:|");
        foreach (var group in checks.OfType<JsonObject>().GroupBy(row => (Server: Value<string>(row, "server"), Mode: Value<string>(row, "mode"))))
        {
            text.AppendLine($"| {group.Key.Server} | {group.Key.Mode} | {group.Count()} |");
        }
        if (measurements.Count > 0)
        {
            text.AppendLine("\nThree alternating two-second trials per row, after 100 warm-up exchanges per stream. One reused stream per worker; uncompressed responses. TLS handshakes and warm-up are excluded. Latency is sampled every tenth exchange.\n")
                .AppendLine("| Mode | Transport | Payload bytes | Streams / connections | Responses/s median [min, max] | p95 µs median | Server CPU µs/response | Runtime allocated B/response |")
                .AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (var group in measurements.OfType<JsonObject>().GroupBy(row =>
                (Mode: Value<string>(row, "mode"), Size: Value<int>(row, "size"), Streams: Value<int>(row, "streams"),
                    Connections: Value<int>(row, "connections"), Server: Value<string>(row, "server"))))
            {
                var throughput = group.Select(row => Value<double>(row, "responsesPerSecond")).Order().ToArray();
                double latency = Median(group.Select(row => Value<double>(row, "p95us")));
                double cpu = Median(group.Select(row => Value<double>(row, "serverCpuSeconds") * 1e6 / Value<int>(row, "operations")));
                double allocation = Median(group.Select(row => Value<double>(row, "allocatedBytesPerResponse")));
                text.AppendLine(FormattableString.Invariant($"| {group.Key.Mode} | {group.Key.Server} | {group.Key.Size} | {group.Key.Streams} / {group.Key.Connections} | {throughput[1]:F0} [{throughput[0]:F0}, {throughput[2]:F0}] | {latency:F1} | {cpu:F1} | {allocation:F0} |"));
            }
        }
        text.AppendLine("\nThese are same-run loopback diagnostics, not production capacity, an SLO, a retained-memory measurement or full xDS parity. Runtime allocation counters have different accounting boundaries and exclude native allocations. Process CPU includes runtime/background work. No performance threshold gates this job. Raw checks, trials, runtime settings and logs accompany this summary.");
        return text.ToString();
    }

    private static T Value<T>(JsonObject row, string name) => row[name]!.GetValue<T>();

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }
}
