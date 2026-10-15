using System.Text.Json;

namespace Starboard.Modules.Terminal.Presentation;

internal static class RendererInputDiagnostics
{
    internal static bool TryParse(string json, out string operation)
    {
        operation = string.Empty;
        if (json.Length > 512)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3 ||
                root.TryGetProperty("version", out var version) == false || version.ValueKind != JsonValueKind.Number ||
                version.TryGetInt32(out var versionNumber) == false || versionNumber != 2 ||
                root.TryGetProperty("type", out var type) == false || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "input-diagnostic" || root.TryGetProperty("payload", out var payload) == false ||
                payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 6)
            {
                return false;
            }

            string[] fields = ["route", "selection", "focus", "modal", "suppressed", "capture"];
            int[] limits = [5, 1, 2, 1, 1, 1];
            var values = new int[fields.Length];
            for (var index = 0; index < fields.Length; index++)
            {
                if (payload.TryGetProperty(fields[index], out var value) == false ||
                    value.ValueKind != JsonValueKind.Number || value.TryGetInt32(out values[index]) == false ||
                    values[index] < 0 || values[index] > limits[index])
                {
                    return false;
                }
            }

            // Keep all six bounded states identifiable within the file log's 64-character limit.
            operation = FormattableString.Invariant($"Input.R{values[0]}.S{values[1]}.F{values[2]}.M{values[3]}.U{values[4]}.C{values[5]}");

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
