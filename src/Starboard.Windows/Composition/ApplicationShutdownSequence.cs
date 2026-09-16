namespace Starboard.Windows.Composition;

internal sealed class ApplicationShutdownSequence
{
    private readonly Func<Task> cleanupAsync;
    private readonly Action<int> shutdownApplication;
    private readonly Action<Exception> observeCleanupFailure;
    private readonly Lock stateLock = new();

    private Task? requestTask;

    internal ApplicationShutdownSequence(Func<Task> cleanupAsync, Action<int> shutdownApplication,
                                         Action<Exception> observeCleanupFailure)
    {
        ArgumentNullException.ThrowIfNull(cleanupAsync);
        ArgumentNullException.ThrowIfNull(shutdownApplication);
        ArgumentNullException.ThrowIfNull(observeCleanupFailure);
        this.cleanupAsync = cleanupAsync;
        this.shutdownApplication = shutdownApplication;
        this.observeCleanupFailure = observeCleanupFailure;
    }

    internal Task RequestAsync(int exitCode = 0)
    {
        lock (stateLock)
        {
            requestTask ??= ExecuteAsync(exitCode);

            return requestTask;
        }
    }

    private async Task ExecuteAsync(int exitCode)
    {
        try
        {
            await cleanupAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            observeCleanupFailure(exception);
            shutdownApplication(1);
            return;
        }

        shutdownApplication(exitCode);
    }
}
