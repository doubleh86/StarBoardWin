using System.Windows;
using Starboard.Windows.Composition;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Starboard.Windows;

public partial class App : Application, IDisposable
{
    private AppCoordinator? coordinator;
    private SingleInstanceGuard? singleInstanceGuard;
    private bool isDisposed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstanceGuard = new SingleInstanceGuard("Local\\Starboard.Windows.Singleton");
        if (singleInstanceGuard.IsPrimaryInstance == false)
        {
            Shutdown();
            return;
        }

        try
        {
            coordinator = new AppCoordinator();
            await coordinator.StartAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Starboard를 시작할 수 없습니다",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        coordinator?.Dispose();
        singleInstanceGuard?.Dispose();
        GC.SuppressFinalize(this);
    }
}
