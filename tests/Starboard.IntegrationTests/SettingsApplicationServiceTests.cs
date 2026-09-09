using System.IO;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Terminal.Contracts;
using Starboard.SharedKernel.Diagnostics;
using Starboard.Windows.Composition;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class SettingsApplicationServiceTests
{
    private static readonly string[] SuccessfulApplyOperations =
        ["terminal", "desktop", "host", "persistence"];

    private static readonly string[] PersistenceFailureOperations =
    [
        "terminal-apply",
        "desktop-apply",
        "host-apply",
        "persistence",
        "desktop-rollback",
        "host-rollback",
        "terminal-rollback",
    ];

    [TestMethod]
    public async Task ApplyValidatedAsyncValidSettingsAppliesModulesBeforePersistence()
    {
        var previous = new AppSettings();
        var requested = CreateRequestedSettings();
        var operations = new List<string>();
        using var service = CreateService(previous,
                                          settings =>
                                          {
                                              operations.Add("terminal");
                                              return TerminalApplied(settings, previous);
                                          },
                                          settings =>
                                          {
                                              operations.Add("desktop");
                                              return DesktopApplied(settings, previous);
                                          },
                                          (settings, cancellationToken) =>
                                          {
                                              _ = settings;
                                              _ = cancellationToken;
                                              operations.Add("persistence");

                                              return Task.CompletedTask;
                                          },
                                          settings =>
                                          {
                                              _ = settings;
                                              operations.Add("host");
                                          });

        var result = await service.ApplyValidatedAsync(requested, CancellationToken.None);

        Assert.AreEqual(PreferenceApplyStatus.Applied, result.Status);
        CollectionAssert.AreEqual(SuccessfulApplyOperations, operations);
        Assert.AreEqual(requested, service.PersistedSettings);
        Assert.AreEqual(requested, service.EffectiveSettings);
    }

    [TestMethod]
    public async Task ApplyValidatedAsyncInvalidThemeRejectsBeforeLiveModules()
    {
        var previous = new AppSettings();
        var terminalCalls = 0;
        var desktopCalls = 0;
        using var service = CreateService(previous,
                                          settings =>
                                          {
                                              terminalCalls++;
                                              return TerminalApplied(settings, previous);
                                          },
                                          settings =>
                                          {
                                              desktopCalls++;
                                              return DesktopApplied(settings, previous);
                                          });

        var result = await service.ApplyValidatedAsync(previous with { Theme = "Unknown" }, CancellationToken.None);

        Assert.AreEqual(PreferenceApplyStatus.Rejected, result.Status);
        Assert.AreEqual(0, terminalCalls);
        Assert.AreEqual(0, desktopCalls);
        Assert.AreEqual(PreferenceApplyStep.Validation, result.Failures.Single().Step);
    }

    [TestMethod]
    public async Task ApplyValidatedAsyncRendererApplyFailureDoesNotChangeDesktopOrPersistedSnapshot()
    {
        var previous = new AppSettings();
        var requested = CreateRequestedSettings();
        var desktopCalls = 0;
        var persistenceCalls = 0;
        using var service = CreateService(previous,
                                          settings => new TerminalSettingsApplyResult(settings,
                                                                                      SettingsApplicationService.ToTerminalSettings(previous),
                                                                                      SettingsApplicationService.ToTerminalSettings(previous),
                                                                                      TerminalSettingsApplyStatus.FailedAndRestored,
                                                                                      "Renderer rejected appearance."),
                                          settings =>
                                          {
                                              desktopCalls++;
                                              return DesktopApplied(settings, previous);
                                          },
                                          (settings, cancellationToken) =>
                                          {
                                              _ = settings;
                                              _ = cancellationToken;
                                              persistenceCalls++;

                                              return Task.CompletedTask;
                                          });

        var result = await service.ApplyValidatedAsync(requested, CancellationToken.None);

        Assert.AreEqual(PreferenceApplyStatus.FailedAndRestored, result.Status);
        Assert.AreEqual(0, desktopCalls);
        Assert.AreEqual(0, persistenceCalls);
        Assert.AreEqual(previous, result.EffectiveSettings);
        Assert.AreEqual(previous, result.PersistedSettings);
    }

    [TestMethod]
    public async Task ApplyValidatedAsyncHotkeyConflictRollsTerminalBackAndKeepsSavedSettings()
    {
        var previous = new AppSettings();
        var requested = CreateRequestedSettings();
        var terminalRequests = new List<TerminalSettings>();
        using var service = CreateService(previous,
                                          settings =>
                                          {
                                              terminalRequests.Add(settings);
                                              return TerminalApplied(settings, previous);
                                          },
                                          settings => new DesktopSettingsApplyResult(settings,
                                                                                     SettingsApplicationService.ToDesktopSettings(previous),
                                                                                     SettingsApplicationService.ToDesktopSettings(previous),
                                                                                     DesktopSettingsApplyStatus.FailedAndRestored,
                                                                                     [
                                                                                         new DesktopSettingsOperationResult(DesktopSettingsOperation.Hotkeys,
                                                                                                                            DesktopSettingsOperationStatus.Restored,
                                                                                                                            "Shortcut is already registered."),
                                                                                     ]));

        var result = await service.ApplyValidatedAsync(requested, CancellationToken.None);

        Assert.AreEqual(PreferenceApplyStatus.FailedAndRestored, result.Status);
        Assert.AreEqual(2, terminalRequests.Count);
        Assert.AreEqual(previous.ShellExecutable, terminalRequests[1].DefaultShellExecutable);
        Assert.AreEqual(previous, result.PersistedSettings);
        Assert.AreEqual(previous, result.EffectiveSettings);
    }

    [TestMethod]
    public async Task ApplyValidatedAsyncStartupFailureRollsTerminalBackWithoutPersistence()
    {
        var previous = new AppSettings();
        var requested = CreateRequestedSettings() with { StartWithWindows = true };
        var persistenceCalls = 0;
        var terminalCalls = 0;
        using var service = CreateService(previous,
                                          settings =>
                                          {
                                              terminalCalls++;
                                              return TerminalApplied(settings, previous);
                                          },
                                          settings => new DesktopSettingsApplyResult(settings,
                                                                                     SettingsApplicationService.ToDesktopSettings(previous),
                                                                                     SettingsApplicationService.ToDesktopSettings(previous),
                                                                                     DesktopSettingsApplyStatus.FailedAndRestored,
                                                                                     [
                                                                                         new DesktopSettingsOperationResult(DesktopSettingsOperation.Startup,
                                                                                                                            DesktopSettingsOperationStatus.Restored,
                                                                                                                            "Per-user startup registration failed."),
                                                                                     ]),
                                          (settings, cancellationToken) =>
                                          {
                                              _ = settings;
                                              _ = cancellationToken;
                                              persistenceCalls++;

                                              return Task.CompletedTask;
                                          });

        var result = await service.ApplyValidatedAsync(requested, CancellationToken.None);

        Assert.AreEqual(PreferenceApplyStatus.FailedAndRestored, result.Status);
        Assert.AreEqual(2, terminalCalls);
        Assert.AreEqual(0, persistenceCalls);
        Assert.IsFalse(result.PersistedSettings.StartWithWindows);
    }

    [TestMethod]
    public async Task ApplyValidatedAsyncAppearanceAndShellChangePreservesExistingSessions()
    {
        var previous = new AppSettings();
        var requested = CreateRequestedSettings();
        var terminal = new SessionPreservingTerminalRuntime(previous);
        using var service = CreateService(previous, terminal.ApplySettings,
                                          settings => DesktopApplied(settings, previous));
        var originalSessions = terminal.Sessions.ToArray();

        var result = await service.ApplyValidatedAsync(requested, CancellationToken.None);
        var newSession = terminal.CreateSession(303, "C:\\New");

        Assert.AreEqual(PreferenceApplyStatus.Applied, result.Status);
        CollectionAssert.AreEqual(originalSessions, terminal.Sessions.Take(2).ToArray());
        Assert.AreEqual("pwsh.exe", newSession.ShellExecutable);
        Assert.AreEqual(101, terminal.Sessions[0].ProcessId);
        Assert.AreEqual("C:\\First", terminal.Sessions[0].WorkingDirectory);
        Assert.AreEqual(202, terminal.Sessions[1].ProcessId);
        Assert.AreEqual("C:\\Second", terminal.Sessions[1].WorkingDirectory);
    }

    [TestMethod]
    public async Task ApplyValidatedAsyncPersistenceFailureRollsDesktopThenTerminalBack()
    {
        var previous = new AppSettings();
        var requested = CreateRequestedSettings();
        var operations = new List<string>();
        var terminalCall = 0;
        var desktopCall = 0;
        using var service = CreateService(previous,
                                          settings =>
                                          {
                                              terminalCall++;
                                              operations.Add(terminalCall == 1 ? "terminal-apply" : "terminal-rollback");

                                              return TerminalApplied(settings, previous);
                                          },
                                          settings =>
                                          {
                                              desktopCall++;
                                              operations.Add(desktopCall == 1 ? "desktop-apply" : "desktop-rollback");

                                              return DesktopApplied(settings, previous);
                                          },
                                          (settings, cancellationToken) =>
                                          {
                                              _ = settings;
                                              _ = cancellationToken;
                                              operations.Add("persistence");

                                              throw new IOException("Atomic replace failed.");
                                          },
                                          settings =>
                                          {
                                              operations.Add(settings == requested ? "host-apply" : "host-rollback");
                                          });

        var result = await service.ApplyValidatedAsync(requested, CancellationToken.None);

        Assert.AreEqual(PreferenceApplyStatus.FailedAndRestored, result.Status);
        CollectionAssert.AreEqual(PersistenceFailureOperations, operations);
        Assert.AreEqual(previous, result.EffectiveSettings);
        Assert.AreEqual(previous, result.PersistedSettings);
    }

    [TestMethod]
    public async Task SaveAsyncRollbackFailureReportsPersistedAndEffectiveStatesAndAllowsRetry()
    {
        var previous = new AppSettings();
        var requested = CreateRequestedSettings();
        var terminalCall = 0;
        var desktopCall = 0;
        var persistenceCall = 0;
        using var service = CreateService(previous,
                                          settings =>
                                          {
                                              terminalCall++;
                                              if (terminalCall == 1 || terminalCall == 3)
                                              {
                                                  return TerminalApplied(settings, previous);
                                              }

                                              return new TerminalSettingsApplyResult(settings,
                                                                                     SettingsApplicationService.ToTerminalSettings(requested),
                                                                                     SettingsApplicationService.ToTerminalSettings(requested),
                                                                                     TerminalSettingsApplyStatus.FailedAndRestoreIncomplete,
                                                                                     "Renderer rollback failed.");
                                          },
                                          settings =>
                                          {
                                              desktopCall++;
                                              if (desktopCall == 1 || desktopCall == 3)
                                              {
                                                  return DesktopApplied(settings, previous);
                                              }

                                              return new DesktopSettingsApplyResult(settings,
                                                                                    SettingsApplicationService.ToDesktopSettings(requested),
                                                                                    SettingsApplicationService.ToDesktopSettings(requested),
                                                                                    DesktopSettingsApplyStatus.FailedAndRestoreIncomplete,
                                                                                    [
                                                                                        new DesktopSettingsOperationResult(DesktopSettingsOperation.Hotkeys,
                                                                                                                           DesktopSettingsOperationStatus.RestoreFailed,
                                                                                                                           "Shortcut rollback failed."),
                                                                                    ]);
                                          },
                                          (settings, cancellationToken) =>
                                          {
                                              _ = settings;
                                              _ = cancellationToken;
                                              persistenceCall++;
                                              if (persistenceCall == 1)
                                              {
                                                  throw new IOException("Save failed.");
                                              }

                                              return Task.CompletedTask;
                                          });

        await Assert.ThrowsExactlyAsync<SettingsApplicationException>(() => service.SaveAsync(requested, CancellationToken.None));

        Assert.AreEqual(PreferenceApplyStatus.FailedAndRestoreIncomplete, service.LastResult!.Status);
        Assert.AreEqual(previous, service.PersistedSettings);
        Assert.AreEqual(requested, service.EffectiveSettings);
        StringAssert.Contains(service.StatusMessage, "마지막 저장");
        StringAssert.Contains(service.StatusMessage, "실제 적용");

        await service.SaveAsync(requested, CancellationToken.None);

        Assert.AreEqual(PreferenceApplyStatus.Applied, service.LastResult!.Status);
        Assert.AreEqual(requested, service.PersistedSettings);
    }

    [TestMethod]
    public async Task ApplyValidatedAsyncCanceledBeforeApplyDoesNotChangeLiveModules()
    {
        var previous = new AppSettings();
        var applyCalls = 0;
        using var service = CreateService(previous,
                                          settings =>
                                          {
                                              applyCalls++;
                                              return TerminalApplied(settings, previous);
                                          },
                                          settings =>
                                          {
                                              applyCalls++;
                                              return DesktopApplied(settings, previous);
                                          });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplyValidatedAsync(CreateRequestedSettings(), cancellation.Token));

        Assert.AreEqual(0, applyCalls);
        Assert.AreEqual(previous, service.PersistedSettings);
        Assert.AreEqual(previous, service.EffectiveSettings);
    }

    private static SettingsApplicationService CreateService(AppSettings previous,
                                                            Func<TerminalSettings, TerminalSettingsApplyResult> applyTerminal,
                                                            Func<DesktopSettings, DesktopSettingsApplyResult> applyDesktop,
                                                            Func<AppSettings, CancellationToken, Task>? persist = null,
                                                            Action<AppSettings>? applyHost = null)
    {
        return new SettingsApplicationService(applyTerminal, applyDesktop,
                                              persist ?? ((settings, cancellationToken) =>
                                              {
                                                  _ = settings;
                                                  _ = cancellationToken;

                                                  return Task.CompletedTask;
                                              }),
                                              previous, previous, applyHost ?? (_ => { }), new NullDiagnosticLog());
    }

    private static AppSettings CreateRequestedSettings()
    {
        return new AppSettings
        {
            ShellExecutable = "pwsh.exe",
            Theme = "One Dark",
            CollapsedHeightDip = 260,
            FontFamily = "Consolas",
            FontSize = 15,
            Opacity = 0.9,
            StartWithWindows = false,
            PreferredMonitor = "Taskbar",
            ExpandShortcut = "Ctrl+Shift+E",
            ActivationShortcut = "Ctrl+Shift+S",
        };
    }

    private static TerminalSettingsApplyResult TerminalApplied(TerminalSettings settings, AppSettings previous)
    {
        return new TerminalSettingsApplyResult(settings, SettingsApplicationService.ToTerminalSettings(previous),
                                               settings, TerminalSettingsApplyStatus.Applied, null);
    }

    private static DesktopSettingsApplyResult DesktopApplied(DesktopSettings settings, AppSettings previous)
    {
        return new DesktopSettingsApplyResult(settings, SettingsApplicationService.ToDesktopSettings(previous),
                                              settings, DesktopSettingsApplyStatus.Applied, []);
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

    private sealed class SessionPreservingTerminalRuntime
    {
        private TerminalSettings currentSettings;

        internal SessionPreservingTerminalRuntime(AppSettings settings)
        {
            currentSettings = SettingsApplicationService.ToTerminalSettings(settings);
            Sessions =
            [
                new FakeSession(101, "C:\\First", settings.ShellExecutable),
                new FakeSession(202, "C:\\Second", settings.ShellExecutable),
            ];
        }

        internal List<FakeSession> Sessions { get; }

        internal TerminalSettingsApplyResult ApplySettings(TerminalSettings settings)
        {
            var previous = currentSettings;
            currentSettings = settings;

            return new TerminalSettingsApplyResult(settings, previous, settings, TerminalSettingsApplyStatus.Applied,
                                                   null);
        }

        internal FakeSession CreateSession(int processId, string workingDirectory)
        {
            var session = new FakeSession(processId, workingDirectory, currentSettings.DefaultShellExecutable);
            Sessions.Add(session);

            return session;
        }
    }

    private sealed record FakeSession(int ProcessId, string WorkingDirectory, string? ShellExecutable);
}
