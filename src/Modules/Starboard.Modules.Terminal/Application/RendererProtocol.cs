using System.Text.Json;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal static class RendererProtocol
{
    private static readonly HashSet<string> _globalHostMessageTypes =
    [
        "initialize",
        "apply-appearance",
    ];

    private static readonly HashSet<string> _sessionHostMessageTypes =
    [
        "session-upsert",
        "activate-session",
        "output",
        "paste",
        "reset",
        "remove-session",
        "session-error",
    ];

    internal const int CurrentVersion = 2;
    internal const int MaximumMessageLength = 1_048_576;

    internal static string SerializeGlobalMessage(string type, object payload)
    {
        ValidateType(type, _globalHostMessageTypes);
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(
            new
            {
                version = CurrentVersion,
                type,
                payload,
            },
            JsonSerializerOptions.Web);
    }

    internal static string SerializeSessionMessage(
        string type,
        TerminalSessionId sessionId,
        object payload)
    {
        ValidateType(type, _sessionHostMessageTypes);
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "The renderer session identifier cannot be empty.",
                nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(
            new
            {
                version = CurrentVersion,
                type,
                sessionId = sessionId.ToString(),
                payload,
            },
            JsonSerializerOptions.Web);
    }

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
                versionNumber != CurrentVersion ||
                root.TryGetProperty("type", out var typeElement) == false ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString();
            root.TryGetProperty("payload", out var payload);

            message = type switch
            {
                "ready" => ParseGlobal(RendererMessageType.Ready, root, payload),
                "new-tab" => ParseGlobal(RendererMessageType.NewTab, root, payload),
                "select-session" => ParseSession(
                    RendererMessageType.SelectSession,
                    root,
                    payload),
                "select-next" => ParseGlobal(
                    RendererMessageType.SelectNext,
                    root,
                    payload),
                "select-previous" => ParseGlobal(
                    RendererMessageType.SelectPrevious,
                    root,
                    payload),
                "input" => ParseData(RendererMessageType.Input, root, payload),
                "resize" => ParseResize(root, payload),
                "copy" => ParseData(RendererMessageType.Copy, root, payload),
                "paste-request" => ParseSession(
                    RendererMessageType.PasteRequest,
                    root,
                    payload),
                "close-session" => ParseSession(
                    RendererMessageType.CloseSession,
                    root,
                    payload),
                "restart-session" => ParseSession(
                    RendererMessageType.RestartSession,
                    root,
                    payload),
                "session-error" => ParseSession(
                    RendererMessageType.SessionError,
                    root,
                    payload),
                "renderer-error" => ParseGlobal(
                    RendererMessageType.RendererError,
                    root,
                    payload),
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
        JsonElement root,
        JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false)
        {
            return null;
        }

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

        return new RendererMessage(type, sessionId, data);
    }

    private static RendererMessage? ParseResize(
        JsonElement root,
        JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false)
        {
            return null;
        }

        if (payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("columns", out var columnsElement) == false ||
            payload.TryGetProperty("rows", out var rowsElement) == false ||
            columnsElement.TryGetInt32(out var columns) == false ||
            rowsElement.TryGetInt32(out var rows) == false ||
            columns < 2 ||
            columns > 500 ||
            rows < 1 ||
            rows > 300)
        {
            return null;
        }

        return new RendererMessage(
            RendererMessageType.Resize,
            sessionId,
            null,
            columns,
            rows);
    }

    private static RendererMessage? ParseGlobal(
        RendererMessageType type,
        JsonElement root,
        JsonElement payload)
    {
        if (root.TryGetProperty("sessionId", out _) == true ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new RendererMessage(type);
    }

    private static RendererMessage? ParseSession(
        RendererMessageType type,
        JsonElement root,
        JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new RendererMessage(type, sessionId);
    }

    private static bool TryParseSessionId(
        JsonElement root,
        out TerminalSessionId sessionId)
    {
        sessionId = default;
        if (root.TryGetProperty("sessionId", out var sessionIdElement) == false ||
            sessionIdElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = sessionIdElement.GetString();
        if (Guid.TryParseExact(value, "N", out var identifier) == false ||
            identifier == Guid.Empty)
        {
            return false;
        }

        sessionId = new TerminalSessionId(identifier);
        return true;
    }

    private static void ValidateType(
        string type,
        HashSet<string> allowedTypes)
    {
        if (string.IsNullOrWhiteSpace(type) == true)
        {
            throw new ArgumentException(
                "The renderer message type cannot be empty.",
                nameof(type));
        }

        if (allowedTypes.Contains(type) == false)
        {
            throw new ArgumentException(
                "The renderer message type is not valid for this message scope.",
                nameof(type));
        }
    }
}
