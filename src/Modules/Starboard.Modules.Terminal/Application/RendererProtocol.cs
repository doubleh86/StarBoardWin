using System.Text.Json;

namespace Starboard.Modules.Terminal.Application;

internal static class RendererProtocol
{
    internal const int MaximumMessageLength = 1_048_576;

    internal static bool TryParse(string json, out RendererMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(json) == true ||
            json.Length > MaximumMessageLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("version", out var version) == false ||
                version.TryGetInt32(out var versionNumber) == false ||
                versionNumber != 1 ||
                root.TryGetProperty("type", out var typeElement) == false ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString();
            root.TryGetProperty("payload", out var payload);

            message = type switch
            {
                "ready" => new RendererMessage(RendererMessageType.Ready),
                "input" => ParseData(RendererMessageType.Input, payload),
                "resize" => ParseResize(payload),
                "copy" => ParseData(RendererMessageType.Copy, payload),
                "paste-request" => new RendererMessage(RendererMessageType.PasteRequest),
                "renderer-error" => new RendererMessage(RendererMessageType.RendererError),
                _ => null,
            };

            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static RendererMessage? ParseData(
        RendererMessageType type,
        JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("data", out var dataElement) == false ||
            dataElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var data = dataElement.GetString();
        if (data is null || data.Length > MaximumMessageLength)
        {
            return null;
        }

        return new RendererMessage(type, data);
    }

    private static RendererMessage? ParseResize(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("columns", out var columnsElement) == false ||
            payload.TryGetProperty("rows", out var rowsElement) == false ||
            columnsElement.TryGetInt32(out var columns) == false ||
            rowsElement.TryGetInt32(out var rows) == false ||
            columns is < 2 or > 500 ||
            rows is < 1 or > 300)
        {
            return null;
        }

        return new RendererMessage(
            RendererMessageType.Resize,
            null,
            columns,
            rows);
    }
}
