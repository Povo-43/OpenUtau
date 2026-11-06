using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
using OpenUtau.Audio;
using Serilog;
using globalAndroidMedia = Android.Media;

namespace OpenUtau.Android.Audio;

public class AndroidAudioOutput : IAudioOutput, IDisposable {
    private const int SampleRate = 44100;
    private const int Channels = 2;

    private globalAndroidMedia.AudioTrack? _track;
    private Thread? _thread;
    private bool _running;
    private ISampleProvider? _provider;
    private object _lock = new();

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
    }

    public void Play() {
        lock (_lock) {
            if (_provider == null) return;
            if (_running) return;
            _running = true;
        }
        EnsureTrack();
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

    private void EnsureTrack() {
        if (_track != null) return;
        int bufferSize = globalAndroidMedia.AudioTrack.GetMinBufferSize(
            SampleRate,
            globalAndroidMedia.ChannelOut.Stereo,
            globalAndroidMedia.Encoding.Pcm16bit);
        if (bufferSize < 0) bufferSize = 4096;
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
            var buffer = new float[SampleRate * Channels / 10]; // 100ms
            var pcm = new short[buffer.Length];
            while (_running) {
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
                _track?.Write(pcm, 0, read);
            }
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
