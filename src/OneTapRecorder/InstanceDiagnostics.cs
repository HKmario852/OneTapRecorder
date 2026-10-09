using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace OneTapRecorder;

public partial class MainWindow
{
    private async Task RunInstanceTest(string folder)
    {
        Directory.CreateDirectory(folder);
        try
        {
            Close();
            Require(!IsVisible, "Instance test did not hide.");
            using var second = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })!;
            await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            for (int i = 0; i < 30 && !IsVisible; i++) await Task.Delay(100);
            Require(IsVisible && second.ExitCode == 0, "Second launch did not reopen the background instance.");
            File.WriteAllText(Path.Combine(folder, "instance-results.json"), "{\"secondLaunchReopensExistingInstance\":true,\"secondProcessExits\":true}");
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(folder, "instance-error.txt"), e.ToString()); Environment.ExitCode = 1; }
        finally { RequestExit(); }
    }
}
