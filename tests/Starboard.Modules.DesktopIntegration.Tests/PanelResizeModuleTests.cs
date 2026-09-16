using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration.Tests;

#pragma warning disable CA1707 // Test names follow the repository's Scenario_ExpectedResult convention.

[TestClass]
public sealed class PanelResizeModuleTests
{
    [TestMethod]
    public void HandleWindowMessage_TopEdgeWhileCollapsed_ReturnsTopHitTestResult()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime);
        var message = CreatePointMessage(NativeMethods.WindowMessageNonClientHitTest, 500, 602);

        var handled = module.HandleWindowMessage(message, out var result);

        Assert.IsTrue(handled);
        Assert.AreEqual(new nint(12), result);
    }

    [TestMethod]
    public void HandleWindowMessage_TopEdgeWhileExpanded_ReturnsClientHitTestResult()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime);
        module.ToggleExpanded();
        var message = CreatePointMessage(NativeMethods.WindowMessageNonClientHitTest, 500, 1);

        var handled = module.HandleWindowMessage(message, out var result);

        Assert.IsTrue(handled);
        Assert.AreEqual(new nint(1), result);
    }

    [TestMethod]
    public void BeginCollapsedPanelResize_WhileCollapsed_RequestsNativeTopResizeWithoutActivation()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime);

        var started = module.BeginCollapsedPanelResize();

        Assert.IsTrue(started);
        Assert.AreEqual(1, runtime.TopResizeRequestCount);
        Assert.AreEqual(0, runtime.ActivationRequestCount);
    }

    [TestMethod]
    public void BeginCollapsedPanelResize_WhileExpanded_DoesNotRequestNativeResize()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime);
        module.ToggleExpanded();

        var started = module.BeginCollapsedPanelResize();

        Assert.IsFalse(started);
        Assert.AreEqual(0, runtime.TopResizeRequestCount);
    }

    [TestMethod]
    public void HandleWindowMessage_TopSizing_PreviewsClampedHeightWithoutMovingBottomOrTakingFocus()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime);
        var changes = new List<PanelCollapsedHeightChangeEventArgs>();
        module.PanelCollapsedHeightChangeRequested += (_, change) => changes.Add(change);
        Assert.IsTrue(module.HandleWindowMessage(CreateMessage(NativeMethods.WindowMessageEnterSizeMove),
                                                 out _));
        var placementCountBeforeSizing = runtime.PlacedBounds.Count;

        var rectanglePointer = AllocateRectangle(new PixelRect(0, 540, 1000, 900));
        try
        {
            var sizing = new WindowMessage(new nint(1), NativeMethods.WindowMessageSizing,
                                           NativeMethods.SizingEdgeTop, rectanglePointer);

            var handled = module.HandleWindowMessage(sizing, out var result);

            Assert.IsTrue(handled);
            Assert.AreEqual(new nint(1), result);
            Assert.AreEqual(new PixelRect(0, 540, 1000, 900), ReadRectangle(rectanglePointer));
            Assert.AreEqual(placementCountBeforeSizing, runtime.PlacedBounds.Count);
            Assert.AreEqual(0, runtime.ActivationRequestCount);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(PanelCollapsedHeightChangePhase.Preview, changes[0].Phase);
            Assert.AreEqual(240, changes[0].RequestedHeightDip);
            Assert.AreEqual(200, changes[0].LastSavedHeightDip);

            module.Refresh();

            Assert.AreEqual(placementCountBeforeSizing, runtime.PlacedBounds.Count,
                            "Background reconciliation must not fight the native sizing loop.");
        }
        finally
        {
            Marshal.FreeHGlobal(rectanglePointer);
        }

        var exitHandled = module.HandleWindowMessage(
            CreateMessage(NativeMethods.WindowMessageExitSizeMove), out var exitResult);

        Assert.IsTrue(exitHandled);
        Assert.AreEqual(nint.Zero, exitResult);
        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(PanelCollapsedHeightChangePhase.Commit, changes[1].Phase);
        Assert.AreEqual(240, changes[1].RequestedHeightDip);
        Assert.AreEqual(changes[0].RequestId, changes[1].RequestId);
        Assert.AreEqual(0, runtime.ActivationRequestCount);
    }

    [TestMethod]
    public void HandleWindowMessage_ExitSizingTwice_RequestsOneSaveCommit()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime);
        var commitCount = 0;
        module.PanelCollapsedHeightChangeRequested += (_, change) =>
        {
            if (change.Phase == PanelCollapsedHeightChangePhase.Commit)
            {
                commitCount++;
            }
        };
        _ = module.HandleWindowMessage(CreateMessage(NativeMethods.WindowMessageEnterSizeMove), out _);
        var rectanglePointer = AllocateRectangle(new PixelRect(0, 540, 1000, 900));
        try
        {
            var sizingMessage = new WindowMessage(new nint(1), NativeMethods.WindowMessageSizing,
                                                  NativeMethods.SizingEdgeTop, rectanglePointer);
            _ = module.HandleWindowMessage(sizingMessage, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(rectanglePointer);
        }

        _ = module.HandleWindowMessage(CreateMessage(NativeMethods.WindowMessageExitSizeMove), out _);
        _ = module.HandleWindowMessage(CreateMessage(NativeMethods.WindowMessageExitSizeMove), out _);

        Assert.AreEqual(1, commitCount);
        Assert.AreEqual(0, runtime.ActivationRequestCount);
    }

    [TestMethod]
    public void HandleWindowMessage_TopEdgeDoubleClick_RestoresDefaultAndRequestsCommitWithoutActivation()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime, 260);
        PanelCollapsedHeightChangeEventArgs? committedChange = null;
        module.PanelCollapsedHeightChangeRequested += (_, change) => committedChange = change;
        var message = new WindowMessage(new nint(1),
                                        NativeMethods.WindowMessageNonClientLeftButtonDoubleClick,
                                        NativeMethods.HitTestTop, 0);

        var handled = module.HandleWindowMessage(message, out var result);

        Assert.IsTrue(handled);
        Assert.AreEqual(nint.Zero, result);
        Assert.AreEqual(new PixelRect(0, 600, 1000, 900), runtime.PlacedBounds[runtime.PlacedBounds.Count - 1]);
        Assert.IsNotNull(committedChange);
        Assert.AreEqual(PanelCollapsedHeightChangePhase.Commit, committedChange.Phase);
        Assert.AreEqual(200, committedChange.RequestedHeightDip);
        Assert.AreEqual(260, committedChange.LastSavedHeightDip);
        Assert.AreEqual(0, runtime.ActivationRequestCount);
    }

    [TestMethod]
    public void HandleWindowMessage_TopEdgeDoubleClickWhileExpanded_DoesNotResize()
    {
        var runtime = new ResizeRuntime();
        using var module = CreateAttachedModule(runtime);
        module.ToggleExpanded();
        var placementCountBeforeDoubleClick = runtime.PlacedBounds.Count;
        var message = new WindowMessage(new nint(1),
                                        NativeMethods.WindowMessageNonClientLeftButtonDoubleClick,
                                        NativeMethods.HitTestTop, 0);

        var handled = module.HandleWindowMessage(message, out _);

        Assert.IsFalse(handled);
        Assert.AreEqual(placementCountBeforeDoubleClick, runtime.PlacedBounds.Count);
    }

    private static DesktopIntegrationModule CreateAttachedModule(ResizeRuntime runtime,
                                                                 double collapsedHeightDip = 200)
    {
        var module = new DesktopIntegrationModule(new NullDiagnosticLog(), runtime, false);
        module.Attach(new nint(1), new PanelOptions(collapsedHeightDip));

        return module;
    }

    private static WindowMessage CreateMessage(int messageId)
    {
        return new WindowMessage(new nint(1), messageId, 0, 0);
    }

    private static WindowMessage CreatePointMessage(int messageId, short x, short y)
    {
        var packedCoordinates = unchecked((uint)(ushort)x | ((uint)(ushort)y << 16));
        return new WindowMessage(new nint(1), messageId, 0, new nint(packedCoordinates));
    }

    private static nint AllocateRectangle(PixelRect bounds)
    {
        var pointer = Marshal.AllocHGlobal(sizeof(int) * 4);
        Marshal.WriteInt32(pointer, 0, bounds.Left);
        Marshal.WriteInt32(pointer, sizeof(int), bounds.Top);
        Marshal.WriteInt32(pointer, sizeof(int) * 2, bounds.Right);
        Marshal.WriteInt32(pointer, sizeof(int) * 3, bounds.Bottom);

        return pointer;
    }

    private static PixelRect ReadRectangle(nint pointer)
    {
        return new PixelRect(Marshal.ReadInt32(pointer, 0), Marshal.ReadInt32(pointer, sizeof(int)),
                             Marshal.ReadInt32(pointer, sizeof(int) * 2),
                             Marshal.ReadInt32(pointer, sizeof(int) * 3));
    }

    private sealed class ResizeRuntime : IDesktopIntegrationRuntime
    {
        private readonly DesktopGeometrySnapshot _geometry = new(
            new TaskbarSnapshot(TaskbarEdge.Bottom, new PixelRect(0, 900, 1000, 940),
                                new PixelRect(0, 0, 1000, 940), new PixelRect(0, 0, 1000, 900), false, 144),
            new MonitorSnapshot(new nint(1), new PixelRect(0, 0, 1000, 940),
                                new PixelRect(0, 0, 1000, 900), new DisplayDpi(144, 144)),
            TaskbarPresence.Visible, DisplayTrackingState.Tracked, null, null);

        public uint TaskbarCreatedMessage => 0;

        public List<PixelRect> PlacedBounds { get; } = [];

        public int ActivationRequestCount { get; private set; }

        public int TopResizeRequestCount { get; private set; }

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
            _ = windowHandle;
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
            return _geometry;
        }

        public FullscreenObservation CaptureFullscreen(nint panelWindowHandle, MonitorSnapshot panelMonitor)
        {
            _ = panelWindowHandle;
            _ = panelMonitor;
            return new FullscreenObservation(PanelFullscreenState.Normal, true, null,
                                             PanelWindowActivation.PreserveForeground,
                                             PanelWindowZOrder.PreserveNormal);
        }

        public void PlaceWithoutActivation(nint windowHandle, PixelRect bounds)
        {
            _ = windowHandle;
            PlacedBounds.Add(bounds);
        }

        public bool BeginTopResize(nint windowHandle)
        {
            _ = windowHandle;
            TopResizeRequestCount++;

            return true;
        }

        public void ActivateOnExplicitRequest(nint windowHandle)
        {
            _ = windowHandle;
            ActivationRequestCount++;
        }

        public void SetTrayPanelVisible(bool isVisible)
        {
            _ = isVisible;
        }

        public void RecreateTrayIcon(bool isVisible)
        {
            _ = isVisible;
        }

        public void Dispose()
        {
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
