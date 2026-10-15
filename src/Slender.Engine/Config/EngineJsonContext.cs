using System.Text.Json.Serialization;

namespace Slender.Engine.Config;

/// <summary>Source-generated JSON metadata for config.json (no reflection; NativeAOT and trim safe).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Converters = [typeof(ActionSpecJsonConverter)])]
[JsonSerializable(typeof(JsonConfigStore.ConfigDto))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}

/// <summary>Source-generated JSON metadata for status.json.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SlenderStatus))]
internal sealed partial class StatusJsonContext : JsonSerializerContext
{
}
