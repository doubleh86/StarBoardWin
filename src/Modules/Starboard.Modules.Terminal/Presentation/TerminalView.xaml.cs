using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Presentation;

internal partial class TerminalView : UserControl, IAsyncDisposable
{
    private const string RendererHostName = "starboard.local";
    private const int MaximumOutputBatchLength = 65_536;
    private const int MaximumPendingOutputLength = 4 * 1024 * 1024;

    private readonly IDiagnosticLog diagnosticLog;
    private readonly Lock outputLock = new();
    private readonly StringBuilder pendingOutput = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();

    private TaskCompletionSource rendererReady = CreateCompletionSource();
    private TerminalOptions? options;
    private ConPtySession? session;
    private bool outputFlushScheduled;
    private bool outputOverflowReported;
    private bool rendererFailed;
    private bool isDisposed;
    private int columns = 80;
    private int rows = 24;

    internal TerminalView(IDiagnosticLog diagnosticLog)
    {
        this.diagnosticLog = diagnosticLog;
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
            await StartSessionAsync(cancellationToken);
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
            ShowError(ToUserMessage(exception));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        lifetimeCancellation.Cancel();
        Renderer.Dispose();

        await StopSessionAsync().ConfigureAwait(false);

        lifetimeCancellation.Dispose();
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

    private async Task StartSessionAsync(CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new InvalidOperationException("Terminal options are unavailable.");
        }

        await StopSessionAsync();

        var shell = ShellResolver.Resolve(options.ShellExecutable);
        session = ConPtySession.Start(
            shell,
            columns,
            rows,
            diagnosticLog);
        session.OutputReceived += Session_OutputReceived;
        session.Exited += Session_Exited;
        session.BeginReading();

        cancellationToken.ThrowIfCancellationRequested();
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
            case RendererMessageType.Input:
                _ = WriteInputAsync(message.Data ?? string.Empty);
                break;
            case RendererMessageType.Resize:
                columns = message.Columns;
                rows = message.Rows;
                ResizeSession(message.Columns, message.Rows);
                break;
            case RendererMessageType.Copy:
                CopyToClipboard(message.Data ?? string.Empty);
                break;
            case RendererMessageType.PasteRequest:
                PasteFromClipboard();
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

        var reconnectRendererOnly = rendererFailed && session is not null;
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
                Renderer.CoreWebView2.Navigate($"https://{RendererHostName}/index.html");
                await rendererReady.Task.WaitAsync(lifetimeCancellation.Token);
                SendInitializeMessage();
                rendererFailed = false;
            }

            if (reconnectRendererOnly == true)
            {
                ShowTerminal();
                return;
            }

            SendMessage("reset", new { });
            await StartSessionAsync(lifetimeCancellation.Token);
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

    private async Task WriteInputAsync(string data)
    {
        var currentSession = session;
        if (currentSession is null || isDisposed == true)
        {
            return;
        }

        try
        {
            await currentSession.WriteAsync(data, lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "Terminal",
                    "WriteInput",
                    "Terminal input could not be delivered.",
                    exception);
                ShowError("shell 입력 연결이 끊겼습니다. 다시 시작해 주세요.");
            }
        }
    }

    private void ResizeSession(int requestedColumns, int requestedRows)
    {
        try
        {
            session?.Resize(requestedColumns, requestedRows);
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

    private void Session_OutputReceived(string data)
    {
        var reportOverflow = false;
        var scheduleFlush = false;

        lock (outputLock)
        {
            if (data.Length >= MaximumPendingOutputLength)
            {
                pendingOutput.Clear();
                pendingOutput.Append(data.AsSpan(data.Length - MaximumPendingOutputLength));
                reportOverflow = outputOverflowReported == false;
                outputOverflowReported = true;
            }
            else
            {
                var overflowLength = pendingOutput.Length + data.Length - MaximumPendingOutputLength;
                if (overflowLength > 0)
                {
                    pendingOutput.Remove(0, overflowLength);
                    reportOverflow = outputOverflowReported == false;
                    outputOverflowReported = true;
                }

                pendingOutput.Append(data);
            }

            if (outputFlushScheduled == true)
            {
                scheduleFlush = false;
            }
            else
            {
                outputFlushScheduled = true;
                scheduleFlush = true;
            }
        }

        if (reportOverflow == true)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "OutputBacklog",
                "The renderer output backlog exceeded its bounded buffer; the oldest data was discarded.");
        }

        if (scheduleFlush == true)
        {
            _ = Dispatcher.BeginInvoke(FlushOutput);
        }
    }

    private void FlushOutput()
    {
        string output;
        bool hasMoreOutput;

        lock (outputLock)
        {
            var length = Math.Min(pendingOutput.Length, MaximumOutputBatchLength);
            output = pendingOutput.ToString(0, length);
            pendingOutput.Remove(0, length);
            hasMoreOutput = pendingOutput.Length > 0;
            outputFlushScheduled = hasMoreOutput;
            if (pendingOutput.Length < MaximumPendingOutputLength / 2)
            {
                outputOverflowReported = false;
            }
        }

        if (string.IsNullOrEmpty(output) == false && isDisposed == false)
        {
            SendMessage("output", new { data = output });
        }

        if (hasMoreOutput == true && isDisposed == false)
        {
            _ = Dispatcher.BeginInvoke(FlushOutput);
        }
    }

    private void Session_Exited(uint exitCode)
    {
        if (isDisposed == true)
        {
            return;
        }

        diagnosticLog.Write(
            DiagnosticLevel.Warning,
            "Terminal",
            "ShellExit",
            $"The shell process exited with code {exitCode}.");

        _ = Dispatcher.BeginInvoke(
            () => ShowError($"shell이 종료됐습니다 (exit {exitCode}). 다시 시작할 수 있습니다."));
    }

    private void SendInitializeMessage()
    {
        if (options is null)
        {
            return;
        }

        SendMessage(
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

    private void SendMessage(string type, object payload)
    {
        var core = Renderer.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(
                new
                {
                    version = 1,
                    type,
                    payload,
                },
                JsonSerializerOptions.Web);
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

    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText() == true)
            {
                var data = Clipboard.GetText();
                if (data.Length <= RendererProtocol.MaximumMessageLength)
                {
                    SendMessage("paste", new { data });
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

    private async Task StopSessionAsync()
    {
        var previousSession = session;
        session = null;

        if (previousSession is null)
        {
            return;
        }

        previousSession.OutputReceived -= Session_OutputReceived;
        previousSession.Exited -= Session_Exited;
        await previousSession.DisposeAsync().ConfigureAwait(false);
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
