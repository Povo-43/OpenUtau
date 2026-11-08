using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Avalonia.Android;
using OpenUtau.Android.Audio;
using OpenUtau.Core;
using Serilog;

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

        // ステータスバーの高さをアプリ側に通知し、コンテンツを下に押し出す
        // パディングに使ってもらう（メニューバーとステータスバーの重なり防止）。
        // GetDimensionPixelSize は物理ピクセルを返すので、Avalonia の DIP に換算する。
        var resId = Resources.GetIdentifier("status_bar_height", "dimen", "android");
        if (resId > 0) {
            var pixels = Resources.GetDimensionPixelSize(resId);
            var density = Resources.DisplayMetrics.Density;
            OpenUtau.App.App.StatusBarHeight = pixels / density;
            Log.Information("Android status bar height: {Height:F0} DIP ({Pixels}px @ {Density:F1}x).",
                OpenUtau.App.App.StatusBarHeight, pixels, density);
        }
    }

    public override void OnBackPressed() {
        // 戻るボタンは、まず最前面の擬似ウィンドウを閉じる。
        if (OpenUtau.App.Views.DialogHost.CloseTopOverlay()) {
            return;
        }
        base.OnBackPressed();
    }

    protected override void OnDestroy() {
        // 戻るボタン等で閉じたらプロセスごと終了する。Android はプロセスをキャッシュして
        // 再オープン時に使い回すが、その場合 Avalonia の App インスタンスやビューが前回の
        // (破棄済みアクティビティに紐づく)もののまま再利用され、起動シーケンスが再実行されず
        // ローディング画面のまま固まる。毎回クリーンなプロセスで起動する方が安定する。
        // (向き変更などは ConfigurationChanges で処理されるため OnDestroy は呼ばれない)
        if (IsFinishing) {
            Log.Information("Activity finishing; exiting the process for a clean next launch.");
            Log.CloseAndFlush();
            Java.Lang.JavaSystem.Exit(0);
        }
        base.OnDestroy();
    }
}
