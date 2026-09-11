namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// An absolute web address that has passed the terminal's external-open policy.
/// </summary>
public sealed record TerminalUrlOpenTarget
{
    public const int MaximumLength = 2_048;

    private TerminalUrlOpenTarget(string absoluteUri)
    {
        AbsoluteUri = absoluteUri;
    }

    public string AbsoluteUri { get; }

    public static bool TryCreate(string? value, out TerminalUrlOpenTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(value) == true || value.Length > MaximumLength)
        {
            return false;
        }

        if (value.Any(char.IsControl) == true ||
            Uri.IsWellFormedUriString(value, UriKind.Absolute) == false ||
            Uri.TryCreate(value, UriKind.Absolute, out var uri) == false)
        {
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) == false &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) == false)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host) == true || string.IsNullOrEmpty(uri.UserInfo) == false)
        {
            return false;
        }

        var absoluteUri = uri.AbsoluteUri;
        if (absoluteUri.Length > MaximumLength)
        {
            return false;
        }

        target = new TerminalUrlOpenTarget(absoluteUri);
        return true;
    }
}
