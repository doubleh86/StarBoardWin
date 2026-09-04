using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Presentation;

internal partial class TerminalView : UserControl, IAsyncDisposable
{
    private const string RendererHostName = "starboard.local";
    private const int MaximumOutputBatchLength = 65_536;
    private const int MaximumPendingOutputLength = 4 * 1024 * 1024;

    private readonly IDiagnosticLog diagnosticLog;
    private readonly TerminalSessionCoordinator sessionCoordinator;
    private readonly Lock outputLock = new();
    private readonly Dictionary<TerminalSessionId, StringBuilder> pendingOutput = [];
    private readonly HashSet<TerminalSessionId> outputFlushScheduled = [];
    private readonly HashSet<TerminalSessionId> outputOverflowReported = [];
    private readonly HashSet<TerminalSessionId> rendererSessionIds = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();

    private TaskCompletionSource rendererReady = CreateCompletionSource();
    private TerminalOptions? options;
    private bool rendererFailed;
    private bool isDisposed;
    private int columns = 80;
    private int rows = 24;

    internal TerminalView(
        IDiagnosticLog diagnosticLog,
        TerminalSessionCoordinator sessionCoordinator)
    {
        this.diagnosticLog = diagnosticLog;
        this.sessionCoordinator = sessionCoordinator;
        sessionCoordinator.WorkspaceChanged += Session_WorkspaceChanged;
        sessionCoordinator.OutputReceived += Session_OutputReceived;
        sessionCoordinator.SessionExited += Session_Exited;
        InitializeComponent();
        var canvas = ((SolidColorBrush)FindResource("CanvasBrush")).Color;
        SetRendererBackground(canvas);
    }

    internal async Task StartAsync(
        TerminalOptions terminalOptions,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        options = terminalOptions;
        ApplyTheme(terminalOptions.Theme);
        ShowLoading("로컬 renderer와 shell session을 준비하고 있습니다.");

        try
        {
            await InitializeRendererAsync(cancellationToken);
            await StartWorkspaceAsync(cancellationToken);
            ShowTerminal();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException or COMException or Win32Exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Error,
                "Terminal",
                "Start",
                "The terminal session could not be started.",
                exception);
            if (Renderer.CoreWebView2 is not null &&
                sessionCoordinator.Snapshot.Tabs.Count > 0)
            {
                ShowTerminal();
            }
            else
            {
                ShowError(ToUserMessage(exception));
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (isDisposed == true)
        {
            return ValueTask.CompletedTask;
        }

        isDisposed = true;
        lifetimeCancellation.Cancel();
        sessionCoordinator.WorkspaceChanged -= Session_WorkspaceChanged;
        sessionCoordinator.OutputReceived -= Session_OutputReceived;
        sessionCoordinator.SessionExited -= Session_Exited;
        Renderer.Dispose();
        lifetimeCancellation.Dispose();

        return ValueTask.CompletedTask;
    }

    private async Task InitializeRendererAsync(CancellationToken cancellationToken)
    {
        if (Renderer.CoreWebView2 is not null)
        {
            return;
        }

        var rendererDirectory = Path.Combine(AppContext.BaseDirectory, "Renderer");
        var indexPath = Path.Combine(rendererDirectory, "index.html");
        if (File.Exists(indexPath) == false)
        {
            throw new FileNotFoundException(
                "번들된 terminal renderer를 찾을 수 없습니다.",
                indexPath);
        }

        var userDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Starboard",
            "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(
            null,
            userDataDirectory,
            null);

        await Renderer.EnsureCoreWebView2Async(environment);
        var core = Renderer.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 initialization returned no core instance.");
        ConfigureRenderer(core);
        core.Navigate($"https://{RendererHostName}/index.html");

        await rendererReady.Task.WaitAsync(cancellationToken);
        SendInitializeMessage();
        rendererSessionIds.Clear();
        SyncWorkspace(sessionCoordinator.Snapshot);
    }

