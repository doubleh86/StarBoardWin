using System.IO;
using Starboard.Windows.Composition;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class ApplicationShutdownSequenceTests
{
    [TestMethod]
    public async Task RequestAsyncCoalescesPendingCleanupAndShutsDownAfterCompletion()
    {
        var cleanupRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var sequence = new ApplicationShutdownSequence(
            async () =>
            {
                events.Add("cleanup-started");
                await cleanupRelease.Task.ConfigureAwait(false);
                events.Add("cleanup-completed");
            },
            exitCode => events.Add($"shutdown-{exitCode}"),
            exception => events.Add($"failure-{exception.GetType().Name}"));

        var firstRequest = sequence.RequestAsync(7);
        var secondRequest = sequence.RequestAsync(9);

        Assert.AreSame(firstRequest, secondRequest);
        Assert.HasCount(1, events);
        Assert.AreEqual("cleanup-started", events[0]);

        cleanupRelease.SetResult();
        await firstRequest;

        Assert.HasCount(3, events);
        Assert.AreEqual("cleanup-started", events[0]);
        Assert.AreEqual("cleanup-completed", events[1]);
        Assert.AreEqual("shutdown-7", events[2]);
    }

    [TestMethod]
    public async Task RequestAsyncObservesCleanupFailureAndRequestsFailureExit()
    {
        var failure = new InvalidOperationException("cleanup failed");
        Exception? observedFailure = null;
        int? requestedExitCode = null;
        var sequence = new ApplicationShutdownSequence(
            () => Task.FromException(failure),
            exitCode => requestedExitCode = exitCode,
            exception => observedFailure = exception);

        await sequence.RequestAsync();

        Assert.AreSame(failure, observedFailure);
        Assert.AreEqual(1, requestedExitCode);
    }

    [TestMethod]
    public void ApplicationShutdownWiringUsesExplicitModeWithoutBlockingOnExit()
    {
        var root = FindSolutionRoot();
        var appMarkup = File.ReadAllText(Path.Combine(root, "src", "Starboard.Windows", "App.xaml"));
        var appSource = File.ReadAllText(Path.Combine(root, "src", "Starboard.Windows", "App.xaml.cs"));
        var coordinatorSource = File.ReadAllText(Path.Combine(root, "src", "Starboard.Windows", "Composition",
                                                              "AppCoordinator.cs"));

        StringAssert.Contains(appMarkup, "ShutdownMode=\"OnExplicitShutdown\"");
        Assert.IsFalse(appSource.Contains("GetAwaiter().GetResult()", StringComparison.Ordinal));
        StringAssert.Contains(coordinatorSource, "RequestApplicationShutdownAsync");
        StringAssert.Contains(coordinatorSource, "new ApplicationShutdownSequence(ShutdownAsync");
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Starboard.Windows.sln")) == true)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Starboard.Windows.sln could not be located.");
    }
}
