using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal sealed class TrayIconAsset : IDisposable
{
    private const string _ResourceName = "Starboard.Modules.DesktopIntegration.Assets.Starboard.ico";

    private readonly Icon? _ownedIcon;
    private readonly Stream? _ownedStream;
    private bool _isDisposed;

    private TrayIconAsset(Icon icon, Icon? ownedIcon, Stream? ownedStream)
    {
        Icon = icon;
        _ownedIcon = ownedIcon;
        _ownedStream = ownedStream;
    }

    internal Icon Icon { get; }

    internal bool IsFallback => _ownedIcon is null;

    internal static TrayIconAsset LoadEmbedded()
    {
        return Load(() => typeof(TrayIconService).Assembly.GetManifestResourceStream(_ResourceName));
    }

    internal static TrayIconAsset Load(Func<Stream?> openResourceStream)
    {
        ArgumentNullException.ThrowIfNull(openResourceStream);

        Stream? iconStream = null;
        try
        {
            iconStream = openResourceStream();
            if (iconStream is null)
            {
                Trace.TraceWarning("The embedded Starboard tray icon was not found; the application icon will be used.");
                return CreateFallback();
            }

            var icon = new Icon(iconStream);
            return new TrayIconAsset(icon, icon, iconStream);
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            iconStream?.Dispose();
            Trace.TraceWarning("The embedded Starboard tray icon could not be loaded ({0}); the application icon will be used.",
                               exception.GetType().Name);
            return CreateFallback();
        }
    }

    public void Dispose()
    {
        if (_isDisposed == true)
        {
            return;
        }

        _isDisposed = true;
        _ownedIcon?.Dispose();
        _ownedStream?.Dispose();
    }

    private static TrayIconAsset CreateFallback()
    {
        return new TrayIconAsset(SystemIcons.Application, null, null);
    }
}

internal sealed class TrayIconService : IDisposable
{
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _toggleVisibilityItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _shortcutGuideItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayIconAsset _iconAsset;

    private bool _isDisposed;

    internal TrayIconService()
    {
        _toggleVisibilityItem = new ToolStripMenuItem("터미널 숨기기")
        {
            AccessibleName = "터미널 표시 또는 숨기기",
        };
        _toggleVisibilityItem.Click += HandleToggleVisibilityClick;

        _settingsItem = new ToolStripMenuItem("설정")
        {
            AccessibleName = "Starboard 설정 열기",
        };
        _settingsItem.Click += HandleSettingsClick;

        _shortcutGuideItem = new ToolStripMenuItem("단축키 안내")
        {
            AccessibleName = "Starboard 단축키 안내 열기",
        };
        _shortcutGuideItem.Click += HandleShortcutGuideClick;

        _exitItem = new ToolStripMenuItem("종료")
        {
            AccessibleName = "Starboard 종료",
        };
        _exitItem.Click += HandleExitClick;

        _contextMenu = new ContextMenuStrip
        {
            AccessibleName = "Starboard 메뉴",
            ShowCheckMargin = false,
            ShowImageMargin = false,
        };
        _contextMenu.Items.Add(_toggleVisibilityItem);
        _contextMenu.Items.Add(_shortcutGuideItem);
        _contextMenu.Items.Add(_settingsItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_exitItem);

        var iconAsset = TrayIconAsset.LoadEmbedded();
        var notifyIcon = new NotifyIcon();
        try
        {
            notifyIcon.ContextMenuStrip = _contextMenu;
            notifyIcon.Icon = iconAsset.Icon;
            notifyIcon.Text = "Starboard 터미널 · Ctrl+Alt+S로 호출";
            notifyIcon.MouseClick += HandleNotifyIconMouseClick;
            notifyIcon.Visible = true;
        }
        catch
        {
            notifyIcon.MouseClick -= HandleNotifyIconMouseClick;
            notifyIcon.Dispose();
            iconAsset.Dispose();
            throw;
        }

        _iconAsset = iconAsset;
        _notifyIcon = notifyIcon;
    }

    internal event EventHandler? ToggleVisibilityRequested;

    internal event EventHandler? SummonRequested;

    internal event EventHandler? SettingsRequested;

    internal event EventHandler? ShortcutGuideRequested;

    internal event EventHandler? ExitRequested;

    internal void SetPanelVisible(bool isVisible)
    {
        _toggleVisibilityItem.Text = isVisible == true
            ? "터미널 숨기기"
            : "터미널 표시";
    }

    public void Dispose()
    {
        if (_isDisposed == true)
        {
            return;
        }

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= HandleNotifyIconMouseClick;
        _toggleVisibilityItem.Click -= HandleToggleVisibilityClick;
        _settingsItem.Click -= HandleSettingsClick;
        _shortcutGuideItem.Click -= HandleShortcutGuideClick;
        _exitItem.Click -= HandleExitClick;
        _notifyIcon.Dispose();
        _iconAsset.Dispose();
        _contextMenu.Dispose();
    }

    private void HandleNotifyIconMouseClick(object? sender, MouseEventArgs eventArguments)
    {
        _ = sender;

        if (eventArguments.Button == MouseButtons.Left)
        {
            SummonRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleToggleVisibilityClick(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleExitClick(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleSettingsClick(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleShortcutGuideClick(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        ShortcutGuideRequested?.Invoke(this, EventArgs.Empty);
    }
}
