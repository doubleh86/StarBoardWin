using System.Drawing;
using System.Windows.Forms;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal sealed class TrayIconService : IDisposable
{
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _toggleVisibilityItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly NotifyIcon _notifyIcon;

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
        _contextMenu.Items.Add(_settingsItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = SystemIcons.Application,
            Text = "Starboard 터미널 · Ctrl+Alt+S로 호출",
            Visible = true,
        };
        _notifyIcon.MouseClick += HandleNotifyIconMouseClick;
    }

    internal event EventHandler? ToggleVisibilityRequested;

    internal event EventHandler? SummonRequested;

    internal event EventHandler? SettingsRequested;

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
        _exitItem.Click -= HandleExitClick;
        _notifyIcon.Dispose();
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
}
