using System;
using System.Globalization;
using System.Resources;
using System.Windows;
namespace OneTapRecorder;
internal static class Localizer
{
    // Strings.resx holds English (the neutral language); Strings.zh-Hant.resx holds Traditional Chinese.
    private static readonly ResourceManager Strings = new("OneTapRecorder.Strings", typeof(Localizer).Assembly);
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-Hant");
    public static bool English { get; set; }
    // Strings that MainWindow.xaml binds with DynamicResource; ApplyLanguage refreshes them.
    internal static readonly string[] XamlKeys =
    [
        "AppTitle", "DisplayLabel", "ModeScreen", "ModeAudio", "ReadyHeading", "ReadyBadge", "ScreenSubtitle",
        "ComputerAudio", "ComputerAudioHint", "Microphone", "MicrophoneHint", "StartScreenButton", "ScreenFormat",
        "OpenRecordingsFolder", "ChangeFolder", "ShortcutDefault", "SetShortcut", "PressAKey", "ShortcutEditorHint",
        "ShortcutPrompt", "Reset", "Cancel", "Save", "A11yDisplay", "A11yComputerAudio", "A11yMicrophone",
        "A11yRecordButton", "A11yShortcut",
    ];
    public static string Text(string key) => Strings.GetString(key, English ? CultureInfo.InvariantCulture : Chinese) ?? key;
    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Text(key), args);
}
public partial class MainWindow
{
    private static string L(string key) => Localizer.Text(key);
    private static string L(string key, params object[] args) => Localizer.Format(key, args);
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
        foreach (string key in Localizer.XamlKeys) Resources[key] = L(key);
        LanguageButton.Content = Localizer.English ? "繁" : "EN";
        LanguageButton.ToolTip = Localizer.English ? "切換至繁體中文" : "Switch to English";
        Title = L("AppTitle");
        Heading.Text = L("ReadyHeading");
        Badge.Text = L("ReadyBadge");
        ApplyMode();
        UpdateHotkeyLabel();
        if (trayMenu != null)
        {
            trayMenu.Items[0].Text = L("TrayOpenWindow");
            trayMenu.Items[2].Text = L("TrayOpenFolder");
            trayMenu.Items[4].Text = L("TrayExit");
        }
        UpdateTray();
    }
    private void UpdateClockDisplay()
    {
        TimerText.Text = $"{(int)elapsed.Elapsed.TotalHours:00}:{elapsed.Elapsed.Minutes:00}:{elapsed.Elapsed.Seconds:00}";
        if (state == CaptureState.Recording) Title = $"● {TimerText.Text} · {L("AppTitle")}";
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