    private void ConfigureRenderer(CoreWebView2 core)
    {
        var rendererDirectory = Path.Combine(AppContext.BaseDirectory, "Renderer");
        core.SetVirtualHostNameToFolderMapping(
            RendererHostName,
            rendererDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);

        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.WebMessageReceived += Core_WebMessageReceived;
        core.NavigationStarting += Core_NavigationStarting;
        core.NewWindowRequested += static (_, eventArgs) => eventArgs.Handled = true;
        core.DownloadStarting += static (_, eventArgs) => eventArgs.Cancel = true;
        core.PermissionRequested += static (_, eventArgs) =>
        {
            eventArgs.State = CoreWebView2PermissionState.Deny;
            eventArgs.Handled = true;
        };
        core.ProcessFailed += Core_ProcessFailed;
    }

    private async Task StartWorkspaceAsync(CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new InvalidOperationException("Terminal options are unavailable.");
        }

        var shell = ShellResolver.Resolve(options.ShellExecutable);
        await sessionCoordinator.StartAsync(
            shell,
            columns,
            rows,
            cancellationToken);
    }

    private void Core_WebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        _ = sender;
        var json = eventArgs.WebMessageAsJson;
        if (RendererProtocol.TryParse(json, out var message) == false || message is null)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "RendererMessage",
                "A malformed or unsupported renderer message was ignored.");
            return;
        }

        switch (message.Type)
        {
            case RendererMessageType.Ready:
                rendererReady.TrySetResult();
                break;
            case RendererMessageType.NewTab:
                _ = AddSessionAsync();
                break;
            case RendererMessageType.SelectSession:
                SelectSession(message.SessionId);
                break;
            case RendererMessageType.SelectNext:
                SelectAdjacentSession(selectPrevious: false);
                break;
            case RendererMessageType.SelectPrevious:
                SelectAdjacentSession(selectPrevious: true);
                break;
            case RendererMessageType.Input:
                if (message.SessionId is { } inputSessionId)
                {
                    _ = WriteInputAsync(
                        inputSessionId,
                        message.Data ?? string.Empty);
                }
                break;
            case RendererMessageType.Resize:
                columns = message.Columns;
                rows = message.Rows;
                if (message.SessionId is { } resizeSessionId)
                {
                    ResizeSession(
                        resizeSessionId,
                        message.Columns,
                        message.Rows);
                }
                break;
            case RendererMessageType.Copy:
                if (message.SessionId is { } copySessionId &&
                    ContainsSession(copySessionId) == true)
                {
                    CopyToClipboard(message.Data ?? string.Empty);
                }
                break;
            case RendererMessageType.PasteRequest:
                if (message.SessionId is { } pasteSessionId &&
                    ContainsSession(pasteSessionId) == true)
                {
                    PasteFromClipboard(pasteSessionId);
                }
                break;
            case RendererMessageType.CloseSession:
                if (message.SessionId is { } closeSessionId)
                {
                    _ = CloseSessionAsync(closeSessionId);
                }
                break;
            case RendererMessageType.RestartSession:
                if (message.SessionId is { } restartSessionId)
                {
                    _ = RestartSessionAsync(restartSessionId);
                }
                break;
            case RendererMessageType.SessionError:
                if (message.SessionId is { } failedSessionId &&
                    ContainsSession(failedSessionId) == true)
                {
                    diagnosticLog.Write(
                        DiagnosticLevel.Warning,
                        "Terminal",
                        "RendererSession",
                        $"Renderer state failed for terminal session {failedSessionId}.");
                }
                break;
            case RendererMessageType.RendererError:
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "Terminal",
                    "RendererRuntime",
                    "The terminal renderer reported a local runtime error.");
                break;
            default:
                throw new InvalidOperationException("Unexpected renderer message type.");
        }
    }

    private bool ContainsSession(TerminalSessionId sessionId)
    {
        return sessionCoordinator.Snapshot.Tabs.Any(
            tab => tab.SessionId == sessionId);
    }

    private void Core_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        _ = sender;
        var allowedPrefix = $"https://{RendererHostName}/";
        if (eventArgs.Uri.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase) == false)
        {
            eventArgs.Cancel = true;
        }
    }

    private void Core_ProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        _ = sender;
        diagnosticLog.Write(
            DiagnosticLevel.Error,
            "Terminal",
            "RendererProcess",
            $"The renderer process failed ({eventArgs.ProcessFailedKind}).");
        rendererFailed = true;
        ShowError("Terminal renderer가 중단됐습니다. shell session은 유지되며 renderer를 다시 연결할 수 있습니다.");
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        if (options is null || isDisposed == true)
        {
            return;
        }

        var reconnectRendererOnly = rendererFailed &&
                                    sessionCoordinator.Snapshot.Tabs.Count > 0;
        RetryButton.IsEnabled = false;
        ShowLoading(
            reconnectRendererOnly
                ? "terminal renderer를 다시 연결하고 있습니다."
                : "shell session을 다시 시작하고 있습니다.");

        try
        {
            if (Renderer.CoreWebView2 is null)
            {
                rendererReady = CreateCompletionSource();
                await InitializeRendererAsync(lifetimeCancellation.Token);
            }
            else if (rendererFailed == true)
            {
                rendererReady = CreateCompletionSource();
                rendererSessionIds.Clear();
                Renderer.CoreWebView2.Navigate($"https://{RendererHostName}/index.html");
                await rendererReady.Task.WaitAsync(lifetimeCancellation.Token);
                rendererFailed = false;
                SendInitializeMessage();
                SyncWorkspace(sessionCoordinator.Snapshot);
            }

            if (reconnectRendererOnly == true)
            {
                ShowTerminal();
                return;
            }

            var activeSessionId = sessionCoordinator.Snapshot.ActiveSessionId;
            if (activeSessionId is null)
            {
                await StartWorkspaceAsync(lifetimeCancellation.Token);
            }
            else
            {
                SendSessionMessage("reset", activeSessionId.Value, new { });
                await sessionCoordinator.RestartAsync(
                    activeSessionId.Value,
                    lifetimeCancellation.Token);
            }

            ShowTerminal();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException or COMException or Win32Exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Error,
                "Terminal",
                "Restart",
                "The terminal session could not be restarted.",
                exception);
            ShowError(ToUserMessage(exception));
        }
        finally
        {
            RetryButton.IsEnabled = true;
        }
    }

    private async Task AddSessionAsync()
    {
        if (isDisposed == true)
        {
            return;
        }

        try
        {
            await sessionCoordinator.AddAsync(lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException or Win32Exception or UnauthorizedAccessException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "Terminal",
                    "AddSession",
                    "A terminal tab could not be added.",
                    exception);
            }
        }
    }

    private void SelectSession(TerminalSessionId? sessionId)
    {
        if (sessionId is null || isDisposed == true)
        {
            return;
        }

        try
        {
            _ = sessionCoordinator.Select(sessionId.Value);
        }
        catch (InvalidOperationException exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "SelectSession",
                "A terminal tab could not be selected.",
                exception);
        }
    }

    private void SelectAdjacentSession(bool selectPrevious)
    {
        if (isDisposed == true)
        {
            return;
        }

        try
        {
            if (selectPrevious == true)
            {
                _ = sessionCoordinator.SelectPrevious();
                return;
            }

            _ = sessionCoordinator.SelectNext();
        }
        catch (InvalidOperationException exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "SelectSession",
                "A terminal tab could not be selected.",
                exception);
        }
    }

    private async Task CloseSessionAsync(TerminalSessionId sessionId)
    {
        if (isDisposed == true)
        {
            return;
        }

        try
        {
            _ = await sessionCoordinator.CloseAsync(
                sessionId,
                lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "Terminal",
                    "CloseSession",
                    $"Terminal session {sessionId} could not be closed.",
                    exception);
                SendSessionMessage(
                    "session-error",
                    sessionId,
                    new { message = "탭을 닫지 못했습니다." });
            }
        }
    }

    private async Task RestartSessionAsync(TerminalSessionId sessionId)
    {
        if (isDisposed == true)
        {
            return;
        }

        SendSessionMessage("reset", sessionId, new { });
        try
        {
            await sessionCoordinator.RestartAsync(
                sessionId,
                lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException or Win32Exception or UnauthorizedAccessException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "Terminal",
                    "RestartSession",
                    $"Terminal session {sessionId} could not be restarted.",
                    exception);
                SendSessionMessage(
                    "session-error",
                    sessionId,
                    new { message = "shell session을 다시 시작하지 못했습니다." });
            }
        }
    }

    private async Task WriteInputAsync(
        TerminalSessionId sessionId,
        string data)
    {
        if (isDisposed == true)
        {
            return;
        }

        try
        {
            await sessionCoordinator.WriteAsync(
                sessionId,
                data,
                lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "Terminal",
                    "WriteInput",
                    "Terminal input could not be delivered.",
                    exception);
                SendSessionMessage(
                    "session-error",
                    sessionId,
                    new { message = "shell 입력 연결이 끊겼습니다. 다시 시작해 주세요." });
            }
        }
    }

    private void ResizeSession(
        TerminalSessionId sessionId,
        int requestedColumns,
        int requestedRows)
    {
        try
        {
            _ = sessionCoordinator.Resize(
                sessionId,
                requestedColumns,
                requestedRows);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "Resize",
                "The ConPTY resize request was rejected.",
                exception);
        }
    }

    private void Session_WorkspaceChanged(TerminalWorkspaceSnapshot snapshot)
    {
        if (isDisposed == true)
        {
            return;
        }

        if (Dispatcher.CheckAccess() == true)
        {
            SyncWorkspace(snapshot);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => SyncWorkspace(snapshot));
    }

    private void SyncWorkspace(TerminalWorkspaceSnapshot snapshot)
    {
        if (isDisposed == true ||
            rendererFailed == true ||
            Renderer.CoreWebView2 is null)
        {
            return;
        }

        var currentSessionIds = snapshot.Tabs
            .Select(tab => tab.SessionId)
            .ToHashSet();
        var removedSessionIds = rendererSessionIds
            .Where(sessionId => currentSessionIds.Contains(sessionId) == false)
            .ToArray();

        foreach (var sessionId in removedSessionIds)
        {
            SendSessionMessage("remove-session", sessionId, new { });
            RemovePendingOutput(sessionId);
        }

        var canAddSession = snapshot.Tabs.Count < TerminalTabRegistry.DefaultMaximumTabs;
        for (var index = 0; index < snapshot.Tabs.Count; index++)
        {
            var tab = snapshot.Tabs[index];
            SendSessionMessage(
                "session-upsert",
                tab.SessionId,
                new
                {
                    tab.Name,
                    state = tab.State.ToString().ToLowerInvariant(),
                    tab.ExitCode,
                    order = index,
                    canAddSession,
                });

            if (tab.State == TerminalSessionState.Exited)
            {
                SendSessionMessage(
                    "session-error",
                    tab.SessionId,
                    new
                    {
                        message = tab.ExitCode is null
                            ? "shell이 종료됐습니다."
                            : $"shell이 종료됐습니다 (exit {tab.ExitCode.Value}).",
                    });
            }
            else if (tab.State == TerminalSessionState.Failed)
            {
                SendSessionMessage(
                    "session-error",
                    tab.SessionId,
                    new { message = "shell session을 시작하거나 유지하지 못했습니다." });
            }
        }

        if (snapshot.ActiveSessionId is { } activeSessionId)
        {
            SendSessionMessage("activate-session", activeSessionId, new { });
        }

        if (rendererFailed == false)
        {
            rendererSessionIds.Clear();
            rendererSessionIds.UnionWith(currentSessionIds);
        }
    }

    private void RemovePendingOutput(TerminalSessionId sessionId)
    {
        lock (outputLock)
        {
            pendingOutput.Remove(sessionId);
            outputFlushScheduled.Remove(sessionId);
            outputOverflowReported.Remove(sessionId);
        }
    }

    private void Session_OutputReceived(TerminalSessionOutput output)
    {
        var data = output.Data;
        var reportOverflow = false;
        var scheduleFlush = false;

        lock (outputLock)
        {
            if (pendingOutput.TryGetValue(output.SessionId, out var sessionOutput) == false)
            {
                sessionOutput = new StringBuilder();
                pendingOutput.Add(output.SessionId, sessionOutput);
            }

            if (data.Length >= MaximumPendingOutputLength)
            {
                sessionOutput.Clear();
                sessionOutput.Append(data.AsSpan(data.Length - MaximumPendingOutputLength));
                reportOverflow = outputOverflowReported.Add(output.SessionId);
            }
            else
            {
                var overflowLength = sessionOutput.Length + data.Length - MaximumPendingOutputLength;
                if (overflowLength > 0)
                {
                    sessionOutput.Remove(0, overflowLength);
                    reportOverflow = outputOverflowReported.Add(output.SessionId);
                }

                sessionOutput.Append(data);
            }

            scheduleFlush = outputFlushScheduled.Add(output.SessionId);
        }

        if (reportOverflow == true)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "OutputBacklog",
                $"Terminal session {output.SessionId} exceeded its bounded renderer backlog; the oldest data was discarded.");
        }

        if (scheduleFlush == true)
        {
            _ = Dispatcher.BeginInvoke(() => FlushOutput(output.SessionId));
        }
    }

    private void FlushOutput(TerminalSessionId sessionId)
    {
        string output = string.Empty;
        bool hasMoreOutput;

        lock (outputLock)
        {
            if (pendingOutput.TryGetValue(sessionId, out var sessionOutput) == false)
            {
                outputFlushScheduled.Remove(sessionId);
                return;
            }

            var length = Math.Min(sessionOutput.Length, MaximumOutputBatchLength);
            output = sessionOutput.ToString(0, length);
            sessionOutput.Remove(0, length);
            hasMoreOutput = sessionOutput.Length > 0;
            if (hasMoreOutput == false)
            {
                pendingOutput.Remove(sessionId);
                outputFlushScheduled.Remove(sessionId);
            }

            if (sessionOutput.Length < MaximumPendingOutputLength / 2)
            {
                outputOverflowReported.Remove(sessionId);
            }
        }

        if (string.IsNullOrEmpty(output) == false && isDisposed == false)
        {
            SendSessionMessage("output", sessionId, new { data = output });
        }

        if (hasMoreOutput == true && isDisposed == false)
        {
            _ = Dispatcher.BeginInvoke(() => FlushOutput(sessionId));
        }
    }

    private void Session_Exited(TerminalSessionExit sessionExit)
    {
        if (isDisposed == true)
        {
            return;
        }

        var exitCode = sessionExit.ExitCode;

        diagnosticLog.Write(
            DiagnosticLevel.Warning,
            "Terminal",
            "ShellExit",
            $"Terminal session {sessionExit.SessionId} exited with code {exitCode}.");
    }

    private void SendInitializeMessage()
    {
        if (options is null)
        {
            return;
        }

        SendGlobalMessage(
            "initialize",
            new
            {
                options.FontFamily,
                options.FontSize,
                theme = new
                {
                    options.Theme.Canvas,
                    options.Theme.Foreground,
                    options.Theme.Muted,
                    options.Theme.Accent,
                    options.Theme.Cursor,
                    options.Theme.Selection,
                    options.Theme.AnsiPalette,
                },
            });
    }

    private void SendGlobalMessage(string type, object payload)
    {
        PostRendererMessage(RendererProtocol.SerializeGlobalMessage(type, payload));
    }

    private void SendSessionMessage(
        string type,
        TerminalSessionId sessionId,
        object payload)
    {
        PostRendererMessage(
            RendererProtocol.SerializeSessionMessage(
                type,
                sessionId,
                payload));
    }

    private void PostRendererMessage(string json)
    {
        var core = Renderer.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            core.PostWebMessageAsJson(json);
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            rendererFailed = true;
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "RendererOutput",
                "The renderer could not accept a host message.",
                exception);
            ShowError("Terminal renderer 연결이 끊겼습니다. 다시 시작해 주세요.");
        }
    }

    private void CopyToClipboard(string data)
    {
        if (string.IsNullOrEmpty(data) == true)
        {
            return;
        }

        try
        {
            Clipboard.SetText(data);
        }
        catch (COMException exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "Copy",
                "The clipboard was temporarily unavailable.",
                exception);
        }
    }

    private void PasteFromClipboard(TerminalSessionId sessionId)
    {
        try
        {
            if (Clipboard.ContainsText() == true)
            {
                var data = Clipboard.GetText();
                if (data.Length <= RendererProtocol.MaximumMessageLength)
                {
                    SendSessionMessage("paste", sessionId, new { data });
                }
                else
                {
                    diagnosticLog.Write(
                        DiagnosticLevel.Warning,
                        "Terminal",
                        "Paste",
                        "A clipboard payload larger than the renderer limit was rejected.");
                }
            }
        }
        catch (COMException exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "Paste",
                "The clipboard was temporarily unavailable.",
                exception);
        }
    }

    private void ShowLoading(string message)
    {
        StatusHeading.Text = "STARBOARD / STARTING";
        StatusMessage.Text = message;
        StatusMessage.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        RetryButton.Visibility = Visibility.Collapsed;
        StatusSurface.Visibility = Visibility.Visible;
        Renderer.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        StatusHeading.Text = "STARBOARD / TERMINAL OFFLINE";
        StatusMessage.Text = message;
        StatusMessage.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        RetryButton.Visibility = Visibility.Visible;
        StatusSurface.Visibility = Visibility.Visible;
        Renderer.Visibility = Visibility.Collapsed;
    }

    private void ShowTerminal()
    {
        StatusSurface.Visibility = Visibility.Collapsed;
        Renderer.Visibility = Visibility.Visible;
    }

    private void ApplyTheme(TerminalTheme theme)
    {
        var canvas = ParseColor(theme.Canvas);
        var foreground = ParseColor(theme.Foreground);
        var muted = ParseColor(theme.Muted);
        var accent = ParseColor(theme.Accent);
        var selection = ParseColor(theme.Selection);
        var error = ParseColor(theme.AnsiPalette[1]);

        Resources["CanvasBrush"] = new SolidColorBrush(canvas);
        Resources["ForegroundBrush"] = new SolidColorBrush(foreground);
        Resources["MutedBrush"] = new SolidColorBrush(muted);
        Resources["RuleBrush"] = new SolidColorBrush(selection);
        Resources["RuleStrongBrush"] = new SolidColorBrush(Blend(accent, canvas, 0.35));
        Resources["AccentBrush"] = new SolidColorBrush(accent);
        Resources["AccentHoverBrush"] = new SolidColorBrush(Blend(accent, foreground, 0.16));
        Resources["AccentPressedBrush"] = new SolidColorBrush(Blend(accent, canvas, 0.18));
        Resources["FocusBrush"] = new SolidColorBrush(accent);
        Resources["ErrorBrush"] = new SolidColorBrush(error);
        Resources["ButtonInkBrush"] = FindBestButtonInk(accent);
        SetRendererBackground(canvas);
    }

    private void SetRendererBackground(Color canvas)
    {
        Renderer.DefaultBackgroundColor = System.Drawing.Color.FromArgb(
            canvas.A,
            canvas.R,
            canvas.G,
            canvas.B);
    }

    private SolidColorBrush FindBestButtonInk(Color background)
    {
        var dark = ((SolidColorBrush)FindResource("ButtonInkDarkBrush")).Color;
        var light = ((SolidColorBrush)FindResource("ButtonInkLightBrush")).Color;
        return new SolidColorBrush(
            ContrastRatio(dark, background) >= ContrastRatio(light, background)
                ? dark
                : light);
    }

    private static Color ParseColor(string value)
    {
        return (Color)ColorConverter.ConvertFromString(value);
    }

    private static Color Blend(Color source, Color target, double targetWeight)
    {
        return Color.FromRgb(
            (byte)Math.Round((source.R * (1 - targetWeight)) + (target.R * targetWeight)),
            (byte)Math.Round((source.G * (1 - targetWeight)) + (target.G * targetWeight)),
            (byte)Math.Round((source.B * (1 - targetWeight)) + (target.B * targetWeight)));
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        return (0.2126 * Linearize(color.R / 255.0)) +
               (0.7152 * Linearize(color.G / 255.0)) +
               (0.0722 * Linearize(color.B / 255.0));
    }

    private static double Linearize(double channel)
    {
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => exception.Message,
            COMException => "Microsoft Edge WebView2 Runtime을 시작하지 못했습니다.",
            UnauthorizedAccessException => "Starboard 로컬 데이터 폴더에 접근할 수 없습니다.",
            _ => "Terminal session을 시작하지 못했습니다. 로컬 로그를 확인한 뒤 다시 시도해 주세요.",
        };
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
