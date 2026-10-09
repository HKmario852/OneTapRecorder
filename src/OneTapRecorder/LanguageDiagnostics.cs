using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
namespace OneTapRecorder;
public partial class MainWindow
{
    private async Task RunLanguageTest(string folder)
    {
        Directory.CreateDirectory(folder);
        try
        {
            if (arguments.Contains("--verify-reload"))
            {
                Require(preferences.Language == "en" && Localizer.English, "Language did not persist across restart.");
            }
            else
            {
                preferences.Language = "zh-Hant";
                ApplyLanguage();
                Require(Title == "一鍵錄影", "Chinese UI missing.");
                LanguageClick(this, new RoutedEventArgs());
                Require(Title == "OneTap Recorder" && Localizer.English, "English switch failed.");
                Require((string)VideoMode.Content == "Screen + audio" && (string)AudioMode.Content == "Audio only", "English mode labels missing.");
                Require(trayMenu!.Items[0].Text == "Open window" && trayMenu.Items[4].Text == "Exit", "Tray menu was not translated.");
                EditHotkeyClick(this, new RoutedEventArgs());
                SelectHotkey(0, 0x77);
                Require((string)SaveHotkeyButton.Content == "Save" && HotkeyHint.Text.StartsWith("Press Save"), "Hotkey editor was not translated.");
                await Task.Delay(150);
                SavePreview(Path.Combine(folder,"English-shortcut.png"));
                CancelHotkeyClick(this, new RoutedEventArgs());
                AudioMode.IsChecked = true;
                SystemAudio.IsChecked = false;
                Microphone.IsChecked = false;
                BeginRecording();
                Require(state == CaptureState.Idle && lastError!.StartsWith("For audio-only"), "Validation message was not translated.");
                LanguageClick(this, new RoutedEventArgs());
                Require(Title == "一鍵錄影" && trayMenu.Items[4].Text == "結束程式", "Chinese switch-back failed.");
                LanguageClick(this, new RoutedEventArgs());
                var saved=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(configPath))!;
                Require(saved.Language == "en", "Language preference was not saved.");
            }
            File.WriteAllText(Path.Combine(folder,arguments.Contains("--verify-reload") ? "language-reload.json" : "language-results.json"), "{\"passed\":true}");
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"language-error.txt"), ex.ToString()); Environment.ExitCode=1; }
        finally { RequestExit(); }
    }
}
