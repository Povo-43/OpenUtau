using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App {
    /// <summary>
    /// Shared application initialization. Desktop runs this from the splash
    /// window; Android runs it from <see cref="App"/> after the main view is
    /// attached.
    /// </summary>
    internal static class AppStartup {
        public static void InitializeCore(Thread mainThread, TaskScheduler mainScheduler, Action<string>? progress = null) {
            void Step(string message) {
                progress?.Invoke(message);
                Log.Information(message);
            }
            Step("Initializing tools…");
            ToolsManager.Inst.Initialize();
            // Before the singer search, so the warnings it raises can be published.
            Step("Initializing project manager…");
            DocManager.Inst.Initialize(mainThread, mainScheduler);
            DocManager.Inst.PostOnUIThread = action => Dispatcher.UIThread.Post(action);
            Step("Searching singers…");
            SingerManager.Inst.Initialize();
            Log.Information("Initialized OpenUtau.");
            InitAudio();
        }

        private static void InitAudio() {
#if !ANDROID
            Log.Information("Initializing audio.");
            if (OS.IsWindows() && Preferences.Default.AudioBackEnd == 0) {
                try {
                    PlaybackManager.Inst.AudioOutput = new NAudioOutput();
                } catch (System.Exception e0) {
                    Log.Error(e0, "Failed to init NAudio");
                }
            } else {
                switch (Preferences.Default.AudioBackEnd) {
                    case 0:
                    case 1:
                        try {
                            PlaybackManager.Inst.AudioOutput = new Audio.MiniAudioOutput();
                        } catch (System.Exception e1) {
                            Log.Error(e1, "Failed to init MiniAudio");
                        }
                        break;
                    case 2:
                        try {
                            PlaybackManager.Inst.AudioOutput = new Audio.SDL3AudioOutput();
                        } catch (System.Exception e2) {
                            Log.Error(e2, "Failed to init SDL3 Audio");
                        }
                        break;
                }
            }
            Log.Information("Initialized audio.");
#endif
        }
    }
}
