using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class DesktopWindowIntegrationTests
{
    [TestMethod]
    [Timeout(10_000)]
    public async Task LocalWpfPanelPlacementPreservesForegroundAndRestoresCollapsedBounds()
    {
        if (OperatingSystem.IsWindows() == false)
        {
            Assert.Inconclusive("The WPF window smoke test is only available on Windows.");
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunLocalWpfPanelSmoke(completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void ReconcileDisplayAndDpiChangesUsesLatestSafeGeometryWithoutActivation()
    {
        var runtime = new FakeDesktopIntegrationRuntime
        {
            Geometry = CreateGeometry(
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1040),
                new PixelRect(0, 1040, 1920, 1080),
                new DisplayDpi(96, 96)),
        };
        using var module = CreateModule(runtime);
        module.Attach(new nint(42), new PanelOptions(200));

        Assert.AreEqual(new PixelRect(0, 840, 1920, 1040), runtime.Placements[0]);

        runtime.Geometry = CreateGeometry(
            new PixelRect(-2560, 0, 0, 1440),
            new PixelRect(-2560, 0, 0, 1400),
            new PixelRect(-2560, 1400, 0, 1440),
            new DisplayDpi(144, 144));
        runtime.RaiseEnvironmentChanged();

        Assert.AreEqual(
            new PixelRect(-2560, 1100, 0, 1400),
            runtime.Placements[runtime.Placements.Count - 1]);

        var suggestedRectangle = new NativeRect
        {
            Left = -2500,
            Top = 1000,
            Right = -100,
            Bottom = 1300,
        };
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(suggestedRectangle, rectanglePointer, false);
            _ = module.HandleWindowMessage(new WindowMessage(
                new nint(42),
                DesktopIntegrationModule.WindowMessageDpiChanged,
                PackDpi(144, 144),
                rectanglePointer));
        }
        finally
        {
            Marshal.FreeHGlobal(rectanglePointer);
        }

        Assert.AreEqual(
            new PixelRect(-2560, 1100, 0, 1400),
            runtime.Placements[runtime.Placements.Count - 1]);
        Assert.AreEqual(0, runtime.ActivationCount);
    }

    [TestMethod]
    public void ReconcileUserHiddenAcrossFullscreenAndExplorerRecoveryRemainsHidden()
    {
        var runtime = new FakeDesktopIntegrationRuntime
        {
            Geometry = CreateDefaultGeometry(),
        };
        using var module = CreateModule(runtime);
        var presentations = new List<bool>();
        module.PanelPresentationRequested += presentations.Add;
        module.Attach(new nint(42), new PanelOptions(200));

        module.SetPanelVisible(false);
        runtime.Fullscreen = CreateFullscreen(PanelFullscreenState.FullscreenOnPanelMonitor);
        runtime.RaiseForegroundChanged();
        _ = module.HandleWindowMessage(new WindowMessage(
            new nint(42),
            unchecked((int)runtime.TaskbarCreatedMessage),
            0,
            0));
        runtime.Fullscreen = CreateFullscreen(PanelFullscreenState.Normal);
        runtime.RaiseForegroundChanged();

        Assert.HasCount(2, presentations);
        Assert.IsTrue(presentations[0]);
        Assert.IsFalse(presentations[1]);
        Assert.AreEqual(1, runtime.TrayRecreationCount);
        Assert.AreEqual(0, runtime.ActivationCount);
    }

    [TestMethod]
    public void ReconcileFullscreenEntryAndExitSuppressesThenRestoresWithoutFocusChange()
    {
        var runtime = new FakeDesktopIntegrationRuntime
        {
            Geometry = CreateDefaultGeometry(),
        };
        using var module = CreateModule(runtime);
        var presentations = new List<bool>();
        module.PanelPresentationRequested += presentations.Add;
        module.Attach(new nint(42), new PanelOptions(200));

        runtime.Fullscreen = CreateFullscreen(PanelFullscreenState.FullscreenOnPanelMonitor);
        runtime.RaiseForegroundChanged();
        module.ActivatePanel();

        Assert.AreEqual(0, runtime.ActivationCount);

        runtime.Fullscreen = CreateFullscreen(PanelFullscreenState.Normal);
        runtime.RaiseForegroundChanged();

        Assert.HasCount(3, presentations);
        Assert.IsTrue(presentations[0]);
        Assert.IsFalse(presentations[1]);
        Assert.IsTrue(presentations[2]);
        Assert.AreEqual(0, runtime.ActivationCount);
        Assert.AreEqual(
            new PixelRect(0, 840, 1920, 1040),
            runtime.Placements[runtime.Placements.Count - 1]);
    }

    [TestMethod]
    public void ExpandCollapseAndMonitorRemovalRestoresSameFrameOrLatestConnectedWorkArea()
    {
        var runtime = new FakeDesktopIntegrationRuntime
        {
            Geometry = CreateDefaultGeometry(),
        };
        using var module = CreateModule(runtime);
        module.Attach(new nint(42), new PanelOptions(200));
        var originalCollapsedBounds = runtime.Placements[runtime.Placements.Count - 1];

        module.ToggleExpanded();
        module.ToggleExpanded();

        Assert.AreEqual(
            originalCollapsedBounds,
            runtime.Placements[runtime.Placements.Count - 1]);

        module.ToggleExpanded();
        runtime.Geometry = CreateGeometry(
            new PixelRect(-1280, 0, 0, 1024),
            new PixelRect(-1280, 0, 0, 984),
            new PixelRect(-1280, 984, 0, 1024),
            new DisplayDpi(96, 96),
            new nint(2));
        module.ToggleExpanded();

        Assert.AreEqual(
            new PixelRect(-1280, 784, 0, 984),
            runtime.Placements[runtime.Placements.Count - 1]);
        Assert.AreEqual(0, runtime.ActivationCount);
    }

    [TestMethod]
    public void ExplicitSummonWithConcealedTaskbarShowsAndActivatesOnlyOutsideFullscreen()
    {
        var runtime = new FakeDesktopIntegrationRuntime
        {
            Geometry = CreateDefaultGeometry(TaskbarPresence.Concealed),
        };
        using var module = CreateModule(runtime);
        var presentations = new List<bool>();
        module.PanelPresentationRequested += presentations.Add;
        module.Attach(new nint(42), new PanelOptions(200));

        module.ActivatePanel();

        Assert.AreEqual(1, runtime.ActivationCount);
        Assert.HasCount(2, presentations);
        Assert.IsFalse(presentations[0]);
        Assert.IsTrue(presentations[1]);

        runtime.Fullscreen = CreateFullscreen(PanelFullscreenState.FullscreenOnPanelMonitor);
        runtime.RaiseForegroundChanged();
        module.ActivatePanel();

        Assert.AreEqual(1, runtime.ActivationCount);
        Assert.HasCount(3, presentations);
        Assert.IsFalse(presentations[0]);
        Assert.IsTrue(presentations[1]);
        Assert.IsFalse(presentations[2]);
    }

    [TestMethod]
    public void TraySignalsAfterExplorerRecoveryAreForwardedExactlyOnce()
    {
        var runtime = new FakeDesktopIntegrationRuntime
        {
            Geometry = CreateDefaultGeometry(),
        };
        using var module = CreateModule(runtime);
        var toggleCount = 0;
        var summonCount = 0;
        var exitCount = 0;
        module.PanelVisibilityToggleRequested += (_, _) => toggleCount++;
        module.PanelSummonRequested += (_, _) => summonCount++;
        module.ExitRequested += (_, _) => exitCount++;
        module.Attach(new nint(42), new PanelOptions(200));

        runtime.RaiseTraySignals();
        _ = module.HandleWindowMessage(new WindowMessage(
            new nint(42),
            unchecked((int)runtime.TaskbarCreatedMessage),
            0,
            0));
        runtime.RaiseTraySignals();

        Assert.AreEqual(2, toggleCount);
        Assert.AreEqual(2, summonCount);
        Assert.AreEqual(2, exitCount);
        Assert.AreEqual(1, runtime.TrayRecreationCount);
    }

    private static DesktopIntegrationModule CreateModule(
        FakeDesktopIntegrationRuntime runtime)
    {
        return new DesktopIntegrationModule(
            new NullDiagnosticLog(),
            runtime,
            false);
    }

    private static void RunLocalWpfPanelSmoke(TaskCompletionSource completion)
    {
        Window? window = null;
        DesktopIntegrationModule? module = null;

        try
        {
            var runtime = new LocalWindowSmokeRuntime();
            var foregroundBeforeShow = runtime.ForegroundWindow;
            window = new Window
            {
                AllowsTransparency = false,
                Height = 200,
                ResizeMode = ResizeMode.NoResize,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = false,
                Width = 900,
                WindowStyle = WindowStyle.None,
            };
            window.Show();

            var windowHandle = new WindowInteropHelper(window).Handle;
            module = new DesktopIntegrationModule(
                new NullDiagnosticLog(),
                runtime,
                false);
            module.SetPanelEngaged(true);
            module.PanelPresentationRequested += isVisible =>
            {
                if (isVisible == true)
                {
                    window.Show();
                    return;
                }

                window.Hide();
            };
            module.Attach(windowHandle, new PanelOptions(200));

            var collapsedBounds = runtime.GetWindowBounds(windowHandle);
            var workArea = runtime.CurrentGeometry.Monitor.WorkArea;
            Assert.AreEqual(TaskbarEdge.Bottom, runtime.CurrentGeometry.Taskbar.Edge);
            Assert.IsTrue(IsWithin(collapsedBounds, workArea));

            module.ToggleExpanded();
            var expandedBounds = runtime.GetWindowBounds(windowHandle);
            Assert.AreEqual(workArea, expandedBounds);

            module.ToggleExpanded();
            var restoredBounds = runtime.GetWindowBounds(windowHandle);
            Assert.AreEqual(collapsedBounds, restoredBounds);
            Assert.AreEqual(foregroundBeforeShow, runtime.ForegroundWindow);
            completion.SetResult();
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
        finally
        {
            module?.Dispose();
            window?.Close();
        }
    }

    private static bool IsWithin(PixelRect bounds, PixelRect workArea)
    {
        return bounds.IsEmpty == false &&
            bounds.Left >= workArea.Left &&
            bounds.Top >= workArea.Top &&
            bounds.Right <= workArea.Right &&
            bounds.Bottom <= workArea.Bottom;
    }

    private static DesktopGeometrySnapshot CreateDefaultGeometry(
        TaskbarPresence taskbarPresence = TaskbarPresence.Visible)
    {
        return CreateGeometry(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            new PixelRect(0, 1040, 1920, 1080),
            new DisplayDpi(96, 96),
            new nint(1),
            taskbarPresence);
    }

    private static DesktopGeometrySnapshot CreateGeometry(
        PixelRect monitorBounds,
        PixelRect workArea,
        PixelRect taskbarBounds,
        DisplayDpi dpi,
        nint monitorHandle = default,
        TaskbarPresence taskbarPresence = TaskbarPresence.Visible)
    {
        if (monitorHandle == 0)
        {
            monitorHandle = new nint(1);
        }

        return new DesktopGeometrySnapshot(
            new TaskbarSnapshot(
                TaskbarEdge.Bottom,
                taskbarBounds,
                monitorBounds,
                workArea,
                taskbarPresence != TaskbarPresence.Visible,
                dpi.Y),
            new MonitorSnapshot(
                monitorHandle,
                monitorBounds,
                workArea,
                dpi),
            taskbarPresence,
            DisplayTrackingState.Tracked,
            null,
            null);
    }

    private static FullscreenObservation CreateFullscreen(PanelFullscreenState state)
    {
        return new FullscreenObservation(
            state,
            true,
            null,
            PanelWindowActivation.PreserveForeground,
            PanelWindowZOrder.PreserveNormal);
    }

    private static nuint PackDpi(uint dpiX, uint dpiY)
    {
        return dpiX | ((nuint)dpiY << 16);
    }

    private sealed class FakeDesktopIntegrationRuntime : IDesktopIntegrationRuntime
    {
        public uint TaskbarCreatedMessage { get; } = 0xC123;

        public DesktopGeometrySnapshot Geometry { get; set; } = CreateDefaultGeometry();

        public FullscreenObservation Fullscreen { get; set; } =
            CreateFullscreen(PanelFullscreenState.Normal);

        public List<PixelRect> Placements { get; } = [];

        public int ActivationCount { get; private set; }

        public int TrayRecreationCount { get; private set; }

        public event EventHandler? EnvironmentChanged;

        public event EventHandler? ForegroundChanged;

        public event EventHandler? TrayToggleVisibilityRequested;

        public event EventHandler? TraySummonRequested;

        public event EventHandler? TrayExitRequested;

        public void Attach(nint windowHandle)
        {
            Assert.AreNotEqual(0, windowHandle);
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
            _ = identifier;
        }

        public DesktopGeometrySnapshot CaptureGeometry(nint panelWindowHandle)
        {
            _ = panelWindowHandle;
            return Geometry;
        }

        public FullscreenObservation CaptureFullscreen(
            nint panelWindowHandle,
            MonitorSnapshot panelMonitor)
        {
            _ = panelWindowHandle;
            _ = panelMonitor;
            return Fullscreen;
        }

        public void PlaceWithoutActivation(nint windowHandle, PixelRect bounds)
        {
            _ = windowHandle;
            Placements.Add(bounds);
        }

        public void ActivateOnExplicitRequest(nint windowHandle)
        {
            _ = windowHandle;
            ActivationCount++;
        }

        public void SetTrayPanelVisible(bool isVisible)
        {
            _ = isVisible;
        }

        public void RecreateTrayIcon(bool isVisible)
        {
            _ = isVisible;
            TrayRecreationCount++;
        }

        public void Dispose()
        {
        }

        internal void RaiseEnvironmentChanged()
        {
            EnvironmentChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void RaiseForegroundChanged()
        {
            ForegroundChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void RaiseTraySignals()
        {
            TrayToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
            TraySummonRequested?.Invoke(this, EventArgs.Empty);
            TrayExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class LocalWindowSmokeRuntime : IDesktopIntegrationRuntime
    {
        private readonly DesktopNativeApi _nativeApi = new();
        private readonly TaskbarService _taskbarService;

        internal LocalWindowSmokeRuntime()
        {
            _taskbarService = new TaskbarService(_nativeApi);
        }

        public uint TaskbarCreatedMessage => 0;

        internal nint ForegroundWindow => _nativeApi.GetForegroundWindow();

        internal DesktopGeometrySnapshot CurrentGeometry { get; private set; } =
            CreateDefaultGeometry();

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

        public event EventHandler? TrayExitRequested
        {
            add { }
            remove { }
        }

        public void Attach(nint windowHandle)
        {
            WindowPlacementService.ConfigureToolWindow(windowHandle);
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
            _ = identifier;
        }

        public DesktopGeometrySnapshot CaptureGeometry(nint panelWindowHandle)
        {
            CurrentGeometry = _taskbarService.CaptureLatest(panelWindowHandle);
            return CurrentGeometry;
        }

        public FullscreenObservation CaptureFullscreen(
            nint panelWindowHandle,
            MonitorSnapshot panelMonitor)
        {
            _ = panelWindowHandle;
            _ = panelMonitor;
            return CreateFullscreen(PanelFullscreenState.Normal);
        }

        public void PlaceWithoutActivation(nint windowHandle, PixelRect bounds)
        {
            WindowPlacementService.PlaceWithoutActivation(windowHandle, bounds);
        }

        public void ActivateOnExplicitRequest(nint windowHandle)
        {
            throw new InvalidOperationException(
                "The background placement smoke test must not activate its panel.");
        }

        public void SetTrayPanelVisible(bool isVisible)
        {
            _ = isVisible;
        }

        public void RecreateTrayIcon(bool isVisible)
        {
            _ = isVisible;
        }

        public PixelRect GetWindowBounds(nint windowHandle)
        {
            return _nativeApi.GetWindowBounds(windowHandle);
        }

        public void Dispose()
        {
        }
    }

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(
            DiagnosticLevel level,
            string subsystem,
            string operation,
            string message,
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
