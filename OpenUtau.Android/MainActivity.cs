using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia.Android;
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
public class MainActivity : AvaloniaMainActivity {
    protected override void OnCreate(Bundle? savedInstanceState) {
        base.OnCreate(savedInstanceState);
        // Android オーディオ出力 (AudioTrack) を再生エンジンに差し替え
        PlaybackManager.Inst.AudioOutput = new AndroidAudioOutput();
    }
}
