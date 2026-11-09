using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
using OpenUtau.Audio;
using Serilog;
using globalAndroidMedia = Android.Media;
using globalAndroidApp = Android.App;
using globalAndroidContent = Android.Content;

namespace OpenUtau.Android.Audio;

public class AndroidAudioOutput : IAudioOutput, IDisposable {
    private const int SampleRate = 44100;
    private const int Channels = 2;

    private globalAndroidMedia.AudioTrack? _track;
    private Thread? _thread;
    private bool _running;
    private ISampleProvider? _provider;
    private object _lock = new();
    private List<short> pendingPcm = new();

    public PlaybackState PlaybackState {
        get {
            lock (_lock) {
                if (!_running) return PlaybackState.Stopped;
                return _track?.PlayState == globalAndroidMedia.PlayState.Playing ? PlaybackState.Playing : PlaybackState.Paused;
            }
        }
    }
    public int DeviceNumber { get; private set; }

    public void SelectDevice(Guid guid, int deviceNumber) {
        DeviceNumber = deviceNumber;
    }

    public void Init(ISampleProvider sampleProvider) {
        Stop();
        lock (_lock) {
            _provider = sampleProvider;
        }
        Log.Information("AndroidAudioOutput.Init: provider set.");
    }

    public void Play() {
        lock (_lock) {
            if (_provider == null) {
                Log.Warning("AndroidAudioOutput.Play: provider is null, cannot start.");
                return;
            }
            if (_running) {
                Log.Information("AndroidAudioOutput.Play: already running.");
                return;
            }
            _running = true;
        }
        Log.Information("AndroidAudioOutput.Play: starting AudioTrack and render loop.");
        EnsureTrack();
        RequestAudioFocus();
        _track!.SetStereoVolume(1.0f, 1.0f);
        _track!.Play();
        _thread = new Thread(RenderLoop) { IsBackground = true, Name = "OpenUtau Android Audio" };
        _thread.Start();
    }

    public void Pause() {
        lock (_lock) {
            if (!_running) return;
            _running = false;
        }
        _track?.Pause();
    }

    public void Stop() {
        lock (_lock) {
            _running = false;
            _provider = null;
        }
        _track?.Stop();
        _track?.Release();
        _track = null;
        if (_thread != null && _thread != Thread.CurrentThread) {
            _thread.Join(1000);
        }
        _thread = null;
    }

    public long GetPosition() {
        return _track?.PlaybackHeadPosition ?? 0;
    }

    public List<AudioOutputDevice> GetOutputDevices() {
        return new List<AudioOutputDevice> {
            new() {
                name = "Default Output",
                api = "Android AudioTrack",
                deviceNumber = 0,
                guid = Guid.Empty,
            }
        };
    }

    private void RequestAudioFocus() {
        try {
            var audioManager = (globalAndroidMedia.AudioManager)globalAndroidApp.Application.Context.GetSystemService(globalAndroidContent.Context.AudioService);
            var result = audioManager.RequestAudioFocus(null, globalAndroidMedia.Stream.Music, globalAndroidMedia.AudioFocus.Gain);
            Log.Information("AndroidAudioOutput.RequestAudioFocus: result={Result}.", result);
        } catch (Exception ex) {
            Log.Warning(ex, "AndroidAudioOutput.RequestAudioFocus failed.");
        }
    }

    private void EnsureTrack() {
        if (_track != null) return;
        Log.Information("AndroidAudioOutput.EnsureTrack: creating AudioTrack.");
        int bufferSize = globalAndroidMedia.AudioTrack.GetMinBufferSize(
            SampleRate,
            globalAndroidMedia.ChannelOut.Stereo,
            globalAndroidMedia.Encoding.Pcm16bit);
        if (bufferSize < 0) bufferSize = 4096;
        // 最小バッファサイズでは書き込みが追いつかないことがあるため
        // 4倍に確保する。
        bufferSize *= 4;
        _track = new globalAndroidMedia.AudioTrack(
            globalAndroidMedia.Stream.Music,
            SampleRate,
            globalAndroidMedia.ChannelConfiguration.Stereo,
            globalAndroidMedia.Encoding.Pcm16bit,
            bufferSize,
            globalAndroidMedia.AudioTrackMode.Stream);
    }

    private void RenderLoop() {
        try {
            Log.Information("AndroidAudioOutput.RenderLoop: started.");
            var buffer = new float[SampleRate * Channels / 10]; // 100ms
            var pcm = new short[buffer.Length];
            while (_running) {
                // まず保留中のデータを書き込む
                if (pendingPcm.Count > 0) {
                    int toWrite = Math.Min(pendingPcm.Count, pcm.Length);
                    for (int i = 0; i < toWrite; i++) {
                        pcm[i] = pendingPcm[i];
                    }
                    pendingPcm.RemoveRange(0, toWrite);
                    var written = _track?.Write(pcm, 0, toWrite) ?? 0;
                    if (written < toWrite) {
                        // 書き込めなかった分を先頭に戻してリトライ
                        for (int i = written; i < toWrite; i++) {
                            pendingPcm.Insert(0, pcm[i]);
                        }
                        Log.Warning("AndroidAudioOutput.RenderLoop: wrote {Written}/{ToWrite} pending samples.", written, toWrite);
                        Thread.Sleep(10);
                        continue;
                    }
                    continue;
                }

                ISampleProvider? provider;
                lock (_lock) {
                    provider = _provider;
                    if (!_running || provider == null) break;
                }
                int read = provider.Read(buffer, 0, buffer.Length);
                if (read <= 0) {
                    Thread.Sleep(10);
                    continue;
                }
                for (int i = 0; i < read; i++) {
                    float sample = Math.Clamp(buffer[i], -1f, 1f);
                    pcm[i] = (short)(sample * short.MaxValue);
                }
                var written2 = _track?.Write(pcm, 0, read) ?? 0;
                if (written2 < read) {
                    // 書き込めなかった分を保留にして次回に回す
                    for (int i = written2; i < read; i++) {
                        pendingPcm.Add(pcm[i]);
                    }
                    Log.Warning("AndroidAudioOutput.RenderLoop: wrote {Written}/{Read} samples, {Pending} pending.", written2, read, pendingPcm.Count);
                }
            }
            Log.Information("AndroidAudioOutput.RenderLoop: stopped normally.");
        } catch (Exception ex) {
            Log.Error(ex, "AndroidAudioOutput render loop error");
        } finally {
            lock (_lock) {
                _running = false;
            }
        }
    }

    public void Dispose() {
        Stop();
        _track?.Dispose();
        _track = null;
    }
}
