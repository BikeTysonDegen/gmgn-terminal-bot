using System.Text.Json;

namespace GmgnTerminal.Core.Gmgn;

// tolerant JsonElement helpers — gmgn mixes numbers and numeric strings
internal static class Json
{
    public static string Str(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        if (!el.TryGetProperty(prop, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => ""
        };
    }

    public static decimal Dec(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0m;
        if (!el.TryGetProperty(prop, out var v)) return 0m;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m,
            _ => 0m
        };
    }

    public static long Lng(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0;
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var l) ? l : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), out var l) ? l : 0,
            _ => 0
        };
    }

    public static bool Has(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out _);

    public static JsonElement Prop(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) ? v : default;
}
