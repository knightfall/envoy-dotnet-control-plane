using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.Tools;

internal static class UpstreamData
{
    internal static readonly JsonSerializerOptions SerializationOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    internal static string GetString(JsonNode node, string key) => node[key]?.GetValue<string>() ?? throw new InvalidDataException("Missing lock field: " + key);
    internal static JsonArray GetArray(JsonNode node, string key) => node[key] as JsonArray ?? throw new InvalidDataException("Missing array: " + key);
    internal static JsonObject ReadJsonObject(string path) => JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Invalid JSON: " + path);
    internal static string ComputeSha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string DecodeUtf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);
    internal static byte[] SerializeJson(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString(SerializationOptions) + "\n");
    internal static JsonArray ToJsonArray(IEnumerable<string> values) => new(values.Select(v => JsonValue.Create(v)).ToArray());
}
