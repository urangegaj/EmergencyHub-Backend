using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf.Reflection;

namespace Gateway.Extensions;

public sealed class ProtoEnumJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly Dictionary<string, T> NameToValue = BuildNameToValue();
    private static readonly Dictionary<T, string> ValueToName = BuildValueToName();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return (T)Enum.ToObject(typeof(T), reader.GetInt32());

        var s = reader.GetString();
        if (s is not null && NameToValue.TryGetValue(s, out var value))
            return value;

        if (s is not null && Enum.TryParse<T>(s, ignoreCase: true, out var parsed))
            return parsed;

        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ValueToName.TryGetValue(value, out var name) ? name : value.ToString());
    }

    private static Dictionary<string, T> BuildNameToValue()
    {
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<T>())
        {
            var value = (T)Enum.Parse(typeof(T), name);
            var attr  = typeof(T).GetField(name)?.GetCustomAttribute<OriginalNameAttribute>();
            map[attr?.Name ?? name] = value;
            map[name] = value;
        }
        return map;
    }

    private static Dictionary<T, string> BuildValueToName()
    {
        var map = new Dictionary<T, string>();
        foreach (var name in Enum.GetNames<T>())
        {
            var value = (T)Enum.Parse(typeof(T), name);
            var attr  = typeof(T).GetField(name)?.GetCustomAttribute<OriginalNameAttribute>();
            map[value] = attr?.Name ?? name;
        }
        return map;
    }
}

public sealed class ProtoEnumJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (!type.IsEnum) return false;
        return Enum.GetNames(type)
            .Any(n => type.GetField(n)?.GetCustomAttribute<OriginalNameAttribute>() is not null);
    }

    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
    {
        var converterType = typeof(ProtoEnumJsonConverter<>).MakeGenericType(type);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

public static class ProtoEnumParse
{
    public static bool TryParse<T>(string? input, out T value) where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        foreach (var name in Enum.GetNames<T>())
        {
            var attr = typeof(T).GetField(name)?.GetCustomAttribute<OriginalNameAttribute>();
            if (attr?.Name.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
            {
                value = (T)Enum.Parse(typeof(T), name);
                return true;
            }
        }
        return Enum.TryParse(input, ignoreCase: true, out value);
    }
}
