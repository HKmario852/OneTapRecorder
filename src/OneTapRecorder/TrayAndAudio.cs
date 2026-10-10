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

    private void InitializeTray()
    {
        trayImage = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
        InitializeStateIcons();
        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Opening += (_, _) => { UpdateClockDisplay(); UpdateTray(); };
        trayMenu.Items.Add(L("TrayOpenWindow"), null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        trayRecord = new Forms.ToolStripMenuItem(L("StartScreen"), null, (_, _) => Dispatcher.Invoke(ToggleRecording));
        trayMenu.Items.Add(trayRecord);
        trayMenu.Items.Add(L("TrayOpenFolder"), null, (_, _) => Dispatcher.Invoke(() => OpenFolderClick(this, new RoutedEventArgs())));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayExit = new Forms.ToolStripMenuItem(L("TrayExit"), null, (_, _) => Dispatcher.Invoke(RequestExit));
        trayMenu.Items.Add(trayExit);
        trayIcon = new Forms.NotifyIcon { Icon = trayImage, Text = L("TrayReady"), ContextMenuStrip = trayMenu, Visible = true };
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
            CaptureState.Recording => L("TrayRecording"),
            CaptureState.Starting => L("TrayStarting"),
            CaptureState.Stopping => L("TraySaving"),
            _ => L(AudioOnly ? "TrayIdleAudio" : "TrayIdleScreen")
        };
        trayRecord.Text = state == CaptureState.Idle ? L(AudioOnly ? "StartAudio" : "StartScreen") : L("StopAndSave");
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
            trayIcon?.ShowBalloonTip(3000, L("BackgroundTitle"), L("BackgroundText"), Forms.ToolTipIcon.Info);
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
        Heading.Text = L("ReadyHeading");
        Badge.Text = L("ReadyBadge");
        TimerText.Text = "00:00:00";
        ApplyMode();
    }
    private void ApplyMode()
    {
        MonitorPanel.Visibility = AudioOnly ? Visibility.Collapsed : Visibility.Visible;
        Subtitle.Text = AudioOnly ? L("AudioSubtitle") : L("ScreenSubtitle");
        RecordButton.Content = L(AudioOnly ? "StartAudioButton" : "StartScreenButton");
        StatusText.Text = AudioOnly ? L("AudioFormat") : L("ScreenFormat");
        RefreshMonitors(); UpdateTray();
    }
    private void StartAudioOnly()
    {
        var current = new AudioOnlyRecorder();
        audioRecorder = current;
        current.Start(activePath!, SystemAudio.IsChecked == true, Microphone.IsChecked == true);
        state = CaptureState.Recording;
        elapsed.Restart(); UpdateRecordingTimer();
        Badge.Text = L("RecordingAudioBadge"); Heading.Text = L("RecordingAudioHeading");
        Subtitle.Text = L("RecordingAudioSubtitle");
        RecordButton.Content = L("StopAndSaveButton"); RecordButton.IsEnabled = true;
        StatusText.Text = "WAV · 48 kHz · " + (SystemAudio.IsChecked == true ? L("ComputerAudioOn") : L("ComputerAudioOff")) + " · " + (Microphone.IsChecked == true ? L("MicOn") : L("MicOff"));
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
