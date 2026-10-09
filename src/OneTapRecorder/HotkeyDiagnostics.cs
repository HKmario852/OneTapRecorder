using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace OneTapRecorder;

public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void TapDiagnosticKey() { keybd_event(0x86, 0, 0, UIntPtr.Zero); keybd_event(0x86, 0, 2, UIntPtr.Zero); }
    private async Task AwaitRecording()
    {
        for (int i = 0; i < 150 && state != CaptureState.Recording; i++) await Task.Delay(100);
        Require(state == CaptureState.Recording, lastError ?? "Global hotkey did not start recording.");
    }
    private async Task RunHotkeyTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new Dictionary<string, object>();
        bool reload = arguments.Contains("--verify-reload");
        try
        {
            await Task.Delay(200);
            if (reload)
            {
                Require(preferences.HotkeyModifiers == 0 && preferences.HotkeyKey == 0x86, "Hotkey was not restored in a new process.");
                Require(TryApplyHotkey(preferences.HotkeyModifiers, preferences.HotkeyKey, out _), "Persisted hotkey could not register.");
                results["coldRestartRestoresAndRegistersHotkey"] = true;
            }
            else
            {
                preferences.Folder = folder;
                SystemAudio.IsChecked = false;
                Microphone.IsChecked = false;
                Require(TryApplyHotkey(0, 0x86, out _), "F23 needed by test is unavailable.");
                EditHotkeyClick(this, new RoutedEventArgs());
                var capture = new KeyEventArgs(Keyboard.PrimaryDevice, hwndSource!, Environment.TickCount, Key.F8) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                CaptureHotkeyButton.RaiseEvent(capture);
                Require(pendingKey == 0x77 && pendingModifiers == 0 && SaveHotkeyButton.IsEnabled, "F8 capture failed.");
                SavePreview(Path.Combine(folder, "hotkey-editor.png"));
                CancelHotkeyClick(this, new RoutedEventArgs());
                Require(preferences.HotkeyKey == 0x86 && hotkeyRegistered, "Cancel changed the active hotkey.");
                results["keyCaptureAndCancel"] = true;
                EditHotkeyClick(this, new RoutedEventArgs());
                TapDiagnosticKey();
                await Task.Delay(250);
                Require(state == CaptureState.Idle && pendingKey == 0x86, "Editing triggered recording.");
                CancelHotkeyClick(this, new RoutedEventArgs());
                results["activeHotkeyDoesNotRecordWhileEditing"] = true;
                Require(TryApplyHotkey(0, 0x77, out _), "Single F8 registration failed.");
                Require(TryApplyHotkey(6, 0x52, out _), "Ctrl+Shift+R registration failed.");
                Require(TryApplyHotkey(0, 0x86, out _), "F23 re-registration failed.");
                results["singleKeyAndModifierCombination"] = true;
                using (var otherWindow = new HwndSource(new HwndSourceParameters("Hotkey conflict test") { Width = 1, Height = 1, WindowStyle = 0 }))
                {
                    Require(RegisterHotKey(otherWindow.Handle, 300, 0x4000, 0x87), "Cannot reserve conflict test key.");
                    try
                    {
                        Require(!TryApplyHotkey(0, 0x87, out var conflict) && conflict.Length > 0, "Conflicting hotkey was accepted.");
                        Require(preferences.HotkeyKey == 0x86 && hotkeyRegistered, "Conflict removed previous shortcut.");
                    }
                    finally { UnregisterHotKey(otherWindow.Handle, 300); }
                }
                Require(!TryApplyHotkey(0, 0x7B, out _) && !TryApplyHotkey(8, 0x52, out _), "Reserved key accepted.");
                results["conflictPreservesOldHotkeyAndReservedKeysRejected"] = true;
                EditHotkeyClick(this, new RoutedEventArgs());
                SelectHotkey(0, 0x86);
                SaveHotkeyClick(this, new RoutedEventArgs());
                var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(configPath))!;
                Require(saved.HotkeyKey == 0x86 && saved.HotkeyModifiers == 0 && !editingHotkey, "Save did not persist selected hotkey.");
                var legacy = JsonSerializer.Deserialize<Preferences>("{\"SystemAudio\":true}")!;
                Require(legacy.HotkeyKey == 0x78 && legacy.HotkeyModifiers == 6, "Legacy config migration lost defaults.");
                results["saveAndLegacySettings"] = true;
                WindowState = WindowState.Minimized;
                await Task.Delay(200);
                keybd_event(0x86, 0, 0, UIntPtr.Zero);
                try
                {
                    await AwaitRecording();
                    for (int i = 0; i < 4; i++) { keybd_event(0x86, 0, 0, UIntPtr.Zero); await Task.Delay(100); }
                    Require(state == CaptureState.Recording, "Held key toggled repeatedly.");
                }
                finally { keybd_event(0x86, 0, 2, UIntPtr.Zero); }
                await Task.Delay(1500);
                var completion = completed!.Task;
                TapDiagnosticKey();
                Require(await completion.WaitAsync(TimeSpan.FromSeconds(30)), "Global key did not stop/save.");
                results["globalKeyStartsStopsWhileMinimized"] = true;
                results["holdingKeyDoesNotRepeat"] = true;
                results["recording"] = activePath!;
                WindowState = WindowState.Normal;
                await Task.Delay(200);
                SavePreview(Path.Combine(folder, "hotkey-saved.png"));
            }
            File.WriteAllText(Path.Combine(folder, reload ? "reload-results.json" : "hotkey-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(folder, "hotkey-error.txt"), e.ToString());
            Environment.ExitCode = 1;
        }
        finally
        {
            if (state == CaptureState.Idle) Close();
            else { closeAfterSave = true; if (state == CaptureState.Recording) StopRecording(); }
        }
    }
}
