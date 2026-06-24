using System.Text.Json;

namespace GmgnTerminal.Core.Gmgn;

// gmgn wraps everything in {"code":0,"msg"|"message":"success","data":...}
// api/v1 uses reason/message, defi/quotation uses msg — accept both.
public static class GmgnEnvelope
{
    public static bool TryGetData(JsonElement root, out JsonElement data, out string error)
    {
        data = default;
        error = "";

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "response root is not an object";
            return false;
        }

        var code = (int)Json.Lng(root, "code");
        if (code != 0)
        {
            var msg = Json.Str(root, "msg");
            if (string.IsNullOrEmpty(msg)) msg = Json.Str(root, "message");
            if (string.IsNullOrEmpty(msg)) msg = Json.Str(root, "reason");
            error = $"code {code}: {msg}";
            return false;
        }

        if (!root.TryGetProperty("data", out data))
        {
            error = "no data field";
            return false;
        }
        if (data.ValueKind == JsonValueKind.Null)
        {
            error = "data is null";
            return false;
        }
        return true;
    }
}
