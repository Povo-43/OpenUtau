using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace OpenUtau.Android;

[Application]
public class OpenUtauApplication : AvaloniaAndroidApplication<global::OpenUtau.App.App> {
    protected OpenUtauApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer) {
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
