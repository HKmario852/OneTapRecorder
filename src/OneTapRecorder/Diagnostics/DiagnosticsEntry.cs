using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OneTapRecorder;

// Diagnostics build only (dotnet build -c Diagnostics): the --*-test and --render-preview switches described in
// docs/VALIDATION.md. Release builds leave this folder out, so these switches do nothing there.
public partial class MainWindow
{
    private static readonly string[] DiagnosticSwitches =
        ["--language-test", "--resource-test", "--smoke-test", "--render-preview", "--hotkey-test", "--audio-tray-test", "--instance-test"];
    internal static bool IsDiagnosticRun(string[] args) => args.Any(a => DiagnosticSwitches.Contains(a) && a != "--instance-test");
    private bool DiagnosticMode => arguments.Any(DiagnosticSwitches.Contains);
    private bool PersistSettings => arguments.Contains("--language-test") || !DiagnosticMode || arguments.Contains("--hotkey-test") || arguments.Contains("--audio-tray-test");

    partial void UseDiagnosticSettingsPath(ref string path)
    {
        if (arguments.Contains("--language-test") || arguments.Contains("--hotkey-test") || arguments.Contains("--audio-tray-test"))
            path = Path.Combine(Path.GetFullPath(arguments.Last()), "settings.json");
    }
    partial void CountUiTick() => uiTimerTicks++;

    async partial void RunDiagnostics()
    {
        if (arguments.Contains("--render-preview"))
        {
            await Task.Delay(500);
            if (arguments.Contains("--english")) { preferences.Language = "en"; ApplyLanguage(); }
            if (arguments.Contains("--chinese")) { preferences.Language = "zh-Hant"; ApplyLanguage(); }
            if (arguments.Contains("--audio-mode")) AudioMode.IsChecked = true;
            if (arguments.Contains("--show-hotkey-editor")) { EditHotkeyClick(this, new RoutedEventArgs()); SelectHotkey(0, 0x77); await Task.Delay(100); }
            SavePreview(arguments.Last());
            Close();
        }
        else if (arguments.Contains("--smoke-test")) await RunSmokeTest(arguments.Last());
        else if (arguments.Contains("--hotkey-test")) await RunHotkeyTest(arguments.Last());
        else if (arguments.Contains("--audio-tray-test")) await RunAudioTrayTest(arguments.Last());
        else if (arguments.Contains("--language-test")) await RunLanguageTest(arguments.Last());
        else if (arguments.Contains("--resource-test")) await RunResourceTest(arguments.Last());
        else if (arguments.Contains("--instance-test")) await RunInstanceTest(arguments.Last());
    }
    private void SavePreview(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private async Task RunSmokeTest(string folder)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            Directory.CreateDirectory(folder);
            preferences.Folder = folder;
            await Task.Delay(500);
            SavePreview(Path.Combine(folder, "actual-ui.png"));
            var results = new List<object>();
            // Exercise the same button/state path twice, with audio enabled and disabled.
            foreach (bool withAudio in new[] { true, false })
            {
                SystemAudio.IsChecked = withAudio; Microphone.IsChecked = false;
                RecordClick(this, new RoutedEventArgs());
                var completion = completed!.Task;
                for (int i = 0; i < 100 && state == CaptureState.Starting; i++) await Task.Delay(100);
                if (state != CaptureState.Recording) throw new Exception(lastError ?? "Recorder did not enter Recording state.");
                if (withAudio) SavePreview(Path.Combine(folder, "recording-ui.png"));
                // A known local tone validates loopback capture without touching microphone input.
                System.Media.SoundPlayer? player = null;
                string tone = Path.Combine(folder, "tone.wav");
                if (withAudio && File.Exists(tone)) { player = new System.Media.SoundPlayer(tone); player.PlayLooping(); }
                await Task.Delay(4000);
                player?.Stop(); player?.Dispose();
                RecordClick(this, new RoutedEventArgs());
                bool success = await completion.WaitAsync(TimeSpan.FromSeconds(30));
                results.Add(new { withAudio, success, path = activePath, error = lastError });
                if (!success) throw new Exception(lastError);
            }
            // A bad save location must return the UI to idle with a useful error.
            preferences.Folder = Path.Combine(folder, "tone.wav", "invalid-folder");
            RecordClick(this, new RoutedEventArgs());
            bool recovered = state == CaptureState.Idle && RecordButton.IsEnabled && lastError != null;
            results.Add(new { invalidFolderRecovered = recovered });
            // Closing a live window must finalize the MP4 before the window disappears.
            preferences.Folder = folder;
            RecordClick(this, new RoutedEventArgs());
            var closingCompletion = completed!.Task;
            for (int i = 0; i < 100 && state == CaptureState.Starting; i++) await Task.Delay(100);
            if (state != CaptureState.Recording) throw new Exception(lastError ?? "Close test failed to start.");
            await Task.Delay(2000);
            Close();
            bool closedAndSaved = await closingCompletion.WaitAsync(TimeSpan.FromSeconds(30));
            results.Add(new { closeWhileRecordingSaved = closedAndSaved && !IsVisible, path = activePath });
            File.WriteAllText(Path.Combine(folder, "smoke-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = recovered && closedAndSaved && !IsVisible ? 0 : 1;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(folder, "smoke-error.txt"), e.ToString()); Environment.ExitCode = 1; }
        finally { if (state == CaptureState.Idle) { Close(); Application.Current.Shutdown(Environment.ExitCode); } else { recorder?.Stop(); } }
    }
}
