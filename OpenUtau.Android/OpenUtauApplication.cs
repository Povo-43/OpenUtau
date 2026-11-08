using System;
using System.IO;
using System.Text;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using OpenUtau.Android.Audio;
using OpenUtau.Core;
using ReactiveUI.Avalonia;
using Serilog;

namespace OpenUtau.Android;

[Register("com.stakira.openutau.OpenUtauApplication")]
[Application]
public class OpenUtauApplication : AvaloniaAndroidApplication<global::OpenUtau.App.App> {
    protected OpenUtauApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer) {
    }

    public override void OnCreate() {
        base.OnCreate();
        InitAndroidLogging();
        Log.Information("OpenUtau Android application created.");
        // Android オーディオ出力 (AudioTrack) を再生エンジンに差し替え
        PlaybackManager.Inst.AudioOutput = new AndroidAudioOutput();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) {
        return base.CustomizeAppBuilder(builder)
            .LogToTrace()
            // Required for ReactiveUI (RxApp static initialization); the desktop
            // builder does the same in Program.BuildAvaloniaApp. Without it the
            // first ViewModel creation fails with "ReactiveUI has not been
            // initialized".
            .UseReactiveUI(_ => { })
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 256 * 1024 * 1024 // 256 MB
            });
    }

    /// <summary>
    /// Desktop configures Serilog in Program.Main, which never runs on Android.
    /// Without this every Log.* call went to the silent logger, so no logs were
    /// captured on devices. Logs go to logcat and to files: the app's internal
    /// data dir, plus the external app dir (accessible over USB/MTP from a PC).
    /// </summary>
    void InitAndroidLogging() {
        try {
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.Information()
                    .WriteTo.File(PathManager.Inst.LogFilePath,
                        rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8));
            var externalDir = GetExternalFilesDir(null)?.AbsolutePath;
            if (!string.IsNullOrEmpty(externalDir)) {
                PathManager.ExternalLogDirectory = externalDir;
                logger.WriteTo.Logger(lc => lc
                    .MinimumLevel.Information()
                    .WriteTo.File(Path.Combine(externalDir, "OpenUtau-.log"),
                        rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8));
            }
            Log.Logger = logger.CreateLogger();
        } catch (Exception e) {
            // Logging must never take the app down; fall back to the silent logger.
            System.Diagnostics.Debug.WriteLine($"Failed to initialize logging: {e}");
        }
    }
}
