using System.Text.Json;
using System.Text.Json.Serialization;


namespace Slender.Engine.Config;

/// <summary>Serializes ActionSpec with a "type" discriminator. Unknown types throw FormatException.</summary>
public sealed class ActionSpecJsonConverter : JsonConverter<ActionSpec>
{
    public override ActionSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        return Parse(doc.RootElement, allowTapHold: true);
    }

    private static ActionSpec Parse(JsonElement root, bool allowTapHold)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Action must be a JSON object.");
        }

        string type = ReadString(root, "type");
        return type switch
        {
            "none" => new ActionSpec.None(),
            "keys" => new ActionSpec.Keys(ReadString(root, "chord")),
            "excel" => new ActionSpec.Excel(ParseCommand(ReadString(root, "command"))),
            "mouseButton" => new ActionSpec.MouseButton(ReadString(root, "button")),
            "actionsRing" => new ActionSpec.ActionsRing(),
            "launch" => new ActionSpec.Launch(ReadString(root, "target"), ReadOptionalString(root, "arguments")),
            "disabled" => new ActionSpec.Disabled(),
            "brightness" => new ActionSpec.Brightness(ReadInt(root, "step")),
            "text" => new ActionSpec.Text(ReadString(root, "value")),
            "toggleThumbWheel" => new ActionSpec.ToggleThumbWheelMode(),
            "tapHold" when allowTapHold => new ActionSpec.TapHold(ReadNested(root, "tap"), ReadNested(root, "hold")),
            "tapHold" => throw new FormatException("A tapHold action cannot contain another tapHold."),
            _ => throw new FormatException($"Unknown action type '{type}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, ActionSpec value, JsonSerializerOptions options) => WriteAction(writer, value, allowTapHold: true);

    private static void WriteAction(Utf8JsonWriter writer, ActionSpec value, bool allowTapHold)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case ActionSpec.None:
                writer.WriteString("type", "none");
                break;
            case ActionSpec.Keys keys:
                writer.WriteString("type", "keys");
                writer.WriteString("chord", keys.Chord);
                break;
            case ActionSpec.Excel excel:
                writer.WriteString("type", "excel");
                writer.WriteString("command", excel.Command.ToString());
                break;
            case ActionSpec.MouseButton mouse:
                writer.WriteString("type", "mouseButton");
                writer.WriteString("button", mouse.Button);
                break;
            case ActionSpec.ActionsRing:
                writer.WriteString("type", "actionsRing");
                break;
            case ActionSpec.Launch launch:
                writer.WriteString("type", "launch");
                writer.WriteString("target", launch.Target);
                if (launch.Arguments is not null) writer.WriteString("arguments", launch.Arguments);
                break;
            case ActionSpec.Disabled:
                writer.WriteString("type", "disabled");
                break;
            case ActionSpec.Brightness brightness:
                writer.WriteString("type", "brightness");
                writer.WriteNumber("step", brightness.StepPercent);
                break;
            case ActionSpec.Text text:
                writer.WriteString("type", "text");
                writer.WriteString("value", text.Value);
                break;
            case ActionSpec.ToggleThumbWheelMode:
                writer.WriteString("type", "toggleThumbWheel");
                break;
            case ActionSpec.TapHold tapHold:
                if (!allowTapHold) throw new NotSupportedException("A tapHold action cannot contain another tapHold.");
                writer.WriteString("type", "tapHold");
                writer.WritePropertyName("tap");
                WriteAction(writer, tapHold.Tap, allowTapHold: false);
                writer.WritePropertyName("hold");
                WriteAction(writer, tapHold.Hold, allowTapHold: false);
                break;
            default:
                throw new NotSupportedException($"Unsupported action {value.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    private static ActionSpec ReadNested(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out JsonElement el))
        {
            return Parse(el, allowTapHold: false);
        }

        throw new FormatException($"Missing action property '{name}'.");
    }

    private static string ReadString(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString() ?? throw new FormatException($"'{name}' is null.");
        }

        throw new FormatException($"Missing string property '{name}'.");
    }

    private static int ReadInt(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v))
        {
            return v;
        }

        throw new FormatException($"Missing integer property '{name}'.");
    }

    private static string? ReadOptionalString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static ExcelCommand ParseCommand(string text)
    {
        if (Enum.TryParse(text, ignoreCase: false, out ExcelCommand command) && Enum.IsDefined(command))
        {
            return command;
        }

        throw new FormatException($"Unknown Excel command '{text}'.");
    }
}
