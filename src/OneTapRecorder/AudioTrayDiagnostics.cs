using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace OneTapRecorder;

public partial class MainWindow
{
    private async Task RunAudioTrayTest(string folder)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(folder);
        var results = new Dictionary<string, object>();
        try
        {
            preferences.Folder = folder;
            Microphone.IsChecked = false;
            AudioMode.IsChecked = true;
            SystemAudio.IsChecked = false;
            RecordClick(this, new RoutedEventArgs());
            Require(state == CaptureState.Idle && lastError != null, "Audio mode accepted zero sources.");
            results["audioModeRejectsNoSources"] = true;
            SystemAudio.IsChecked = true;
            Require(TryApplyHotkey(0, 0x86, out _), "Could not register diagnostic key.");
            Require(trayIcon?.Visible == true && trayMenu?.Items.Count == 5, "Tray icon/menu was not initialized.");
            SavePreview(Path.Combine(folder, "audio-mode.png"));
            Close(); // Same WPF Closing path as the title-bar cross.
            Require(!IsVisible && !exitRequested && state == CaptureState.Idle, "Cross exited instead of hiding.");
            trayMenu!.Items[0].PerformClick();
            Require(IsVisible, "Tray show command did not reopen window.");
            results["crossHidesAndTrayReopens"] = true;
            Close();
            TapDiagnosticKey();
            await AwaitRecording();
            Require(audioRecorder != null && recorder == null, "Audio mode started a screen recorder.");
            var audioCompletion = completed!.Task;
            using (var tone = new System.Media.SoundPlayer(Path.Combine(folder, "tone.wav")))
            {
                tone.PlayLooping();
                await Task.Delay(2500);
                tone.Stop();
                await Task.Delay(1200);
            }
            Require(!IsVisible && state == CaptureState.Recording, "Background audio capture stopped unexpectedly.");
            trayRecord!.PerformClick();
            Require(await audioCompletion.WaitAsync(TimeSpan.FromSeconds(15)), lastError ?? "Audio recording did not save.");
            results["backgroundHotkeyAudioAndTrayStop"] = true;
            results["audioFile"] = activePath!;
            // Also verify a silent output produces a valid, timed audio-only file.
            trayRecord.PerformClick();
            await AwaitRecording();
            var second = completed!.Task;
            await Task.Delay(1100);
            trayRecord.PerformClick();
            Require(await second.WaitAsync(TimeSpan.FromSeconds(15)), "Second audio capture failed.");
            results["repeatedAudioCapture"] = activePath!;
            RestoreFromTray();
            VideoMode.IsChecked = true;
            SystemAudio.IsChecked = false;
            RecordClick(this, new RoutedEventArgs());
            await AwaitRecording();
            var videoCompletion = completed!.Task;
            Close();
            await Task.Delay(1400);
            Require(state == CaptureState.Recording && !IsVisible, "Cross stopped video recording.");
            trayRecord.PerformClick();
            Require(await videoCompletion.WaitAsync(TimeSpan.FromSeconds(30)), "Background video did not save.");
            results["crossKeepsVideoRecording"] = activePath!;
            RestoreFromTray();
            AudioMode.IsChecked = true;
            SystemAudio.IsChecked = true;
            trayRecord.PerformClick();
            await AwaitRecording();
            var exitCompletion = completed!.Task;
            await Task.Delay(1000);
            trayExit!.PerformClick();
            Require(await exitCompletion.WaitAsync(TimeSpan.FromSeconds(15)), "Tray exit did not save audio.");
            Require(!IsVisible && trayIcon!.Visible == false, "Tray exit did not close/dispose icon.");
            results["trayExitFinalizesAudioAndRemovesIcon"] = activePath!;
            var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(configPath))!;
            Require(saved.AudioOnly, "Audio mode preference was not persisted.");
            results["audioModePreferenceSaved"] = true;
            File.WriteAllText(Path.Combine(folder, "audio-tray-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "audio-tray-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
        finally
        {
            if (state == CaptureState.Idle) { exitRequested = true; Close(); Application.Current.Shutdown(Environment.ExitCode); }
            else { closeAfterSave = true; exitRequested = true; if (state == CaptureState.Recording) StopRecording(); }
        }
    }
}
