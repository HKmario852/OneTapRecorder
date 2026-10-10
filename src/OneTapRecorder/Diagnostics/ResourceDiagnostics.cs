using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
namespace OneTapRecorder;
public partial class MainWindow
{
    private long uiTimerTicks;
    private async Task<object> MeasureResources(string phase)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        TimeSpan cpu = process.TotalProcessorTime;
        long ticks = uiTimerTicks;
        var wall = Stopwatch.StartNew();
        long privateSum = 0, workingSum = 0;
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(5000);
            process.Refresh();
            privateSum += process.PrivateMemorySize64;
            workingSum += process.WorkingSet64;
        }
        return new { phase, wallSeconds = wall.Elapsed.TotalSeconds, cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
            cpuPercentOneCore = 100 * (process.TotalProcessorTime - cpu).TotalSeconds / wall.Elapsed.TotalSeconds,
            privateMiB = privateSum / 3.0 / 1048576, workingSetMiB = workingSum / 3.0 / 1048576, uiTimerTicks = uiTimerTicks - ticks };
    }
    private async Task RunResourceTest(string folder)
    {
        Directory.CreateDirectory(folder);
        try
        {
            preferences.Folder = folder;
            Microphone.IsChecked = false;
            SystemAudio.IsChecked = true;
            AudioMode.IsChecked = true;
            HideToTray();
            await Task.Delay(3000);
            var results = new List<object> { await MeasureResources("hiddenIdle") };
            ToggleRecording();
            await AwaitRecording();
            await Task.Delay(2000);
            results.Add(await MeasureResources("hiddenAudioRecording"));
            var completion = completed!.Task;
            StopRecording();
            Require(await completion.WaitAsync(TimeSpan.FromSeconds(15)), lastError ?? "Audio capture failed.");
            await Task.Delay(1000);
            results.Add(await MeasureResources("hiddenIdleAfterRecording"));
            File.WriteAllText(Path.Combine(folder, "resource-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "resource-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
        finally { exitRequested = true; Close(); }
    }
}
