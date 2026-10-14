using System.Runtime.InteropServices;

namespace Starboard.Modules.Terminal.Presentation;

internal enum ClipboardAccessStatus
{
    Completed,
    Unavailable,
    Stale,
}

internal sealed record ClipboardAccessResult<T>(ClipboardAccessStatus Status, T Value);

// Runs on the caller's STA synchronization context. Only the delay is asynchronous;
// clipboard calls must never be moved to Task.Run or a thread-pool continuation.
internal sealed class ClipboardAccess(Action verifyAccess, Func<CancellationToken, Task> delay)
{
    internal async Task<ClipboardAccessResult<T>> ExecuteAsync<T>(Func<T> operation, Func<bool> isCurrent,
                                                                  Action<ExternalException> observeFailure,
                                                                  CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            verifyAccess();
            if (cancellationToken.IsCancellationRequested == true || isCurrent() == false)
            {
                return new ClipboardAccessResult<T>(ClipboardAccessStatus.Stale, default!);
            }

            try
            {
                var value = operation();
                // OLE can pump messages, so even a synchronous clipboard call can reenter the host.
                var status = cancellationToken.IsCancellationRequested == false && isCurrent() == true
                    ? ClipboardAccessStatus.Completed : ClipboardAccessStatus.Stale;

                return new ClipboardAccessResult<T>(status, value);
            }
            catch (ExternalException exception) when (exception.HResult >= unchecked((int)0x800401D0) &&
                                                       exception.HResult <= unchecked((int)0x800401D4))
            {
                observeFailure(exception);
                // Failed OLE calls can also pump messages. Do not publish a final failure
                // to a replacement tab/renderer or wait again for an invalidated request.
                if (cancellationToken.IsCancellationRequested == true || isCurrent() == false)
                {
                    return new ClipboardAccessResult<T>(ClipboardAccessStatus.Stale, default!);
                }

                if (attempt == 2)
                {
                    return new ClipboardAccessResult<T>(ClipboardAccessStatus.Unavailable, default!);
                }
            }

            try
            {
                await delay(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                return new ClipboardAccessResult<T>(ClipboardAccessStatus.Stale, default!);
            }
        }

        throw new InvalidOperationException("The bounded clipboard operation did not complete.");
    }
}
