namespace Starboard.Windows.Composition;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;

    internal SingleInstanceGuard(string name)
    {
        mutex = new Mutex(true, name, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    internal bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (IsPrimaryInstance == true)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}
