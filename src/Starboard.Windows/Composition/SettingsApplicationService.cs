using System.Text.RegularExpressions;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Preferences;
using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Terminal;
using Starboard.Modules.Terminal.Contracts;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Windows.Composition;

internal sealed class SettingsApplicationService : ISettingsEditorSaveHandler, IDisposable
{
    private static readonly Regex ShortcutPattern = new("^(?:(?:Ctrl|Alt|Shift)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-9]|2[0-4]))$",
                                                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                                                        TimeSpan.FromMilliseconds(100));

    private readonly Func<TerminalSettings, TerminalSettingsApplyResult> applyTerminalSettings;
    private readonly Func<DesktopSettings, DesktopSettingsApplyResult> applyDesktopSettings;
    private readonly Func<AppSettings, CancellationToken, Task> persistSettingsAsync;
    private readonly Func<bool, CancellationToken, Task<TerminalWorkspacePersistenceResult>>
        setWorkspacePersistenceEnabledAsync;
    private readonly Action<AppSettings> applyHostAppearance;
    private readonly IDiagnosticLog diagnosticLog;
    private readonly SemaphoreSlim applyLock = new(1, 1);
    private readonly CancellationTokenSource disposalCancellation = new();
    private AppSettings persistedSettings;
    private AppSettings effectiveSettings;
    private TerminalCollapsedHeightChangeRequestId? activeCollapsedHeightRequestId;
    private TerminalCollapsedHeightChangeResult? completedCollapsedHeightChange;
    private bool? pendingWorkspacePersistenceState;
    private bool isDisposed;

    internal SettingsApplicationService(PreferencesModule preferencesModule, TerminalModule terminalModule,
                                        DesktopIntegrationModule desktopIntegrationModule,
                                        AppSettings persistedSettings, AppSettings effectiveSettings,
                                        Action<AppSettings> applyHostAppearance, IDiagnosticLog diagnosticLog)
        : this(terminalModule.ApplySettings, desktopIntegrationModule.ApplySettings, preferencesModule.SaveAsync,
               persistedSettings, effectiveSettings, applyHostAppearance, diagnosticLog,
               terminalModule.SetWorkspacePersistenceEnabledAsync)
    {
    }

    internal SettingsApplicationService(Func<TerminalSettings, TerminalSettingsApplyResult> applyTerminalSettings,
                                        Func<DesktopSettings, DesktopSettingsApplyResult> applyDesktopSettings,
                                        Func<AppSettings, CancellationToken, Task> persistSettingsAsync,
                                        AppSettings persistedSettings, AppSettings effectiveSettings,
                                        Action<AppSettings> applyHostAppearance, IDiagnosticLog diagnosticLog,
                                        Func<bool, CancellationToken, Task<TerminalWorkspacePersistenceResult>>?
                                            setWorkspacePersistenceEnabledAsync = null)
    {
        ArgumentNullException.ThrowIfNull(applyTerminalSettings);
        ArgumentNullException.ThrowIfNull(applyDesktopSettings);
        ArgumentNullException.ThrowIfNull(persistSettingsAsync);
        ArgumentNullException.ThrowIfNull(persistedSettings);
        ArgumentNullException.ThrowIfNull(effectiveSettings);
        ArgumentNullException.ThrowIfNull(applyHostAppearance);
        ArgumentNullException.ThrowIfNull(diagnosticLog);

        this.applyTerminalSettings = applyTerminalSettings;
        this.applyDesktopSettings = applyDesktopSettings;
        this.persistSettingsAsync = persistSettingsAsync;
        this.setWorkspacePersistenceEnabledAsync = setWorkspacePersistenceEnabledAsync ??
            ((enabled, cancellationToken) =>
            {
                _ = enabled;
                _ = cancellationToken;

                return Task.FromResult(new TerminalWorkspacePersistenceResult(
                    TerminalWorkspacePersistenceOperation.Save, TerminalWorkspacePersistenceStatus.Skipped, null));
            });
        this.persistedSettings = persistedSettings;
        this.effectiveSettings = effectiveSettings;
        this.applyHostAppearance = applyHostAppearance;
        this.diagnosticLog = diagnosticLog;
    }

    internal event EventHandler? StatusChanged;

    internal event Action<double>? CollapsedHeightChanged;

    internal AppSettings PersistedSettings => persistedSettings;

    internal AppSettings EffectiveSettings => effectiveSettings;

    internal PreferenceApplyResult? LastResult { get; private set; }

