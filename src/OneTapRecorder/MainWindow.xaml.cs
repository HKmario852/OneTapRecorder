using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace OneTapRecorder;

public partial class MainWindow : Window
{
    private enum CaptureState { Idle, Starting, Recording, Stopping }
    private CaptureState state;
    private IVideoSession? recorder;
    private readonly Stopwatch elapsed = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneTapRecorder", "settings.json");
    private Preferences preferences = new();
    private bool closeAfterSave;
    private IntPtr handle;
    private bool hotkeyRegistered;
    private HwndSource? hwndSource;
    private string? activePath;
    private string? lastError;
    private TaskCompletionSource<bool>? completed;
    private readonly string[] arguments = Environment.GetCommandLineArgs();
#if !DIAGNOSTICS
    // The --*-test switches exist only in the Diagnostics build (see Diagnostics/DiagnosticsEntry.cs).
    private static bool DiagnosticMode => false;
    private static bool PersistSettings => true;
    internal static bool IsDiagnosticRun(string[] args) => false;
#endif
    // Implemented in Diagnostics/*.cs; the compiler removes these calls from other builds.
    partial void UseDiagnosticSettingsPath(ref string path);
    partial void CountUiTick();
    partial void RunDiagnostics();
    private record MonitorChoice(string DeviceName, string Label, int Width, int Height);
    private sealed class Preferences
    {
        public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "OneTap Recordings");
        public string? Monitor { get; set; }
        public bool SystemAudio { get; set; } = true;
        public bool Microphone { get; set; }
        public uint HotkeyModifiers { get; set; } = 6;
        public uint HotkeyKey { get; set; } = 0x78;
        public bool AudioOnly { get; set; }
        public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-Hant" : "en";
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();
        UseDiagnosticSettingsPath(ref configPath);
        if (PersistSettings)
        {
            try { if (File.Exists(configPath)) preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(configPath)) ?? new(); }
            catch { StatusText.Text = L("SettingsLoadFailed"); }
        }
        if (string.IsNullOrWhiteSpace(preferences.Folder) || !Path.IsPathFullyQualified(preferences.Folder)) preferences.Folder = new Preferences().Folder;
        SystemAudio.IsChecked = preferences.SystemAudio;
        Microphone.IsChecked = preferences.Microphone;
        AudioMode.IsChecked = preferences.AudioOnly;
        VideoMode.IsChecked = !preferences.AudioOnly;
        ApplyMode();
        if (!IsValidHotkey(preferences.HotkeyModifiers, preferences.HotkeyKey)) { preferences.HotkeyModifiers = 6; preferences.HotkeyKey = 0x78; }
        PreviewKeyDown += CaptureHotkeyKeyDown;
        InitializeHotkeyReleaseTracking();
        ApplyLanguage();
        RefreshMonitors();
        FolderButton.ToolTip = preferences.Folder;
        timer.Tick += (_, _) => { CountUiTick(); UpdateClockDisplay(); };
        IsVisibleChanged += (_, _) => UpdateRecordingTimer();
        StateChanged += (_, _) => UpdateRecordingTimer();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            int dark = 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            hwndSource = HwndSource.FromHwnd(handle);
            hwndSource?.AddHook(HandleMessage);
            if (!DiagnosticMode || arguments.Contains("--audio-tray-test") || arguments.Contains("--instance-test") || arguments.Contains("--language-test")) InitializeTray();
            if (!DiagnosticMode)
            {
                hotkeyRegistered = RegisterHotKey(handle, activeHotkeyId, 0x4000 | preferences.HotkeyModifiers, preferences.HotkeyKey);
                UpdateHotkeyLabel();
            }
        };
        Closing += OnClosing;
        Closed += (_, _) => { timer.Stop(); if (hotkeyRegistered) UnregisterHotKey(handle, activeHotkeyId); hwndSource?.RemoveHook(HandleMessage); recorder?.Dispose(); };
        Loaded += (_, _) => RunDiagnostics();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
    }

    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (state == CaptureState.Idle) RefreshMonitors(); });
    private void RefreshMonitors()
    {
        string? selected = (MonitorBox.SelectedItem as MonitorChoice)?.DeviceName ?? preferences.Monitor;
        var monitors = Screen.AllScreens.OrderByDescending(s => s.Primary).Select((s, i) => new MonitorChoice(s.DeviceName,
            $"{L("DisplayNumber", i + 1)}{(s.Primary ? L("PrimaryDisplay") : "")}  {s.Bounds.Width} × {s.Bounds.Height}", s.Bounds.Width, s.Bounds.Height)).ToList();
        MonitorBox.ItemsSource = monitors;
        MonitorBox.SelectedItem = monitors.FirstOrDefault(s => s.DeviceName == selected) ?? monitors.FirstOrDefault();
        RecordButton.IsEnabled = !editingHotkey && (AudioOnly || monitors.Count > 0);
    }

    private IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x312 && hotkeyRegistered && wParam.ToInt32() == activeHotkeyId)
        {
            handled = true;
            if (hotkeyHeld) return IntPtr.Zero;
            hotkeyHeld = true;
            hotkeyReleaseTimer.Start();
            if (editingHotkey) SelectHotkey(preferences.HotkeyModifiers, preferences.HotkeyKey);
            else ToggleRecording();
        }
        return IntPtr.Zero;
    }
    private void RecordClick(object sender, RoutedEventArgs e) => ToggleRecording();
    private void ToggleRecording()
    {
        if (editingHotkey || exitRequested) return;
        if (state == CaptureState.Idle) BeginRecording();
        else if (state == CaptureState.Recording) StopRecording();
    }
    private void BeginRecording()
    {
        if (state != CaptureState.Idle) return;
        completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lastError = null;
        try
        {
            RefreshMonitors();
            var monitor = MonitorBox.SelectedItem as MonitorChoice;
            if (!AudioOnly && monitor == null) throw new InvalidOperationException(L("NoDisplay"));
            Directory.CreateDirectory(preferences.Folder);
            // Check access before capture starts; never overwrite an existing recording.
            activePath = Path.Combine(preferences.Folder, $"Recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}" + (AudioOnly ? ".wav" : ".mp4"));
            using (new FileStream(activePath + ".probe", FileMode.CreateNew, FileAccess.Write)) { }
            File.Delete(activePath + ".probe");
            if (AudioOnly)
            {
                state = CaptureState.Starting;
                SetBusy(true);
                RecordButton.IsEnabled = false;
                TimerText.Text = "00:00:00";
                SavePreferences();
                StartAudioOnly();
                return;
            }
            double scale = Math.Min(1, Math.Min(1920.0 / monitor!.Width, 1080.0 / monitor.Height));
            int width = Math.Max(2, (int)(monitor.Width * scale) / 2 * 2);
            int height = Math.Max(2, (int)(monitor.Height * scale) / 2 * 2);
            state = CaptureState.Starting;
            UpdateTray();
            SetBusy(true);
            Badge.Text = L("StartingBadge");
            Heading.Text = L("StartingHeading");
            RecordButton.Content = L("StartingButton");
            RecordButton.IsEnabled = false;
            TimerText.Text = "00:00:00";
            StatusText.Text = L("StartingStatus");
            SavePreferences();
            var current = VideoSessionFactory.Create(monitor.DeviceName, width, height, SystemAudio.IsChecked == true, Microphone.IsChecked == true);
            recorder = current;
            current.Started += () => Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(recorder, current)) return;
                if (state == CaptureState.Starting)
                {
                    state = CaptureState.Recording;
                    elapsed.Restart(); UpdateRecordingTimer();
                    Badge.Text = L("RecordingScreenBadge"); Heading.Text = L("RecordingScreenHeading");
                    Subtitle.Text = L("RecordingScreenSubtitle");
                    RecordButton.Content = L("StopAndSaveButton"); RecordButton.IsEnabled = true;
                    StatusText.Text = $"{width} × {height} · 30 fps · {(SystemAudio.IsChecked == true ? L("ComputerAudioOn") : L("ComputerAudioOff"))} · {(Microphone.IsChecked == true ? L("MicOn") : L("MicOff"))}";
                    UpdateTray();
                    if (closeAfterSave) StopRecording();
                }
            });
            current.Finished += (error, path) => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(recorder, current)) FinishRecording(error, path ?? activePath); });
            current.Record(activePath);
        }
        catch (Exception e) { FinishRecording(e.Message, activePath); }
    }
    private void StopRecording()
    {
        if (state != CaptureState.Recording) return;
        state = CaptureState.Stopping;
        RecordButton.IsEnabled = false; RecordButton.Content = L("SavingButton");
        Heading.Text = L(AudioOnly ? "SavingAudioHeading" : "SavingScreenHeading"); Badge.Text = L("SavingBadge");
        UpdateTray();
        StatusText.Text = L("SavingStatus");
        timer.Stop(); elapsed.Stop();
        try { if (audioRecorder != null) audioRecorder.RequestStop(); else recorder?.Stop(); } catch (Exception e) { FinishRecording(e.Message, activePath); }
    }
    private void FinishRecording(string? error, string? path)
    {
        timer.Stop(); elapsed.Stop();
        UpdateClockDisplay();
        var oldRecorder = recorder; recorder = null;
        oldRecorder?.Dispose();
        audioRecorder = null;
        state = CaptureState.Idle;
        SetBusy(false); RefreshMonitors();
        ApplyMode();
        Title = L("AppTitle");
        bool saved = error == null && path != null && File.Exists(path) && new FileInfo(path).Length > 0;
        if (saved)
        {
            Badge.Text = L("SavedBadge"); Heading.Text = L(AudioOnly ? "SavedAudioHeading" : "SavedScreenHeading");
            StatusText.Text = L("SavedFile", Path.GetFileName(path!));
        }
        else
        {
            lastError = error ?? L("FileIncomplete");
            Badge.Text = L("FailedBadge"); Heading.Text = L(AudioOnly ? "FailedAudioHeading" : "FailedScreenHeading");
            StatusText.Text = L("FailedStatus");
            if (!DiagnosticMode) { RestoreFromTray(); MessageBox.Show(this, L("FailedMessage", Heading.Text, lastError), L("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        completed?.TrySetResult(saved);
        if (closeAfterSave && saved) Close();
        else { closeAfterSave = false; exitRequested = false; UpdateTray(); }
    }
    private void SetBusy(bool busy) { SettingsPanel.IsEnabled = !busy; ChangeFolderButton.IsEnabled = !busy; EditHotkeyButton.IsEnabled = !busy; LanguageButton.IsEnabled = !busy; }
    private bool SavePreferences()
    {
        preferences.Monitor = (MonitorBox.SelectedItem as MonitorChoice)?.DeviceName;
        preferences.SystemAudio = SystemAudio.IsChecked == true;
        preferences.Microphone = Microphone.IsChecked == true;
        preferences.AudioOnly = AudioOnly;
        if (!PersistSettings) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            string temp = configPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(preferences));
            File.Move(temp, configPath, true);
            return true;
        }
        catch { StatusText.Text = L("SettingsSaveFailed"); return false; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested && (!DiagnosticMode || arguments.Contains("--audio-tray-test") || arguments.Contains("--instance-test")))
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (state != CaptureState.Idle)
        {
            e.Cancel = true; closeAfterSave = true;
            if (state == CaptureState.Recording) StopRecording();
        }
        else SavePreferences();
    }
    private void OpenFolderClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(preferences.Folder); Process.Start(new ProcessStartInfo(preferences.Folder) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L("OpenFolderFailed")); }
    }
    private void ChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("ChooseFolder"), InitialDirectory = preferences.Folder };
        if (dialog.ShowDialog(this) == true) { preferences.Folder = dialog.FolderName; FolderButton.ToolTip = preferences.Folder; SavePreferences(); StatusText.Text = L("SaveFolder", preferences.Folder); }
    }
}
