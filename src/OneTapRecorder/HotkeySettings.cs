using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OneTapRecorder;

public partial class MainWindow
{
    private int activeHotkeyId = 1;
    private bool editingHotkey;
    private uint pendingModifiers;
    private uint pendingKey;
    private double heightBeforeEditor;
    private bool hotkeyHeld;
    private readonly DispatcherTimer hotkeyReleaseTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    private void InitializeHotkeyReleaseTracking()
    {
        hotkeyReleaseTimer.Tick += (_, _) =>
        {
            if ((GetAsyncKeyState((int)preferences.HotkeyKey) & 0x8000) == 0)
            {
                hotkeyHeld = false;
                hotkeyReleaseTimer.Stop();
            }
        };
        Closed += (_, _) => hotkeyReleaseTimer.Stop();
    }

    private static bool IsValidHotkey(uint modifiers, uint key)
    {
        if ((modifiers & ~7u) != 0 || key == 0x7B) return false; // Windows reserves F12.
        // Function keys, letters, digits and numpad keys; modifiers alone aren't a shortcut.
        return key is >= 0x70 and <= 0x87 or >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or >= 0x60 and <= 0x6F;
    }
    private static string HotkeyName(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        string name = KeyInterop.KeyFromVirtualKey((int)key).ToString();
        if (key is >= 0x30 and <= 0x39) name = ((char)key).ToString();
        parts.Add(name);
        return string.Join(" + ", parts);
    }
    private void UpdateHotkeyLabel()
    {
        string name = HotkeyName(preferences.HotkeyModifiers, preferences.HotkeyKey);
        ShortcutText.Text = name + (hotkeyRegistered || DiagnosticMode || handle == IntPtr.Zero ? L("  開始／停止") : L("  未能啟用，請重新設定"));
    }
    private void EditHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (state != CaptureState.Idle || editingHotkey) return;
        editingHotkey = true;
        LanguageButton.IsEnabled = false;
        UpdateTray();
        pendingKey = 0;
        pendingModifiers = 0;
        CaptureHotkeyButton.Content = L("等候按鍵…");
        HotkeyHint.Text = L("同一快捷鍵會開始／停止錄影。Esc 取消。");
        SaveHotkeyButton.IsEnabled = false;
        HotkeyEditor.Visibility = Visibility.Visible;
        RecordButton.IsEnabled = false;
        EditHotkeyButton.IsEnabled = false;
        heightBeforeEditor = Height;
        Height = Math.Max(MinHeight, Math.Min(Height + 185, SystemParameters.WorkArea.Height));
        CaptureHotkeyButton.Focus();
        HotkeyEditor.BringIntoView();
    }
    private void SelectHotkey(uint modifiers, uint key)
    {
        if (!IsValidHotkey(modifiers, key))
        {
            pendingKey = 0;
            SaveHotkeyButton.IsEnabled = false;
            CaptureHotkeyButton.Content = L("請揀另一個按鍵");
            HotkeyHint.Text = L("請用英文字母、數字或功能鍵，可加 Ctrl／Alt／Shift。F12 及 Windows 鍵不適用。");
            return;
        }
        pendingModifiers = modifiers;
        pendingKey = key;
        CaptureHotkeyButton.Content = HotkeyName(modifiers, key);
        SaveHotkeyButton.IsEnabled = true;
        HotkeyHint.Text = modifiers == 0 && key < 0x70
            ? L("此單鍵會喺其他程式生效，包括打字時；建議用功能鍵或組合鍵。")
            : L("按「儲存」套用，或繼續按另一組按鍵。");
    }
    private void CaptureHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (!editingHotkey) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { e.Handled = true; CloseHotkeyEditor(); return; }
        if (key == Key.Tab) return;
        // Let keyboard users activate the editor's buttons without capturing navigation keys.
        if ((key == Key.Enter || key == Key.Space) && e.OriginalSource != CaptureHotkeyButton) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        uint modifiers = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= 2;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= 1;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= 4;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0 || key is Key.LWin or Key.RWin) modifiers |= 8;
        SelectHotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
    private bool TryApplyHotkey(uint modifiers, uint key, out string error)
    {
        error = "";
        if (!IsValidHotkey(modifiers, key)) { error = L("呢個按鍵不適用，請揀另一個。"); return false; }
        if (hotkeyRegistered && modifiers == preferences.HotkeyModifiers && key == preferences.HotkeyKey) return true;
        int newId = activeHotkeyId == 1 ? 2 : 1;
        // Register the replacement first: a conflict must never remove the working shortcut.
        if (!RegisterHotKey(handle, newId, 0x4000 | modifiers, key))
        {
            error = L("呢個快捷鍵已被佔用或系統唔支援，請揀另一個。原有設定已保留。");
            return false;
        }
        if (hotkeyRegistered) UnregisterHotKey(handle, activeHotkeyId);
        activeHotkeyId = newId;
        hotkeyHeld = false;
        hotkeyReleaseTimer.Stop();
        hotkeyRegistered = true;
        preferences.HotkeyModifiers = modifiers;
        preferences.HotkeyKey = key;
        UpdateHotkeyLabel();
        return true;
    }
    private void SaveHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (!editingHotkey || pendingKey == 0) return;
        if (!TryApplyHotkey(pendingModifiers, pendingKey, out string error)) { HotkeyHint.Text = error; return; }
        bool saved = SavePreferences();
        CloseHotkeyEditor();
        StatusText.Text = saved ? L("快捷鍵已儲存：") + HotkeyName(preferences.HotkeyModifiers, preferences.HotkeyKey)
            : L("快捷鍵今次可用，但未能儲存設定，下次開啟要重新設定。");
    }
    private void CancelHotkeyClick(object sender, RoutedEventArgs e) => CloseHotkeyEditor();
    private void ResetHotkeyClick(object sender, RoutedEventArgs e) => SelectHotkey(6, 0x78);
    private void CloseHotkeyEditor()
    {
        if (!editingHotkey) return;
        editingHotkey = false;
        LanguageButton.IsEnabled = true;
        UpdateTray();
        HotkeyEditor.Visibility = Visibility.Collapsed;
        Height = heightBeforeEditor;
        EditHotkeyButton.IsEnabled = true;
        RefreshMonitors();
        EditHotkeyButton.Focus();
    }
}
