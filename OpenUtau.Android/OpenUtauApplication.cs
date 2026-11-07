using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using OpenUtau.Android.Audio;
using OpenUtau.Core;

namespace OpenUtau.Android;

// OpenUtauApplication は Android の Application クラスとしては使用しません。
// 初期化は MainActivity.OnCreate で行います。
// 将来の Android 固有初期化コードのためにクラスは残しておきます。
namespace OpenUtau.Android {
    public class OpenUtauApplication {
    }
}
