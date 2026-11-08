using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App {
    /// <summary>Result of one startup step.</summary>
    internal sealed class StartupStep {
        public string Name = "";
        public string Message = "";
        public bool Completed;
    }

    /// <summary>
    /// Shared application initialization. Desktop runs this from the splash
    /// window; Android runs it from <see cref="App"/> after the main view is
    /// attached.
    /// </summary>
    internal static class AppStartup {
        /// <summary>Steps that exceed this budget are skipped so startup can proceed.</summary>
        static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

        public static List<StartupStep> InitializeCore(Thread mainThread, TaskScheduler mainScheduler, Action<string>? progress = null) {
            var steps = new List<StartupStep>();
            steps.Add(RunStep("tools", "Initializing tools…", progress,
                () => ToolsManager.Inst.Initialize()));
            // Before the singer search, so the warnings it raises can be published.
            steps.Add(RunStep("docmanager", "Initializing project manager…", progress,
                () => {
                    DocManager.Inst.Initialize(mainThread, mainScheduler);
                    DocManager.Inst.PostOnUIThread = action => Dispatcher.UIThread.Post(action);
                }));
            steps.Add(RunStep("singers", "Searching singers…", progress,
                () => SingerManager.Inst.Initialize()));
            if (steps.All(s => s.Completed)) {
                Log.Information("Initialized OpenUtau.");
            } else {
                Log.Error("OpenUtau initialization finished with skipped steps: {Steps}",
                    string.Join(", ", steps.Where(s => !s.Completed).Select(s => s.Name)));
            }
            InitAudio();
            return steps;
        }

        /// <summary>
        /// Runs one startup step with a time budget. A step that finishes in time
        /// propagates exceptions (fatal, shown on the error screen). A step that
        /// exceeds the budget is abandoned and skipped: the orphaned task keeps
        /// running in the background and applies its work if it ever completes,
        /// while startup moves on. Callers can check <see cref="StartupStep.Completed"/>
        /// to degrade gracefully (e.g., an empty singer list).
        /// </summary>
        static StartupStep RunStep(string name, string message, Action<string>? progress, Action body) {
            progress?.Invoke(message);
            Log.Information(message);
            var step = new StartupStep { Name = name, Message = message };
            var task = Task.Run(body);
            try {
                task.Wait(StepTimeout);
            } catch (AggregateException e) {
                Log.Error(e, "Startup step failed: {Step}", name);
                throw e.GetBaseException();
            }
            if (!task.IsCompleted) {
                step.Completed = false;
                Log.Error("Startup step timed out after {Seconds}s: {Step} — skipping it.",
                    StepTimeout.TotalSeconds, name);
                progress?.Invoke($"{message} (timed out — skipped)");
                _ = task.ContinueWith(t => {
                    if (t.IsFaulted) {
                        Log.Error(t.Exception, "Startup step {Step} failed late.", name);
                    } else {
                        Log.Information("Startup step {Step} completed late.", name);
                    }
                });
                return step;
            }
            step.Completed = true;
            return step;
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
