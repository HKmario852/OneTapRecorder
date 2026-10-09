using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
namespace OneTapRecorder;
internal static class Localizer
{
    public static bool English { get; set; }
    private static readonly Dictionary<string, string> EnglishText = new()
    {
        ["一鍵錄影"] = "OneTap Recorder",
        ["上次設定未能讀取，已使用預設設定。"] = "Could not load settings. Using defaults.",
        ["螢幕"] = "Display",
        [" · 主螢幕"] = " · Primary",
        ["▣  螢幕"] = "▣  Display",
        ["螢幕與聲音"] = "Screen + audio",
        ["只錄聲音"] = "Audio only",
        ["準備好就開始"] = "Ready when you are",
        ["●  準備就緒"] = "●  Ready",
        ["螢幕與電腦聲音，一次錄低。"] = "Capture your screen and computer audio.",
        ["只錄電腦聲音或旁白，唔擷取畫面。"] = "Record computer audio or your voice. No video.",
        ["電腦聲音"] = "Computer audio",
        ["录音"] = "Audio recording",
        ["錄音"] = "audio recording",
        ["錄影"] = "screen recording",
        ["錄低預設喇叭／耳機嘅聲音"] = "Capture your default speakers or headphones",
        ["咪高峰"] = "Microphone",
        ["需要旁白時先開啟"] = "Turn on to add your voice",
        ["電腦聲音開啟"] = "Computer audio on",
        ["電腦聲音關閉"] = "Computer audio off",
        ["咪高峰開啟"] = "Mic on",
        ["咪高峰關閉"] = "Mic off",
        ["開始"] = "Start ",
        ["●   開始"] = "●   Start ",
        ["開始錄影"] = "Start screen recording",
        ["●   開始錄影"] = "●   Start recording",
        ["■   停止並儲存"] = "■   Stop and save",
        ["停止並儲存"] = "Stop and save",
        ["正在儲存"] = "Saving ",
        ["正在儲存…"] = "Saving…",
        ["●  儲存中"] = "●  Saving",
        ["已儲存"] = " saved",
        ["已儲存 "] = "Saved: ",
        ["●  已儲存"] = "●  Saved",
        ["●  錄音中"] = "●  Recording audio",
        ["●  錄影中"] = "●  Recording screen",
        ["正在錄低聲音"] = "Recording your audio",
        ["正在錄低你嘅畫面"] = "Recording your screen",
        ["可關閉視窗，喺背景繼續錄音。"] = "You can close this window and keep recording.",
        ["完成後再按一次，即可儲存。"] = "Press again when you are done.",
        ["●  正在準備"] = "●  Starting",
        ["準備錄影中"] = "Getting ready",
        ["正在啟動…"] = "Starting…",
        ["正在連接螢幕同聲音裝置…"] = "Connecting to your display and audio devices…",
        ["檔案完成前，請保留程式開啟。"] = "Finishing your file. Please keep the app running.",
        ["未能完成"] = "Could not finish ",
        ["。\n\n"] = ".\n\n",
        ["檔案未能完成。"] = "The recording could not be completed.",
        ["●  需要留意"] = "●  Check settings",
        ["請檢查儲存位置、螢幕及聲音裝置，再試一次。"] = "Check the save folder, display and audio devices, then retry.",
        ["\n\n請檢查可用磁碟空間、Windows 咪高峰權限及音訊裝置。"] = "\n\nCheck free disk space, Windows microphone permissions and audio devices.",
        ["今次可以繼續錄影，但未能記住設定。"] = "Recording is available, but settings could not be saved.",
        ["搵唔到可錄影嘅螢幕。"] = "No display is available to record.",
        ["搵唔到聲音輸出裝置。請連接喇叭／耳機，或關閉電腦聲音。"] = "No audio output device found. Connect speakers/headphones, or turn computer audio off.",
        ["搵唔到咪高峰。請連接咪高峰，或關閉咪高峰開關。"] = "No microphone found. Connect one, or turn the microphone off.",
        ["只錄聲音時，請至少開啟電腦聲音或咪高峰。"] = "For audio-only recording, turn on computer audio or the microphone.",
        ["已到 WAV 單檔大小上限，請重新開始另一段錄音。"] = "The WAV file size limit was reached. Start a new recording.",
        ["MP4 · 最高 1080p · 30 fps · 只儲存喺本機"] = "MP4 · Up to 1080p · 30 fps · Saved locally",
        ["WAV · 48 kHz · 立體聲 · 只儲存喺本機"] = "WAV · 48 kHz · Stereo · Saved locally",
        ["開啟錄影資料夾"] = "Open recordings folder",
        ["更改位置"] = "Change folder",
        ["未能開啟資料夾"] = "Could not open folder",
        ["選擇錄影儲存位置"] = "Choose where to save recordings",
        ["儲存位置："] = "Save folder: ",
        ["開啟視窗"] = "Open window",
        ["開啟儲存資料夾"] = "Open recordings folder",
        ["結束程式"] = "Exit",
        ["一鍵錄影 · 準備就緒"] = "OneTap Recorder · Ready",
        ["一鍵錄影 · 正在啟動"] = "OneTap Recorder · Starting",
        ["一鍵錄影 · 正在儲存"] = "OneTap Recorder · Saving",
        ["一鍵錄影 · "] = "OneTap Recorder · ",
        [" · 準備就緒"] = " · Ready",
        ["錄製中"] = "Recording",
        ["一鍵錄影繼續喺背景運行"] = "OneTap Recorder is running in the background",
        ["雙擊圖示可開啟視窗；右鍵 → 結束程式先會完全退出。"] = "Double-click the tray icon to open. Right-click → Exit to quit.",
        ["  開始／停止"] = "  Start / stop",
        ["  未能啟用，請重新設定"] = "  Unavailable. Choose another shortcut.",
        ["Ctrl + Shift + F9  開始／停止"] = "Ctrl + Shift + F9  Start / stop",
        ["設定快捷鍵"] = "Set shortcut",
        ["等候按鍵…"] = "Press a key…",
        ["同一快捷鍵會開始／停止錄影。Esc 取消。"] = "Use the same shortcut to start/stop. Esc cancels.",
        ["按你想用嘅按鍵，例如 F8 或 Ctrl + Shift + R"] = "Press a shortcut, e.g. F8 or Ctrl + Shift + R",
        ["請揀另一個按鍵"] = "Choose another key",
        ["請用英文字母、數字或功能鍵，可加 Ctrl／Alt／Shift。F12 及 Windows 鍵不適用。"] = "Use letters, numbers or function keys, with optional Ctrl/Alt/Shift. F12 and the Windows key are unavailable.",
        ["此單鍵會喺其他程式生效，包括打字時；建議用功能鍵或組合鍵。"] = "This key also works in other apps while typing. A function key or modifier combination is recommended.",
        ["按「儲存」套用，或繼續按另一組按鍵。"] = "Press Save to apply, or press a different shortcut.",
        ["呢個按鍵不適用，請揀另一個。"] = "This key is unavailable. Choose another one.",
        ["呢個快捷鍵已被佔用或系統唔支援，請揀另一個。原有設定已保留。"] = "This shortcut is in use or unavailable. Your previous shortcut is unchanged.",
        ["快捷鍵已儲存："] = "Shortcut saved: ",
        ["快捷鍵今次可用，但未能儲存設定，下次開啟要重新設定。"] = "Shortcut works for now, but could not be saved for next time.",
        ["重設預設"] = "Reset",
        ["取消"] = "Cancel",
        ["儲存"] = "Save",
        ["選擇錄影螢幕"] = "Choose display to record",
        ["錄製電腦聲音"] = "Record computer audio",
        ["錄製咪高峰"] = "Record microphone",
        ["開始或停止錄影"] = "Start or stop recording",
        ["按下新的錄影快捷鍵"] = "Press a new recording shortcut",
    };
    internal static readonly Dictionary<string, string> ResourceKeys = new()
    {
        ["UI000"] = "一鍵錄影",
        ["UI004"] = "▣  螢幕",
        ["UI005"] = "螢幕與聲音",
        ["UI006"] = "只錄聲音",
        ["UI007"] = "準備好就開始",
        ["UI008"] = "●  準備就緒",
        ["UI009"] = "螢幕與電腦聲音，一次錄低。",
        ["UI011"] = "電腦聲音",
        ["UI015"] = "錄低預設喇叭／耳機嘅聲音",
        ["UI016"] = "咪高峰",
        ["UI017"] = "需要旁白時先開啟",
        ["UI025"] = "●   開始錄影",
        ["UI057"] = "MP4 · 最高 1080p · 30 fps · 只儲存喺本機",
        ["UI059"] = "開啟錄影資料夾",
        ["UI060"] = "更改位置",
        ["UI077"] = "Ctrl + Shift + F9  開始／停止",
        ["UI078"] = "設定快捷鍵",
        ["UI079"] = "等候按鍵…",
        ["UI080"] = "同一快捷鍵會開始／停止錄影。Esc 取消。",
        ["UI081"] = "按你想用嘅按鍵，例如 F8 或 Ctrl + Shift + R",
        ["UI090"] = "重設預設",
        ["UI091"] = "取消",
        ["UI092"] = "儲存",
        ["UI093"] = "選擇錄影螢幕",
        ["UI094"] = "錄製電腦聲音",
        ["UI095"] = "錄製咪高峰",
        ["UI096"] = "開始或停止錄影",
        ["UI097"] = "按下新的錄影快捷鍵",
    };
    public static string Text(string text) => English && EnglishText.TryGetValue(text, out string? translated) ? translated : text;
}
public partial class MainWindow
{
    private static string L(string text) => Localizer.Text(text);
    private void LanguageClick(object sender, RoutedEventArgs e)
    {
        if (state != CaptureState.Idle) return;
        if (editingHotkey) CloseHotkeyEditor();
        preferences.Language = Localizer.English ? "zh-Hant" : "en";
        ApplyLanguage();
        SavePreferences();
    }
    private void ApplyLanguage()
    {
        Localizer.English = preferences.Language == "en";
        foreach (var pair in Localizer.ResourceKeys) Resources[pair.Key] = L(pair.Value);
        LanguageButton.Content = Localizer.English ? "繁" : "EN";
        LanguageButton.ToolTip = Localizer.English ? "切換至繁體中文" : "Switch to English";
        Title = L("一鍵錄影");
        Heading.Text = L("準備好就開始");
        Badge.Text = L("●  準備就緒");
        ApplyMode();
        UpdateHotkeyLabel();
        if (trayMenu != null)
        {
            trayMenu.Items[0].Text = L("開啟視窗");
            trayMenu.Items[2].Text = L("開啟儲存資料夾");
            trayMenu.Items[4].Text = L("結束程式");
        }
        UpdateTray();
    }
    private void UpdateClockDisplay()
    {
        TimerText.Text = $"{(int)elapsed.Elapsed.TotalHours:00}:{elapsed.Elapsed.Minutes:00}:{elapsed.Elapsed.Seconds:00}";
        if (state == CaptureState.Recording) Title = $"● {TimerText.Text} · {L("一鍵錄影")}";
    }
    private void UpdateRecordingTimer()
    {
        if (state == CaptureState.Recording && IsVisible && WindowState != WindowState.Minimized)
        {
            UpdateClockDisplay();
            timer.Start();
        }
        else timer.Stop();
    }
}
