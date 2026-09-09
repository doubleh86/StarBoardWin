using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration.Tests;

#pragma warning disable CA1707 // Test names follow the repository's Scenario_ExpectedResult convention.

[TestClass]
public sealed class DesktopSettingsApplyTests
{
    [TestMethod]
    public void ApplySettings_HeightAndOpacity_UsesCurrentDpiAndWorkArea()
    {
        var runtime = new FakeRuntime();
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(collapsedHeightDip: 240, opacity: 0.8));

        Assert.AreEqual(DesktopSettingsApplyStatus.Applied, result.Status);
        Assert.AreEqual(0.8, runtime.LastOpacity);
        Assert.AreEqual(new PixelRect(0, 531, 1000, 891), runtime.LastPlacedBounds);
        Assert.AreEqual(240, result.EffectiveSettings.CollapsedHeightDip);
    }

    [TestMethod]
    public void ApplySettings_ExpandShortcutRegistrationFails_RestoresPreviousShortcuts()
    {
        var runtime = new FakeRuntime
        {
            RejectedShortcut = "Ctrl+Alt+X",
        };
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(expandShortcut: "Ctrl+Alt+X"));

        Assert.AreEqual(DesktopSettingsApplyStatus.FailedAndRestored, result.Status);
        Assert.AreEqual("Ctrl+Alt+E", result.EffectiveSettings.Hotkeys.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", result.EffectiveSettings.Hotkeys.ActivationShortcut);
        Assert.AreEqual("Ctrl+Alt+E", runtime.RegisteredShortcuts[0x5342]);
        Assert.AreEqual("Ctrl+Alt+S", runtime.RegisteredShortcuts[0x5343]);
        Assert.AreEqual(DesktopSettingsOperationStatus.Restored, result.Operations[2].Status);
        StringAssert.Contains(result.Operations[2].FailureMessage, "restored");
    }

    [TestMethod]
    public void ApplySettings_ActivationShortcutRegistrationFails_RestoresPreviousShortcuts()
    {
        var runtime = new FakeRuntime
        {
            RejectedShortcut = "Ctrl+Alt+X",
        };
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(activationShortcut: "Ctrl+Alt+X"));

        Assert.AreEqual(DesktopSettingsApplyStatus.FailedAndRestored, result.Status);
        Assert.AreEqual("Ctrl+Alt+E", result.EffectiveSettings.Hotkeys.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", result.EffectiveSettings.Hotkeys.ActivationShortcut);
        Assert.AreEqual("Ctrl+Alt+E", runtime.RegisteredShortcuts[0x5342]);
        Assert.AreEqual("Ctrl+Alt+S", runtime.RegisteredShortcuts[0x5343]);
        Assert.AreEqual(DesktopSettingsOperationStatus.Restored, result.Operations[2].Status);
        StringAssert.Contains(result.Operations[2].FailureMessage, "restored");
    }

    [TestMethod]
    public void ApplySettings_ReplacementFailsAfterInitialShortcutConflict_RestoresAvailableShortcut()
    {
        var runtime = new FakeRuntime();
        runtime.RejectedShortcuts.Add("Ctrl+Alt+E");
        runtime.RejectedShortcuts.Add("Ctrl+Alt+X");
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(expandShortcut: "Ctrl+Alt+X"));

        Assert.AreEqual(DesktopSettingsApplyStatus.FailedAndRestored, result.Status);
        Assert.AreEqual(string.Empty, result.EffectiveSettings.Hotkeys.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", result.EffectiveSettings.Hotkeys.ActivationShortcut);
        Assert.IsFalse(runtime.RegisteredShortcuts.ContainsKey(0x5342));
        Assert.AreEqual("Ctrl+Alt+S", runtime.RegisteredShortcuts[0x5343]);
        Assert.AreEqual(DesktopSettingsOperationStatus.Restored, result.Operations[2].Status);
    }

    [TestMethod]
    public void ApplySettings_HotkeyFailureAfterAppearanceChange_RestoresEffectiveSnapshot()
    {
        var runtime = new FakeRuntime
        {
            RejectedShortcut = "Ctrl+Alt+X",
        };
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(collapsedHeightDip: 240, opacity: 0.8,
                                                         expandShortcut: "Ctrl+Alt+X"));

        Assert.AreEqual(DesktopSettingsApplyStatus.FailedAndRestored, result.Status);
        Assert.AreEqual(200, result.EffectiveSettings.CollapsedHeightDip);
        Assert.AreEqual(0.97, result.EffectiveSettings.Opacity);
        Assert.AreEqual(0.97, runtime.LastOpacity);
        Assert.AreEqual(new PixelRect(0, 591, 1000, 891), runtime.LastPlacedBounds);
        Assert.AreEqual(DesktopSettingsOperationStatus.Restored, result.Operations[0].Status);
    }

    [TestMethod]
    public void ApplySettings_AppearanceRollbackFails_ReturnsLastEffectiveAppearance()
    {
        var runtime = new FakeRuntime
        {
            RejectedShortcut = "Ctrl+Alt+X",
            ThrowOnOpacityCall = 3,
        };
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(collapsedHeightDip: 240, opacity: 0.8,
                                                         expandShortcut: "Ctrl+Alt+X"));

        Assert.AreEqual(DesktopSettingsApplyStatus.FailedAndRestoreIncomplete, result.Status);
        Assert.AreEqual(240, result.EffectiveSettings.CollapsedHeightDip);
        Assert.AreEqual(0.8, result.EffectiveSettings.Opacity);
        Assert.AreEqual(0.8, runtime.LastOpacity);
        Assert.AreEqual(DesktopSettingsOperationStatus.RestoreFailed, result.Operations[0].Status);
    }

    [TestMethod]
    public void ApplySettings_MatchingShortcuts_RejectsSettingsWithoutReplacingRegistrations()
    {
        var runtime = new FakeRuntime();
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(activationShortcut: "Ctrl+Alt+E"));

        Assert.AreEqual(DesktopSettingsApplyStatus.FailedWithoutChange, result.Status);
        Assert.AreEqual("Ctrl+Alt+E", result.EffectiveSettings.Hotkeys.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", result.EffectiveSettings.Hotkeys.ActivationShortcut);
        Assert.AreEqual(2, runtime.RegisterCallCount);
        Assert.AreEqual(DesktopSettingsOperationStatus.Failed, result.Operations[2].Status);
    }

    [TestMethod]
    public void ApplySettings_UnchangedShortcuts_DoesNotReplaceCurrentRegistrations()
    {
        var runtime = new FakeRuntime();
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());

        var result = module.ApplySettings(CreateSettings(opacity: 0.8));

        Assert.AreEqual(DesktopSettingsApplyStatus.Applied, result.Status);
        Assert.AreEqual(2, runtime.RegisterCallCount);
        Assert.AreEqual(DesktopSettingsOperationStatus.Unchanged, result.Operations[2].Status);
    }

    [TestMethod]
    public void ApplySettings_StartupAdapterFails_RestoresPreviousStartupIntent()
    {
        var runtime = new FakeRuntime();
        var startup = new FakeStartupRegistration { ThrowWhenEnabled = true };
        using var module = CreateAttachedModule(runtime, startup);

        var result = module.ApplySettings(CreateSettings(startWithWindows: true));

        Assert.AreEqual(DesktopSettingsApplyStatus.FailedAndRestored, result.Status);
        Assert.IsFalse(result.EffectiveSettings.Startup.StartWithWindows);
        CollectionAssert.AreEqual(new List<bool> { true, false }, startup.Requests);
        Assert.AreEqual(DesktopSettingsOperationStatus.Restored, result.Operations[3].Status);
        Assert.AreEqual(DesktopSettingsOperationStatus.Unchanged, result.Operations[2].Status);
    }

    [TestMethod]
    public void TraySettingsRequested_RaisesModuleSettingsRequest()
    {
        var runtime = new FakeRuntime();
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());
        var requestCount = 0;
        module.SettingsRequested += (_, _) => requestCount++;

        runtime.RaiseTraySettingsRequested();

        Assert.AreEqual(1, requestCount);
    }

    [TestMethod]
    public void TrayShortcutGuideRequested_ReportsActualGlobalShortcutRegistrationState()
    {
        var runtime = new FakeRuntime();
        runtime.RejectedShortcuts.Add("Ctrl+Alt+E");
        using var module = CreateAttachedModule(runtime, new FakeStartupRegistration());
        GlobalShortcutRegistrationSnapshot? receivedSnapshot = null;
        module.ShortcutGuideRequested += (_, eventArguments) => receivedSnapshot = eventArguments.RegistrationSnapshot;

        runtime.RaiseTrayShortcutGuideRequested();

        Assert.IsNotNull(receivedSnapshot);
        Assert.AreEqual("Ctrl+Alt+E", receivedSnapshot.Expand.ConfiguredGesture);
        Assert.AreEqual(GlobalShortcutRegistrationStatus.NotRegistered, receivedSnapshot.Expand.Status);
        Assert.IsNull(receivedSnapshot.Expand.EffectiveGesture);
        Assert.AreEqual("Ctrl+Alt+S", receivedSnapshot.Activation.EffectiveGesture);
        Assert.AreEqual(GlobalShortcutRegistrationStatus.Registered, receivedSnapshot.Activation.Status);
    }

    [TestMethod]
    public void QuoteExecutablePath_PathContainsSpacesAndKorean_QuotesEntirePath()
    {
        var command = RegistryStartupRegistration.QuoteExecutablePath("C:\\사용자 파일\\Starboard App\\Starboard.exe");

        Assert.AreEqual("\"C:\\사용자 파일\\Starboard App\\Starboard.exe\"", command);
    }

    private static DesktopIntegrationModule CreateAttachedModule(FakeRuntime runtime,
                                                                 FakeStartupRegistration startupRegistration)
    {
        var module = new DesktopIntegrationModule(new NullDiagnosticLog(), runtime, startupRegistration, false);
        module.Attach(new nint(1), new PanelOptions(200));

        return module;
    }

    private static DesktopSettings CreateSettings(double collapsedHeightDip = 200, double opacity = 0.97,
                                                  bool startWithWindows = false, string expandShortcut = "Ctrl+Alt+E",
                                                  string activationShortcut = "Ctrl+Alt+S")
    {
        return new DesktopSettings(collapsedHeightDip, opacity, PreferredMonitorBehavior.TaskbarMonitor,
                                   new HotkeySettings(expandShortcut, activationShortcut),
                                   new StartupSettings(startWithWindows));
    }

    private sealed class FakeRuntime : IDesktopIntegrationRuntime
    {
        private readonly DesktopGeometrySnapshot geometry = new(new TaskbarSnapshot(TaskbarEdge.Bottom,
                                                                                    new PixelRect(0, 900, 1000, 940),
                                                                                    new PixelRect(0, 0, 1000, 940),
                                                                                    new PixelRect(0, 0, 1000, 900),
                                                                                    false, 144),
                                                                new MonitorSnapshot(new nint(1),
                                                                                    new PixelRect(0, 0, 1000, 940),
                                                                                    new PixelRect(0, 0, 1000, 900),
                                                                                    new DisplayDpi(144, 144)),
                                                                TaskbarPresence.Visible, DisplayTrackingState.Tracked,
                                                                null, null);

        public uint TaskbarCreatedMessage => 0;

        public string? RejectedShortcut { get; init; }

        public HashSet<string> RejectedShortcuts { get; } = [];

        public int? ThrowOnOpacityCall { get; init; }

        public Dictionary<int, string> RegisteredShortcuts { get; } = [];

        public int RegisterCallCount { get; private set; }

        public double LastOpacity { get; private set; }

        private int OpacitySetCallCount { get; set; }

        public PixelRect LastPlacedBounds { get; private set; }

        public event EventHandler? EnvironmentChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? ForegroundChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? TrayToggleVisibilityRequested
        {
            add { }
            remove { }
        }

        public event EventHandler? TraySummonRequested
        {
            add { }
            remove { }
        }

        public event EventHandler? TraySettingsRequested;

        public event EventHandler? TrayShortcutGuideRequested;

        public event EventHandler? TrayExitRequested
        {
            add { }
            remove { }
        }

        public void Attach(nint windowHandle)
        {
            _ = windowHandle;
        }

        public bool RegisterHotKey(nint windowHandle, int identifier, GlobalHotkey hotkey)
        {
            _ = windowHandle;
            RegisterCallCount++;
            if (string.Equals(hotkey.DisplayText, RejectedShortcut, StringComparison.Ordinal) == true ||
                RejectedShortcuts.Contains(hotkey.DisplayText) == true)
            {
                return false;
            }

            RegisteredShortcuts[identifier] = hotkey.DisplayText;

            return true;
        }

        public bool RegisterHotKey(nint windowHandle, int identifier, uint virtualKey)
        {
            _ = windowHandle;
            _ = identifier;
            _ = virtualKey;

            return true;
        }

        public void UnregisterHotKey(nint windowHandle, int identifier)
        {
            _ = windowHandle;
            RegisteredShortcuts.Remove(identifier);
        }

        public DesktopGeometrySnapshot CaptureGeometry(nint panelWindowHandle)
        {
            _ = panelWindowHandle;
            return geometry;
        }

        public FullscreenObservation CaptureFullscreen(nint panelWindowHandle, MonitorSnapshot panelMonitor)
        {
            _ = panelWindowHandle;
            _ = panelMonitor;

            return new FullscreenObservation(PanelFullscreenState.Normal, true, null,
                                             PanelWindowActivation.PreserveForeground, PanelWindowZOrder.PreserveNormal);
        }

        public void PlaceWithoutActivation(nint windowHandle, PixelRect bounds)
        {
            _ = windowHandle;
            LastPlacedBounds = bounds;
        }

        public void SetPanelOpacity(nint windowHandle, double opacity)
        {
            _ = windowHandle;
            OpacitySetCallCount++;
            if (OpacitySetCallCount == ThrowOnOpacityCall)
            {
                throw new InvalidOperationException("Opacity update failed.");
            }

            LastOpacity = opacity;
        }

        public void ActivateOnExplicitRequest(nint windowHandle)
        {
            _ = windowHandle;
        }

        public void SetTrayPanelVisible(bool isVisible)
        {
            _ = isVisible;
        }

        public void RecreateTrayIcon(bool isVisible)
        {
            _ = isVisible;
        }

        public void RaiseTraySettingsRequested()
        {
            TraySettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseTrayShortcutGuideRequested()
        {
            TrayShortcutGuideRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public List<bool> Requests { get; } = [];

        public bool ThrowWhenEnabled { get; init; }

        public void SetEnabled(bool isEnabled)
        {
            Requests.Add(isEnabled);
            if (isEnabled == true && ThrowWhenEnabled == true)
            {
                throw new InvalidOperationException("Startup registration failed.");
            }
        }
    }

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null)
        {
            _ = level;
            _ = subsystem;
            _ = operation;
            _ = message;
            _ = exception;
        }
    }
}

#pragma warning restore CA1707