    internal TerminalWorkspacePersistenceResult? LastWorkspacePersistenceResult { get; private set; }

    internal string? StatusMessage { get; private set; }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var result = await ApplyValidatedAsync(settings, cancellationToken);
        if (result.Succeeded == false)
        {
            throw new SettingsApplicationException(result);
        }

        if (result.WorkspaceRestoreTransition != WorkspaceRestorePreferenceTransition.Unchanged)
        {
            pendingWorkspacePersistenceState = settings.RestoreWorkspaceOnLaunch;
        }

        if (pendingWorkspacePersistenceState is null)
        {
            return;
        }

        await ApplyWorkspacePersistenceAsync(pendingWorkspacePersistenceState.Value, cancellationToken);
    }

    internal async Task<PreferenceApplyResult> ApplyValidatedAsync(AppSettings requestedSettings,
                                                                   CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(requestedSettings);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, disposalCancellation.Token);
        await applyLock.WaitAsync(linkedCancellation.Token);
        try
        {
            return await ApplyCoreAsync(requestedSettings, linkedCancellation.Token);
        }
        finally
        {
            applyLock.Release();
        }
    }

    internal async ValueTask<TerminalCollapsedHeightChangeResult> ApplyCollapsedHeightChangeAsync(
        TerminalCollapsedHeightChangeRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(request);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, disposalCancellation.Token);
        await applyLock.WaitAsync(linkedCancellation.Token);
        try
        {
            linkedCancellation.Token.ThrowIfCancellationRequested();
            if (request.Phase == TerminalCollapsedHeightChangePhase.Commit &&
                completedCollapsedHeightChange?.RequestId == request.RequestId)
            {
                return completedCollapsedHeightChange;
            }

            if (request.LastSavedHeightDip != persistedSettings.CollapsedHeightDip)
            {
                return CreateStaleCollapsedHeightResult(request);
            }

            if (request.Phase == TerminalCollapsedHeightChangePhase.Preview)
            {
                activeCollapsedHeightRequestId = request.RequestId;
                completedCollapsedHeightChange = null;

                return new TerminalCollapsedHeightChangeResult(request.RequestId,
                                                               TerminalCollapsedHeightChangeStatus.Applied,
                                                               request.RequestedHeightDip);
            }

            if (activeCollapsedHeightRequestId is { } activeRequestId && activeRequestId != request.RequestId)
            {
                return CreateStaleCollapsedHeightResult(request);
            }

            activeCollapsedHeightRequestId = request.RequestId;
            var requestedSettings = persistedSettings with { CollapsedHeightDip = request.RequestedHeightDip };
            var applyResult = await ApplyCoreAsync(requestedSettings, linkedCancellation.Token);
            var result = ToCollapsedHeightChangeResult(request, applyResult);
            completedCollapsedHeightChange = result;
            activeCollapsedHeightRequestId = null;
            CollapsedHeightChanged?.Invoke(result.AppliedHeightDip);

            return result;
        }
        finally
        {
            applyLock.Release();
        }
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        disposalCancellation.Cancel();
        disposalCancellation.Dispose();
    }

    internal static TerminalSettings ToTerminalSettings(AppSettings settings)
    {
        var theme = PreferencesModule.GetTheme(settings.Theme);
        var terminalTheme = new TerminalTheme(theme.Canvas, theme.Foreground, theme.Muted, theme.Accent, theme.Cursor,
                                              theme.Selection, theme.AnsiPalette);

        return new TerminalSettings(new TerminalAppearanceSettings(settings.FontFamily, settings.FontSize, terminalTheme),
                                    settings.ShellExecutable);
    }

    internal static TerminalOptions ToTerminalOptions(AppSettings settings)
    {
        var terminalSettings = ToTerminalSettings(settings);

        return new TerminalOptions(settings.ShellExecutable, settings.FontFamily, settings.FontSize,
                                   terminalSettings.Appearance.Theme, settings.RestoreWorkspaceOnLaunch);
    }

    internal static DesktopSettings ToDesktopSettings(AppSettings settings)
    {
        var monitorBehavior = settings.PreferredMonitor switch
        {
            "Taskbar" => PreferredMonitorBehavior.TaskbarMonitor,
            _ => throw new ArgumentException("The preferred monitor behavior is unsupported.", nameof(settings)),
        };

        return new DesktopSettings(settings.CollapsedHeightDip, settings.Opacity, monitorBehavior,
                                   new HotkeySettings(settings.ExpandShortcut, settings.ActivationShortcut),
                                   new StartupSettings(settings.StartWithWindows));
    }

    internal static AppSettings WithDesktopSettings(AppSettings settings, DesktopSettings desktopSettings)
    {
        return settings with
        {
            CollapsedHeightDip = desktopSettings.CollapsedHeightDip,
            Opacity = desktopSettings.Opacity,
            PreferredMonitor = desktopSettings.PreferredMonitorBehavior switch
            {
                PreferredMonitorBehavior.TaskbarMonitor => "Taskbar",
                _ => settings.PreferredMonitor,
            },
            ExpandShortcut = desktopSettings.Hotkeys.ExpandShortcut,
            ActivationShortcut = desktopSettings.Hotkeys.ActivationShortcut,
            StartWithWindows = desktopSettings.Startup.StartWithWindows,
        };
    }

    private async Task<PreferenceApplyResult> ApplyCoreAsync(AppSettings requestedSettings,
                                                             CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new PreferenceApplyRequest(persistedSettings, requestedSettings);
        var failures = new List<PreferenceApplyFailure>();
        if (TryValidateRequest(requestedSettings, out var validationFailure) == false)
        {
            failures.Add(CreateFailure(PreferenceApplyStep.Validation, PreferenceApplyFailureStage.Validation,
                                       validationFailure!));
            return Complete(request, PreferenceApplyStatus.Rejected, effectiveSettings, failures);
        }

        TerminalSettings requestedTerminalSettings;
        DesktopSettings requestedDesktopSettings;
        try
        {
            requestedTerminalSettings = ToTerminalSettings(requestedSettings);
            requestedDesktopSettings = ToDesktopSettings(requestedSettings);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            failures.Add(CreateFailure(PreferenceApplyStep.Validation, PreferenceApplyFailureStage.Validation,
                                       exception.Message));
            return Complete(request, PreferenceApplyStatus.Rejected, effectiveSettings, failures);
        }

        var terminalResult = ApplyTerminal(requestedTerminalSettings, failures, PreferenceApplyFailureStage.Apply);
        effectiveSettings = WithTerminalSettings(effectiveSettings, terminalResult.EffectiveSettings);
        if (terminalResult.Status != TerminalSettingsApplyStatus.Applied)
        {
            return CompleteFailure(request, failures);
        }

        var desktopResult = ApplyDesktop(requestedDesktopSettings, failures, PreferenceApplyFailureStage.Apply);
        effectiveSettings = WithDesktopSettings(effectiveSettings, desktopResult.EffectiveSettings);
        if (desktopResult.Status != DesktopSettingsApplyStatus.Applied)
        {
            RollbackTerminal(failures);
            TryApplyHostAppearance(persistedSettings, failures, PreferenceApplyFailureStage.Rollback);

            return CompleteFailure(request, failures);
        }

        if (TryApplyHostAppearance(requestedSettings, failures, PreferenceApplyFailureStage.Apply) == false)
        {
            RollbackDesktop(failures);
            RollbackTerminal(failures);
            TryApplyHostAppearance(persistedSettings, failures, PreferenceApplyFailureStage.Rollback);

            return CompleteFailure(request, failures);
        }

        effectiveSettings = requestedSettings;
        try
        {
            await persistSettingsAsync(requestedSettings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            failures.Add(CreateFailure(PreferenceApplyStep.Persistence, PreferenceApplyFailureStage.Apply,
                                       exception.Message));
            RollbackDesktop(failures);
            TryApplyHostAppearance(persistedSettings, failures, PreferenceApplyFailureStage.Rollback);
            RollbackTerminal(failures);

            return CompleteFailure(request, failures);
        }

        persistedSettings = requestedSettings;
        effectiveSettings = requestedSettings;

        return Complete(request, PreferenceApplyStatus.Applied, effectiveSettings, failures);
    }

    private TerminalCollapsedHeightChangeResult CreateStaleCollapsedHeightResult(
        TerminalCollapsedHeightChangeRequest request)
    {
        return new TerminalCollapsedHeightChangeResult(
            request.RequestId, TerminalCollapsedHeightChangeStatus.Reverted, persistedSettings.CollapsedHeightDip,
            "패널 높이가 다른 설정 변경으로 갱신되어 마지막 저장값을 유지했습니다.");
    }

    private TerminalCollapsedHeightChangeResult ToCollapsedHeightChangeResult(
        TerminalCollapsedHeightChangeRequest request, PreferenceApplyResult applyResult)
    {
        if (applyResult.Succeeded == true)
        {
            return new TerminalCollapsedHeightChangeResult(request.RequestId,
                                                           TerminalCollapsedHeightChangeStatus.Saved,
                                                           applyResult.PersistedSettings.CollapsedHeightDip);
        }

        var failureMessage = StatusMessage ?? "패널 높이를 저장하지 못했습니다.";
        var status = applyResult.Status == PreferenceApplyStatus.FailedAndRestored
            ? TerminalCollapsedHeightChangeStatus.Reverted
            : TerminalCollapsedHeightChangeStatus.Failed;

        return new TerminalCollapsedHeightChangeResult(request.RequestId, status,
                                                       applyResult.EffectiveSettings.CollapsedHeightDip,
                                                       failureMessage);
    }

    private async Task ApplyWorkspacePersistenceAsync(bool enabled, CancellationToken cancellationToken)
    {
        var operation = enabled == true
            ? TerminalWorkspacePersistenceOperation.Save
            : TerminalWorkspacePersistenceOperation.Delete;
        TerminalWorkspacePersistenceResult workspaceResult;
        try
        {
            workspaceResult = await setWorkspacePersistenceEnabledAsync(enabled, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            workspaceResult = new TerminalWorkspacePersistenceResult(operation,
                                                                     TerminalWorkspacePersistenceStatus.Failed,
                                                                     "작업공간 작업이 취소되었습니다.");
        }
        catch (Exception exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Host", operation.ToString(),
                                "The terminal workspace operation failed unexpectedly.", exception);
            workspaceResult = new TerminalWorkspacePersistenceResult(operation,
                                                                     TerminalWorkspacePersistenceStatus.Failed,
                                                                     "작업공간 작업을 완료하지 못했습니다.");
        }

        LastWorkspacePersistenceResult = workspaceResult;
        if (workspaceResult.Succeeded == true)
        {
            pendingWorkspacePersistenceState = null;
            StatusMessage = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);

            return;
        }

        StatusMessage = CreateWorkspacePersistenceMessage(enabled, workspaceResult);
        StatusChanged?.Invoke(this, EventArgs.Empty);
        diagnosticLog.Write(DiagnosticLevel.Warning, "Host", enabled == true ? "SaveWorkspace" : "DeleteWorkspace",
                            StatusMessage);

        throw new SettingsEditorSaveException(StatusMessage);
    }

    private TerminalSettingsApplyResult ApplyTerminal(TerminalSettings settings, List<PreferenceApplyFailure> failures,
                                                      PreferenceApplyFailureStage stage)
    {
        try
        {
            var result = applyTerminalSettings(settings);
            if (result.Status != TerminalSettingsApplyStatus.Applied)
            {
                failures.Add(CreateFailure(PreferenceApplyStep.TerminalSettings, stage,
                                           result.FailureMessage ?? "Terminal settings could not be applied."));
                if (stage == PreferenceApplyFailureStage.Apply &&
                    result.Status == TerminalSettingsApplyStatus.FailedAndRestoreIncomplete)
                {
                    failures.Add(CreateFailure(PreferenceApplyStep.TerminalSettings,
                                               PreferenceApplyFailureStage.Rollback,
                                               "Terminal settings recovery was incomplete."));
                }
            }

            return result;
        }
        catch (Exception exception)
        {
            failures.Add(CreateFailure(PreferenceApplyStep.TerminalSettings, stage, exception.Message));
            var current = ToTerminalSettings(effectiveSettings);

            return new TerminalSettingsApplyResult(settings, current, current,
                                                   TerminalSettingsApplyStatus.FailedWithoutChange, exception.Message);
        }
    }

    private DesktopSettingsApplyResult ApplyDesktop(DesktopSettings settings, List<PreferenceApplyFailure> failures,
                                                    PreferenceApplyFailureStage stage)
    {
        try
        {
            var result = applyDesktopSettings(settings);
            if (result.Status != DesktopSettingsApplyStatus.Applied)
            {
                failures.Add(CreateFailure(PreferenceApplyStep.DesktopSettings, stage, GetDesktopFailureMessage(result)));
                if (stage == PreferenceApplyFailureStage.Apply &&
                    result.Status == DesktopSettingsApplyStatus.FailedAndRestoreIncomplete)
                {
                    failures.Add(CreateFailure(PreferenceApplyStep.DesktopSettings,
                                               PreferenceApplyFailureStage.Rollback,
                                               "Desktop settings recovery was incomplete."));
                }
            }

            return result;
        }
        catch (Exception exception)
        {
            failures.Add(CreateFailure(PreferenceApplyStep.DesktopSettings, stage, exception.Message));
            var current = ToDesktopSettings(effectiveSettings);

            return new DesktopSettingsApplyResult(settings, current, current,
                                                  DesktopSettingsApplyStatus.FailedWithoutChange, []);
        }
    }

    private void RollbackTerminal(List<PreferenceApplyFailure> failures)
    {
        var result = ApplyTerminal(ToTerminalSettings(persistedSettings), failures,
                                   PreferenceApplyFailureStage.Rollback);
        effectiveSettings = WithTerminalSettings(effectiveSettings, result.EffectiveSettings);
    }

    private void RollbackDesktop(List<PreferenceApplyFailure> failures)
    {
        var result = ApplyDesktop(ToDesktopSettings(persistedSettings), failures, PreferenceApplyFailureStage.Rollback);
        effectiveSettings = WithDesktopSettings(effectiveSettings, result.EffectiveSettings);
    }

    private bool TryApplyHostAppearance(AppSettings settings, List<PreferenceApplyFailure> failures,
                                        PreferenceApplyFailureStage stage)
    {
        try
        {
            applyHostAppearance(settings);
            return true;
        }
        catch (Exception exception)
        {
            failures.Add(CreateFailure(PreferenceApplyStep.DesktopSettings, stage, exception.Message));
            return false;
        }
    }

    private PreferenceApplyResult CompleteFailure(PreferenceApplyRequest request,
                                                  IReadOnlyList<PreferenceApplyFailure> failures)
    {
        var restored = effectiveSettings == persistedSettings &&
            failures.Any(failure => failure.Stage == PreferenceApplyFailureStage.Rollback) == false;
        return Complete(request,
                        restored == true
                            ? PreferenceApplyStatus.FailedAndRestored
                            : PreferenceApplyStatus.FailedAndRestoreIncomplete,
                        effectiveSettings, failures);
    }

    private PreferenceApplyResult Complete(PreferenceApplyRequest request, PreferenceApplyStatus status,
                                           AppSettings actualSettings, IReadOnlyList<PreferenceApplyFailure> failures)
    {
        var result = new PreferenceApplyResult(request, status, actualSettings, persistedSettings, failures);
        LastResult = result;
        StatusMessage = CreateStatusMessage(result);
        StatusChanged?.Invoke(this, EventArgs.Empty);

        if (result.Succeeded == false)
        {
            diagnosticLog.Write(result.Status == PreferenceApplyStatus.FailedAndRestoreIncomplete
                                    ? DiagnosticLevel.Error
                                    : DiagnosticLevel.Warning,
                                "Host", "ApplySettings", StatusMessage ?? "Settings could not be applied.");
        }

        return result;
    }

    private static AppSettings WithTerminalSettings(AppSettings settings, TerminalSettings terminalSettings)
    {
        return settings with
        {
            ShellExecutable = terminalSettings.DefaultShellExecutable,
            FontFamily = terminalSettings.Appearance.FontFamily,
            FontSize = terminalSettings.Appearance.FontSize,
            Theme = FindThemeName(terminalSettings.Appearance.Theme, settings.Theme),
        };
    }

    private static string FindThemeName(TerminalTheme theme, string fallback)
    {
        foreach (var name in new[] { "Dark", "Light", "One Dark", "Tokyo Night" })
        {
            var candidate = PreferencesModule.GetTheme(name);
            if (string.Equals(candidate.Canvas, theme.Canvas, StringComparison.OrdinalIgnoreCase) == true &&
                string.Equals(candidate.Foreground, theme.Foreground, StringComparison.OrdinalIgnoreCase) == true &&
                candidate.AnsiPalette.SequenceEqual(theme.AnsiPalette, StringComparer.OrdinalIgnoreCase) == true)
            {
                return name;
            }
        }

        return fallback;
    }

    private static string GetDesktopFailureMessage(DesktopSettingsApplyResult result)
    {
        return result.Operations
            .FirstOrDefault(operation => string.IsNullOrWhiteSpace(operation.FailureMessage) == false)
            ?.FailureMessage
            ?? "Desktop settings could not be applied.";
    }

    private static PreferenceApplyFailure CreateFailure(PreferenceApplyStep step, PreferenceApplyFailureStage stage,
                                                        string message)
    {
        return new PreferenceApplyFailure(step, stage, message);
    }

    private static bool TryValidateRequest(AppSettings settings, out string? failureMessage)
    {
        failureMessage = null;
        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            failureMessage = "The settings schema must be current before live apply.";
            return false;
        }

        if (settings.Theme is not ("Dark" or "Light" or "One Dark" or "Tokyo Night"))
        {
            failureMessage = "The selected theme is unsupported.";
            return false;
        }

        if (double.IsFinite(settings.CollapsedHeightDip) == false || settings.CollapsedHeightDip < 96 ||
            settings.CollapsedHeightDip > 720)
        {
            failureMessage = "Collapsed height must be between 96 and 720 DIP.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.FontFamily) == true)
        {
            failureMessage = "A terminal font family is required.";
            return false;
        }

        if (double.IsFinite(settings.FontSize) == false || settings.FontSize < 8 || settings.FontSize > 32)
        {
            failureMessage = "Font size must be between 8 and 32.";
            return false;
        }

        if (double.IsFinite(settings.Opacity) == false || settings.Opacity < 0.72 || settings.Opacity > 1)
        {
            failureMessage = "Opacity must be between 0.72 and 1.";
            return false;
        }

        if (settings.ShellExecutable?.Contains('\0') == true)
        {
            failureMessage = "The shell executable contains an invalid character.";
            return false;
        }

        if (string.Equals(settings.PreferredMonitor, "Taskbar", StringComparison.Ordinal) == false)
        {
            failureMessage = "The preferred monitor behavior is unsupported.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.ExpandShortcut) == true ||
            string.IsNullOrWhiteSpace(settings.ActivationShortcut) == true)
        {
            failureMessage = "A shortcut is required.";
            return false;
        }

        if (ShortcutPattern.IsMatch(settings.ExpandShortcut.Trim()) == false ||
            ShortcutPattern.IsMatch(settings.ActivationShortcut.Trim()) == false)
        {
            failureMessage = "A shortcut has an invalid format.";
            return false;
        }

        if (string.Equals(settings.ExpandShortcut.Trim(), settings.ActivationShortcut.Trim(),
                          StringComparison.OrdinalIgnoreCase) == true)
        {
            failureMessage = "Expand and activation shortcuts must differ.";
            return false;
        }

        return true;
    }

    private static string? CreateStatusMessage(PreferenceApplyResult result)
    {
        return result.Status switch
        {
            PreferenceApplyStatus.Applied => null,
            PreferenceApplyStatus.Rejected =>
                "설정 값이 유효하지 않아 적용하지 않았습니다. 편집 값을 확인해 주세요.",
            PreferenceApplyStatus.FailedAndRestored =>
                "설정을 적용하거나 저장하지 못해 이전 상태로 복구했습니다. 편집 값은 유지되며 다시 시도할 수 있습니다.",
            PreferenceApplyStatus.FailedAndRestoreIncomplete =>
                $"복구가 완료되지 않아 저장 값과 실제 적용 상태가 다릅니다. " +
                $"마지막 저장: {Describe(result.PersistedSettings)} / " +
                $"실제 적용: {Describe(result.EffectiveSettings)}. 편집 값을 유지한 채 다시 시도해 주세요.",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private static string CreateWorkspacePersistenceMessage(bool enabled,
                                                            TerminalWorkspacePersistenceResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.FailureDetail) == true
            ? null
            : " " + result.FailureDetail;
        if (enabled == true)
        {
            return "일반 설정은 저장했지만 현재 탭 구성을 저장하지 못했습니다. " +
                "현재 세션은 유지됩니다. 저장을 다시 누르면 작업공간 저장을 재시도합니다." + detail;
        }

        return "일반 설정은 저장했고 현재 세션은 유지했지만 저장된 작업공간을 삭제하지 못했습니다. " +
            "구성과 백업 데이터가 남아 있을 수 있습니다. 저장을 다시 누르면 삭제를 재시도합니다." + detail;
    }

    private static string Describe(AppSettings settings)
    {
        return $"테마 {settings.Theme}, 높이 {settings.CollapsedHeightDip:0.#} DIP, " +
            $"단축키 {settings.ExpandShortcut}/{settings.ActivationShortcut}, " +
            $"자동 시작 {(settings.StartWithWindows == true ? "켬" : "끔")}";
    }
}

internal sealed class SettingsApplicationException : Exception
{
    internal SettingsApplicationException(PreferenceApplyResult result)
        : base("Settings were not saved because live application did not complete.")
    {
        Result = result;
    }

    internal PreferenceApplyResult Result { get; }
}
