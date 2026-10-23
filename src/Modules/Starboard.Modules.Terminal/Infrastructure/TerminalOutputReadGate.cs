namespace Starboard.Modules.Terminal.Infrastructure;

internal sealed class TerminalOutputReadGate
{
    private readonly Lock _stateLock = new();
    private TaskCompletionSource? _resume;

    internal void SetPaused(bool paused)
    {
        lock (_stateLock)
        {
            if (paused == true)
            {
                _resume ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else
            {
                _resume?.TrySetResult();
                _resume = null;
            }
        }
    }

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? wait;
            lock (_stateLock)
            {
                wait = _resume?.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (wait is null)
            {
                return;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
