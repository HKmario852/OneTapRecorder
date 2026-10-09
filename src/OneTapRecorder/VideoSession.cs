using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ScreenRecorderLib;

namespace OneTapRecorder;
internal interface IVideoSession : IDisposable
{
    event Action? Started;
    event Action<string?, string?>? Finished;
    void Record(string path);
    void Stop();
}
internal static class VideoSessionFactory
{
    // Avoid initializing the native video stack while the app is idle or recording only audio.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static IVideoSession Create(string display, int width, int height, bool systemAudio, bool microphone)
        => new VideoSession(display, width, height, systemAudio, microphone);
}
internal sealed class VideoSession : IVideoSession
{
    private readonly Recorder recorder;
    public event Action? Started;
    public event Action<string?, string?>? Finished;
    public VideoSession(string display, int width, int height, bool systemAudio, bool microphone)
    {
        var audioSources = new List<AudioSourceBase>();
        if (systemAudio) audioSources.Add(LoopbackAudioSource.Default ?? throw new InvalidOperationException(Localizer.Text("搵唔到聲音輸出裝置。請連接喇叭／耳機，或關閉電腦聲音。")));
        if (microphone) audioSources.Add(CaptureAudioSource.Default ?? throw new InvalidOperationException(Localizer.Text("搵唔到咪高峰。請連接咪高峰，或關閉咪高峰開關。")));
        recorder = Recorder.CreateRecorder(new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = new List<RecordingSourceBase> { new DisplayRecordingSource(display) { RecorderApi = RecorderApi.DesktopDuplication } } },
            OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video, OutputFrameSize = new ScreenSize(width, height) },
            AudioOptions = new AudioOptions { IsAudioEnabled = audioSources.Count > 0, AudioSources = audioSources },
            VideoEncoderOptions = new VideoEncoderOptions { Framerate = 30, Bitrate = 6000000, IsFixedFramerate = true, IsHardwareEncodingEnabled = true, IsMp4FastStartEnabled = true }
        });
        recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) Started?.Invoke(); };
        recorder.OnRecordingComplete += (_, e) => Finished?.Invoke(null, e.FilePath);
        recorder.OnRecordingFailed += (_, e) => Finished?.Invoke(e.Error, null);
    }
    public void Record(string path) => recorder.Record(path);
    public void Stop() => recorder.Stop();
    public void Dispose() => recorder.Dispose();
}
