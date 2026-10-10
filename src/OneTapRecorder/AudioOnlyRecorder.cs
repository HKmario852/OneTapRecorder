using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OneTapRecorder;

// A separate audio pipeline: no display capture, video encoder, or video stream.
internal sealed class AudioOnlyRecorder
{
    private readonly List<WasapiCapture> sources = new();
    private readonly List<BufferedWaveProvider> buffers = new();
    private readonly Stopwatch clock = new();
    private readonly object stopLock = new();
    private bool stopping;
    private readonly CancellationTokenSource stopSignal = new();
    private Exception? captureError;
    private readonly WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start(string path, bool system, bool microphone)
    {
        if (!system && !microphone) throw new InvalidOperationException(Localizer.Text("AudioSourceRequired"));
        WaveFileWriter? writer = null;
        try
        {
            if (system) Add(new WasapiLoopbackCapture());
            if (microphone) Add(new WasapiCapture());
            writer = new WaveFileWriter(new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite), new WaveFormat(48000, 16, 2));
            foreach (var source in sources) source.StartRecording();
            clock.Start();
            var output = new MixingSampleProvider(format) { ReadFully = true };
            foreach (var buffer in buffers) output.AddMixerInput(new VolumeSampleProvider(buffer.ToSampleProvider()) { Volume = 1f / buffers.Count });
            var pcm = new SampleToWaveProvider16(output);
            var ownedWriter = writer;
            Completion = Task.Run(async () =>
            {
                Exception? failure = null;
                long framesWritten = 0;
                byte[] data = new byte[4800 * 4];
                void WriteUntil(long target)
                {
                    while (framesWritten < target)
                    {
                        int frames = (int)Math.Min(4800, target - framesWritten);
                        if (ownedWriter.Length + frames * 4 > 4_000_000_000L) throw new IOException(Localizer.Text("WavLimit"));
                        int read = pcm.Read(data, 0, frames * 4);
                        ownedWriter.Write(data, 0, read);
                        framesWritten += read / 4;
                    }
                }
                try
                {
                    long lastFlush = 0;
                    while (!Volatile.Read(ref stopping))
                    {
                        // Keep 200ms of capture latency; write silence on the wall clock when Windows sends no packets.
                        WriteUntil(Math.Max(0, (long)((clock.Elapsed.TotalSeconds - .2) * 48000)));
                        if (framesWritten - lastFlush >= 48000) { ownedWriter.Flush(); lastFlush = framesWritten; }
                        await Task.Delay(100, stopSignal.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (Volatile.Read(ref stopping)) { }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    RequestStop();
                    // Dispose waits for the capture threads to finish before the last buffered samples are read.
                    foreach (var source in sources) { try { source.Dispose(); } catch (Exception ex) { failure ??= ex; } }
                    try { WriteUntil((long)(clock.Elapsed.TotalSeconds * 48000)); } catch (Exception ex) { failure ??= ex; }
                    try { ownedWriter.Dispose(); } catch (Exception ex) { failure ??= ex; }
                }
                stopSignal.Dispose();
                if (failure != null) throw failure;
                if (captureError != null) throw captureError;
            });
        }
        catch
        {
            foreach (var source in sources) source.Dispose();
            writer?.Dispose();
            throw;
        }
    }
    private void Add(WasapiCapture source)
    {
        sources.Add(source);
        source.WaveFormat = format;
        var buffer = new BufferedWaveProvider(format, TimeSpan.FromSeconds(2)) { ReadFully = true, DiscardOnBufferOverflow = false };
        buffers.Add(buffer);
        source.DataAvailable += (_, e) =>
        {
            try { buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); }
            catch (Exception ex) { Interlocked.CompareExchange(ref captureError, ex, null); RequestStop(); }
        };
        source.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null) { Interlocked.CompareExchange(ref captureError, e.Exception, null); RequestStop(); }
        };
    }
    public void RequestStop()
    {
        lock (stopLock)
        {
            if (stopping) return;
            stopping = true;
            clock.Stop();
            stopSignal.Cancel();
            foreach (var source in sources) { try { source.StopRecording(); } catch (Exception ex) { captureError ??= ex; } }
        }
    }
}
