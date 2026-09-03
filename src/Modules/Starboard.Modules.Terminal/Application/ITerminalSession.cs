namespace Starboard.Modules.Terminal.Application;

internal interface ITerminalSession : IAsyncDisposable
{
    event Action<string>? OutputReceived;

    event Action<uint>? Exited;

    void BeginReading();

    ValueTask WriteAsync(string data, CancellationToken cancellationToken);

    void Resize(int columns, int rows);
}
