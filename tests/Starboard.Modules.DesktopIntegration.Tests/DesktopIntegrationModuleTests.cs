using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration.Tests;

[TestClass]
public sealed class DesktopIntegrationModuleTests
{
    [TestMethod]
    public void HandleWindowMessageWithActivationHotKeyRaisesActivationToggleRequest()
    {
        using var module = new DesktopIntegrationModule(new NullDiagnosticLog(),
                                                        _ => Assert.Fail("Hotkey handling must not apply opacity."));
        var requestCount = 0;
        module.PanelActivationToggleRequested += (_, _) => requestCount++;

        var handled = module.HandleWindowMessage(DesktopIntegrationModule.WindowMessageHotKey,
                                                 DesktopIntegrationModule.ActivationHotKeyIdentifier);

        Assert.IsTrue(handled);
        Assert.AreEqual(1, requestCount);
    }

    [TestMethod]
    public void HandleWindowMessageWithUnknownHotKeyDoesNotRaiseActivationToggleRequest()
    {
        using var module = new DesktopIntegrationModule(new NullDiagnosticLog(),
                                                        _ => Assert.Fail("Hotkey handling must not apply opacity."));
        var requestCount = 0;
        module.PanelActivationToggleRequested += (_, _) => requestCount++;

        var handled = module.HandleWindowMessage(DesktopIntegrationModule.WindowMessageHotKey, new nint(-1));

        Assert.IsFalse(handled);
        Assert.AreEqual(0, requestCount);
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
