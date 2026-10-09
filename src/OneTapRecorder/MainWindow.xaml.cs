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
    private bool DiagnosticMode => arguments.Contains("--language-test") || arguments.Contains("--resource-test") || arguments.Contains("--smoke-test") || arguments.Contains("--render-preview") || arguments.Contains("--hotkey-test") || arguments.Contains("--audio-tray-test") || arguments.Contains("--instance-test");
    private bool PersistSettings => arguments.Contains("--language-test") || !DiagnosticMode || arguments.Contains("--hotkey-test") || arguments.Contains("--audio-tray-test");
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
        if (arguments.Contains("--language-test") || arguments.Contains("--hotkey-test") || arguments.Contains("--audio-tray-test")) configPath = Path.Combine(Path.GetFullPath(arguments.Last()), "settings.json");
        if (PersistSettings)
        {
            try { if (File.Exists(configPath)) preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(configPath)) ?? new(); }
            catch { StatusText.Text = L("上次設定未能讀取，已使用預設設定。"); }
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
        timer.Tick += (_, _) => { uiTimerTicks++; UpdateClockDisplay(); };
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
        Loaded += async (_, _) =>
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
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
    }

    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (state == CaptureState.Idle) RefreshMonitors(); });
    private void RefreshMonitors()
    {
        string? selected = (MonitorBox.SelectedItem as MonitorChoice)?.DeviceName ?? preferences.Monitor;
        var monitors = Screen.AllScreens.OrderByDescending(s => s.Primary).Select((s, i) => new MonitorChoice(s.DeviceName,
            $"{L("螢幕")} {i + 1}{(s.Primary ? L(" · 主螢幕") : "")}  {s.Bounds.Width} × {s.Bounds.Height}", s.Bounds.Width, s.Bounds.Height)).ToList();
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
            if (!AudioOnly && monitor == null) throw new InvalidOperationException(L("搵唔到可錄影嘅螢幕。"));
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
            Badge.Text = L("●  正在準備");
            Heading.Text = L("準備錄影中");
            RecordButton.Content = L("正在啟動…");
            RecordButton.IsEnabled = false;
            TimerText.Text = "00:00:00";
            StatusText.Text = L("正在連接螢幕同聲音裝置…");
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
                    Badge.Text = L("●  錄影中"); Heading.Text = L("正在錄低你嘅畫面");
                    Subtitle.Text = L("完成後再按一次，即可儲存。");
                    RecordButton.Content = L("■   停止並儲存"); RecordButton.IsEnabled = true;
                    StatusText.Text = $"{width} × {height} · 30 fps · {(SystemAudio.IsChecked == true ? L("電腦聲音開啟") : L("電腦聲音關閉"))} · {(Microphone.IsChecked == true ? L("咪高峰開啟") : L("咪高峰關閉"))}";
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
        RecordButton.IsEnabled = false; RecordButton.Content = L("正在儲存…");
        Heading.Text = L("正在儲存") + CaptureWord; Badge.Text = L("●  儲存中");
        UpdateTray();
        StatusText.Text = L("檔案完成前，請保留程式開啟。");
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
        Title = L("一鍵錄影");
        bool saved = error == null && path != null && File.Exists(path) && new FileInfo(path).Length > 0;
        if (saved)
        {
            Badge.Text = L("●  已儲存"); Heading.Text = CaptureWord + L("已儲存");
            StatusText.Text = L("已儲存 ") + Path.GetFileName(path);
        }
        else
        {
            lastError = error ?? L("檔案未能完成。");
            Badge.Text = L("●  需要留意"); Heading.Text = L("未能完成") + CaptureWord;
            StatusText.Text = L("請檢查儲存位置、螢幕及聲音裝置，再試一次。");
            if (!DiagnosticMode) { RestoreFromTray(); MessageBox.Show(this, L("未能完成") + CaptureWord + L("。\n\n") + lastError + L("\n\n請檢查可用磁碟空間、Windows 咪高峰權限及音訊裝置。"), L("一鍵錄影"), MessageBoxButton.OK, MessageBoxImage.Warning); }
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
        catch { StatusText.Text = L("今次可以繼續錄影，但未能記住設定。"); return false; }
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
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L("未能開啟資料夾")); }
    }
    private void ChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("選擇錄影儲存位置"), InitialDirectory = preferences.Folder };
        if (dialog.ShowDialog(this) == true) { preferences.Folder = dialog.FolderName; FolderButton.ToolTip = preferences.Folder; SavePreferences(); StatusText.Text = L("儲存位置：") + preferences.Folder; }
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
