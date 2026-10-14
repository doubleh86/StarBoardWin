using System.ComponentModel;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

[TestClass]
public sealed class SessionNotificationRegistrationTests
{
    [TestMethod]
    public void DisposeCurrentSessionAndWindowOnlyUnregistersOnceAndRejectsDelayedMessage()
    {
        var native = new FakeNative();
        var registration = new SessionNotificationRegistration(42, new Log(), native);
        Assert.IsTrue(registration.Matches(42, 12));
        Assert.IsFalse(registration.Matches(43, 12));
        Assert.IsFalse(registration.Matches(42, 13));
        registration.Dispose();
        registration.Dispose();
        Assert.AreEqual(1, native.UnregisterCount);
        Assert.IsFalse(registration.Matches(42, 12));
    }

    [TestMethod]
    public void RegisterFailureLogsCodeAndRetainsFallbackWithoutUnregistering()
    {
        var native = new FakeNative { FailRegistration = true };
        var log = new Log();
        using (var registration = new SessionNotificationRegistration(42, log, native))
        {
            Assert.IsFalse(registration.Matches(42, 12));
        }

        Assert.AreEqual(0, native.UnregisterCount);
        StringAssert.Contains(log.Message!, "Error=5");
        StringAssert.Contains(log.Message!, "fallback");
        Assert.IsFalse(log.Message!.Contains("private-user-text", StringComparison.Ordinal));
    }

    private sealed class FakeNative : ISessionNotificationNativeApi
    {
        internal bool FailRegistration { get; init; }
        internal int UnregisterCount { get; private set; }
        public uint GetCurrentSessionId() => 12;
        public void Register(nint windowHandle)
        {
            if (FailRegistration == true)
            {
                throw new Win32Exception(5, "private-user-text");
            }
        }

        public void Unregister(nint windowHandle) => UnregisterCount++;
    }

    private sealed class Log : IDiagnosticLog
    {
        internal string? Message { get; private set; }
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message, Exception? exception = null)
        {
            Assert.IsNull(exception);
            Message = message;
        }
    }
}
