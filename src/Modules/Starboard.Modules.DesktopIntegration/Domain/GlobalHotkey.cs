using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Domain;

internal readonly record struct GlobalHotkey(uint Modifiers, uint VirtualKey, string DisplayText);

internal static class GlobalHotkeyParser
{
    internal static bool TryParse(
        string? value,
        out GlobalHotkey hotkey,
        out string? failureMessage)
    {
        hotkey = default;
        failureMessage = null;

        if (string.IsNullOrWhiteSpace(value) == true)
        {
            failureMessage = "A shortcut is required.";
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            failureMessage = "A shortcut must include at least one modifier and a key.";
            return false;
        }

        uint modifiers = 0;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (TryAddModifier(parts[index], ref modifiers) == false)
            {
                failureMessage = "Only Ctrl, Alt, and Shift modifiers are supported, without duplicates.";
                return false;
            }
        }

        if (modifiers == 0 || TryParseVirtualKey(parts[parts.Length - 1], out var virtualKey) == false)
        {
            failureMessage = "The shortcut key must be A-Z, 0-9, or F1-F24.";
            return false;
        }

        hotkey = new GlobalHotkey(
            modifiers,
            virtualKey,
            CreateDisplayText(modifiers, virtualKey));
        return true;
    }

    private static bool TryAddModifier(string value, ref uint modifiers)
    {
        uint modifier = value.ToUpperInvariant() switch
        {
            "CTRL" => NativeMethods.ModifierControl,
            "ALT" => NativeMethods.ModifierAlt,
            "SHIFT" => NativeMethods.ModifierShift,
            _ => 0,
        };
        if (modifier == 0 || (modifiers & modifier) != 0)
        {
            return false;
        }

        modifiers |= modifier;
        return true;
    }

    private static bool TryParseVirtualKey(string value, out uint virtualKey)
    {
        virtualKey = 0;
        if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]) == true)
        {
            virtualKey = char.ToUpperInvariant(value[0]);
            return true;
        }

        if (value.Length < 2 || value[0] is not ('F' or 'f') ||
            int.TryParse(value.AsSpan(1), out var functionNumber) == false ||
            functionNumber < 1 || functionNumber > 24)
        {
            return false;
        }

        virtualKey = NativeMethods.VirtualKeyF1 + (uint)(functionNumber - 1);
        return true;
    }

    private static string CreateDisplayText(uint modifiers, uint virtualKey)
    {
        var components = new List<string>(4);
        if ((modifiers & NativeMethods.ModifierControl) != 0)
        {
            components.Add("Ctrl");
        }

        if ((modifiers & NativeMethods.ModifierAlt) != 0)
        {
            components.Add("Alt");
        }

        if ((modifiers & NativeMethods.ModifierShift) != 0)
        {
            components.Add("Shift");
        }

        components.Add(virtualKey >= NativeMethods.VirtualKeyF1 &&
                       virtualKey <= NativeMethods.VirtualKeyF24
            ? $"F{virtualKey - NativeMethods.VirtualKeyF1 + 1}"
            : ((char)virtualKey).ToString());
        return string.Join('+', components);
    }
}
