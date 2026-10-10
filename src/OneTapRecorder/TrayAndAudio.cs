using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace OneTapRecorder;

public partial class MainWindow
{
    private AudioOnlyRecorder? audioRecorder;
    private bool exitRequested;
    private bool backgroundNoticeShown;
    private Forms.NotifyIcon? trayIcon;
    private Forms.ContextMenuStrip? trayMenu;
    private Forms.ToolStripMenuItem? trayRecord;
    private Forms.ToolStripMenuItem? trayExit;
    private Icon? trayImage;
    private bool AudioOnly => AudioMode.IsChecked == true;
    private string CaptureWord => AudioOnly ? L("錄音") : L("錄影");

    private void InitializeTray()
    {
        trayImage = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
        InitializeStateIcons();
        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Opening += (_, _) => { UpdateClockDisplay(); UpdateTray(); };
        trayMenu.Items.Add(L("開啟視窗"), null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        trayRecord = new Forms.ToolStripMenuItem(L("開始錄影"), null, (_, _) => Dispatcher.Invoke(ToggleRecording));
        trayMenu.Items.Add(trayRecord);
        trayMenu.Items.Add(L("開啟儲存資料夾"), null, (_, _) => Dispatcher.Invoke(() => OpenFolderClick(this, new RoutedEventArgs())));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayExit = new Forms.ToolStripMenuItem(L("結束程式"), null, (_, _) => Dispatcher.Invoke(RequestExit));
        trayMenu.Items.Add(trayExit);
        trayIcon = new Forms.NotifyIcon { Icon = trayImage, Text = L("一鍵錄影 · 準備就緒"), ContextMenuStrip = trayMenu, Visible = true };
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        Closed += (_, _) => { trayIcon.Visible = false; trayIcon.Dispose(); trayMenu.Dispose(); trayImage.Dispose(); recordingIcon?.Dispose(); savingIcon?.Dispose(); };
        UpdateTray();
    }
    private void UpdateTray()
    {
        if (trayIcon == null || trayRecord == null) return;
        UpdateStateIcon();
        trayIcon.Text = state switch
        {
            CaptureState.Recording => L("一鍵錄影 · ") + L("錄製中"),
            CaptureState.Starting => L("一鍵錄影 · 正在啟動"),
            CaptureState.Stopping => L("一鍵錄影 · 正在儲存"),
            _ => L("一鍵錄影 · ") + (AudioOnly ? L("只錄聲音") : L("螢幕與聲音")) + L(" · 準備就緒")
        };
        trayRecord.Text = state == CaptureState.Idle ? L("開始") + CaptureWord : L("停止並儲存");
        trayRecord.Enabled = !editingHotkey && !exitRequested && state is CaptureState.Idle or CaptureState.Recording;
    }
    public void RestoreFromTray()
    {
        Show(); WindowState = WindowState.Normal; Activate();
    }
    private void HideToTray()
    {
        if (editingHotkey) CloseHotkeyEditor();
        SavePreferences();
        Hide(); UpdateTray();
        if (!DiagnosticMode && !backgroundNoticeShown)
        {
            backgroundNoticeShown = true;
            trayIcon?.ShowBalloonTip(3000, L("一鍵錄影繼續喺背景運行"), L("雙擊圖示可開啟視窗；右鍵 → 結束程式先會完全退出。"), Forms.ToolTipIcon.Info);
        }
    }
    private void RequestExit()
    {
        if (exitRequested) return;
        exitRequested = true;
        if (editingHotkey) CloseHotkeyEditor();
        UpdateTray();
        Close();
    }
    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (MonitorPanel == null || StatusText == null || state != CaptureState.Idle) return;
        Heading.Text = L("準備好就開始");
        Badge.Text = L("●  準備就緒");
        TimerText.Text = "00:00:00";
        ApplyMode();
    }
    private void ApplyMode()
    {
        MonitorPanel.Visibility = AudioOnly ? Visibility.Collapsed : Visibility.Visible;
        Subtitle.Text = AudioOnly ? L("只錄電腦聲音或旁白，唔擷取畫面。") : L("螢幕與電腦聲音，一次錄低。");
        RecordButton.Content = L("●   開始") + CaptureWord;
        StatusText.Text = AudioOnly ? L("WAV · 48 kHz · 立體聲 · 只儲存喺本機") : L("MP4 · 最高 1080p · 30 fps · 只儲存喺本機");
        RefreshMonitors(); UpdateTray();
    }
    private void StartAudioOnly()
    {
        var current = new AudioOnlyRecorder();
        audioRecorder = current;
        current.Start(activePath!, SystemAudio.IsChecked == true, Microphone.IsChecked == true);
        state = CaptureState.Recording;
        elapsed.Restart(); UpdateRecordingTimer();
        Badge.Text = L("●  錄音中"); Heading.Text = L("正在錄低聲音");
        Subtitle.Text = L("可關閉視窗，喺背景繼續錄音。");
        RecordButton.Content = L("■   停止並儲存"); RecordButton.IsEnabled = true;
        StatusText.Text = "WAV · 48 kHz · " + (SystemAudio.IsChecked == true ? L("電腦聲音開啟") : L("電腦聲音關閉")) + " · " + (Microphone.IsChecked == true ? L("咪高峰開啟") : L("咪高峰關閉"));
        UpdateTray();
        _ = ObserveAudioCompletion(current);
        if (closeAfterSave) StopRecording();
    }
    private async System.Threading.Tasks.Task ObserveAudioCompletion(AudioOnlyRecorder current)
    {
        string? error = null;
        try { await current.Completion; } catch (Exception ex) { error = ex.Message; }
        if (ReferenceEquals(audioRecorder, current)) { audioRecorder = null; FinishRecording(error, activePath); }
    }
}
