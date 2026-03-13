using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodotAgent.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();
    public static readonly JsonSerializerOptions CompactOptions = Create(writeIndented: false);

    private static JsonSerializerOptions Create(bool writeIndented = true)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string SerializeCompact<T>(T value) => JsonSerializer.Serialize(value, CompactOptions);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
