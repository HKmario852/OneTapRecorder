using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace OneTapRecorder;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private EventWaitHandle? showSignal;
    private RegisteredWaitHandle? signalWait;
    private bool ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!OneTapRecorder.MainWindow.IsDiagnosticRun(e.Args))
        {
            instanceMutex = new Mutex(true, @"Local\OneTapRecorder.Instance", out ownsMutex);
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\OneTapRecorder.Show");
            if (!ownsMutex) { showSignal.Set(); Shutdown(); return; }
            signalWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(() => (MainWindow as OneTapRecorder.MainWindow)?.RestoreFromTray()), null, Timeout.Infinite, false);
        }
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        signalWait?.Unregister(null);
        showSignal?.Dispose();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
