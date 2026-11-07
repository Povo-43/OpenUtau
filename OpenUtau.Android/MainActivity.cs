using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.AppCompat.App;
using Avalonia;
using Avalonia.Android;
using OpenUtau.Android.Audio;
using OpenUtau.App.Views;
using OpenUtau.Core;

namespace OpenUtau.Android;

[Register("com.stakira.openutau.MainActivity")]
[Activity(
    Label = "OpenUtau",
    Theme = "@style/MyTheme",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.FontScale)]
[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryLauncher })]
public class MainActivity : AppCompatActivity
{
    private AvaloniaView? _avaloniaView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Avalonia アプリを構築
        var builder = AppBuilder.Configure<global::OpenUtau.App.App>()
            .UseAndroid()
            .LogToTrace()
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 256 * 1024 * 1024 // 256 MB
            });

        var _ = builder.Build();

        // メインウィンドウを AvaloniaView に設定
        var mainWindow = new MainWindow();
        _avaloniaView = new AvaloniaView(this);
        _avaloniaView.Content = mainWindow;
        SetContentView(_avaloniaView);

        // Android オーディオ出力 (AudioTrack) を再生エンジンに差し替え
        PlaybackManager.Inst.AudioOutput = new AndroidAudioOutput();
    }

    protected override void OnDestroy()
    {
        _avaloniaView?.Dispose();
        base.OnDestroy();
    }
}
