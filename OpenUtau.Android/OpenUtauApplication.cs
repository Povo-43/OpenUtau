using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using OpenUtau.Android.Audio;
using OpenUtau.Core;

namespace OpenUtau.Android;

[Register("com.stakira.openutau.OpenUtauApplication")]
[Application]
public class OpenUtauApplication : AvaloniaAndroidApplication<global::OpenUtau.App.App> {
    protected OpenUtauApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer) {
    }

    public override void OnCreate() {
        base.OnCreate();
        // Android オーディオ出力 (AudioTrack) を再生エンジンに差し替え
        PlaybackManager.Inst.AudioOutput = new AndroidAudioOutput();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) {
        return base.CustomizeAppBuilder(builder)
            .LogToTrace()
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 256 * 1024 * 1024 // 256 MB
            });
    }
}
