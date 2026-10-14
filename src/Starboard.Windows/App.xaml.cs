using System.Windows;
using System.Windows.Threading;
using Starboard.SharedKernel.Diagnostics;
using Starboard.Windows.Composition;
using Starboard.Windows.Infrastructure;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Starboard.Windows;

public partial class App : Application, IDisposable
{
    private AppCoordinator? coordinator;
    private SingleInstanceGuard? singleInstanceGuard;
    private bool isDisposed;
    private readonly FileDiagnosticLog diagnosticLog = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += ObserveDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += ObserveUnhandledException;

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
            MessageBox.Show(exception.Message, "Starboard를 시작할 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            if (coordinator is null)
            {
                Shutdown(1);
                return;
            }

            await coordinator.RequestApplicationShutdownAsync(1);
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
        DispatcherUnhandledException -= ObserveDispatcherException;
        AppDomain.CurrentDomain.UnhandledException -= ObserveUnhandledException;
        coordinator?.Dispose();
        singleInstanceGuard?.Dispose();
        coordinator = null;
        singleInstanceGuard = null;
        GC.SuppressFinalize(this);
    }

    private void ObserveDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs eventArguments)
    {
        diagnosticLog.Write(DiagnosticLevel.Error, "Host", "DispatcherUnhandled",
                            "Unhandled dispatcher exception; normal failure policy retained.", eventArguments.Exception);
        // Recovery belongs to the callback that owns the resource. Do not set Handled here.
    }

    private void ObserveUnhandledException(object sender, UnhandledExceptionEventArgs eventArguments)
    {
        diagnosticLog.Write(DiagnosticLevel.Error, "Host",
                            eventArguments.IsTerminating == true ? "Unhandled.Terminating" : "Unhandled.NonTerminating",
                            "Unhandled managed exception.", eventArguments.ExceptionObject as Exception);
    }
}
