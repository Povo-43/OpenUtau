using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUtau.App.Views;
using OpenUtau.Colors;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App {
    public class App : Application {
        public override void Initialize() {
            Log.Information("Initializing application.");
            // Desktop Program.Main registers this, but the Android entry point never
            // runs Program.Main. Without it, Encoding.GetEncoding("shift_jis") fails,
            // which breaks UST files and voicebank loading (static initializers in
            // Ust/EnunuUtils). Registration is idempotent.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            AvaloniaXamlLoader.Load(this);
#if DEBUG
            this.AttachDeveloperTools();
#endif
            InitializeCulture();
            InitializeTheme();
            Log.Information("Initialized application.");
        }

        public override void OnFrameworkInitializationCompleted() {
            Log.Information("Framework initialization completed.");
            RegisterUnhandledExceptionHandlers();
#if !ANDROID
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new SplashWindow();
            }
#else
            // Android: single-view app. The real MainWindow view is used as the
            // root; dialogs are shown as in-app overlays (see DialogHost).
            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView) {
                var errorText = new TextBlock {
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                };
                var loadingText = new TextBlock {
                    Text = "Loading OpenUtau…",
                    FontSize = 18,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                var statusText = new TextBlock {
                    FontSize = 12,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextAlignment = TextAlignment.Center,
                    IsVisible = false,
                };
                var copyLogButton = new Button {
                    Content = "Copy log to clipboard",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0),
                    IsVisible = false,
                };
                var loadingPanel = new StackPanel {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children = { loadingText, statusText, copyLogButton },
                };
                var loadingView = new ScrollViewer { Content = loadingPanel };
                // Assign the single view root exactly once, then swap its children.
                // Replacing ISingleViewApplicationLifetime.MainView later relies on
                // the platform re-attaching the whole content, which on some devices
                // never completes and leaves the app on the last frame; swapping a
                // child of an already attached control always works.
                var rootHost = new Border();
                rootHost.Child = loadingView;
                singleView.MainView = rootHost;

#if ANDROID
                // Push content below the status bar so the menu bar and controls
                // don't overlap the system UI. The height is set by MainActivity
                // after Android resources are available; LayoutUpdated retries
                // until it is non-zero.
                double appliedStatusBarHeight = -1;
                void ApplyStatusBarPadding() {
                    // LayoutUpdated fires on every pass; only touch the padding (and
                    // log) when the height actually changes.
                    if (StatusBarHeight > 0 && StatusBarHeight != appliedStatusBarHeight) {
                        appliedStatusBarHeight = StatusBarHeight;
                        rootHost.Padding = new Thickness(0, StatusBarHeight, 0, 0);
                        Log.Information("Status bar height: {Height:F0}, padding applied.", StatusBarHeight);
                    }
                }
                ApplyStatusBarPadding();
                rootHost.LayoutUpdated += (_, _) => ApplyStatusBarPadding();
#endif

                // If startup takes unusually long, surface a log copy button so the
                // user can report where it is stuck.
                var startedAt = DateTime.UtcNow;
                var stuckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                copyLogButton.Click += async (_, _) => {
                    try {
                        await TopLevel.GetTopLevel(loadingView)!.Clipboard!
                            .SetTextAsync(CollectStartupLogs());
                        copyLogButton.Content = "Copied! Please paste it in the report.";
                    } catch (Exception e) {
                        Log.Error(e, "Failed to copy startup log.");
                        statusText.Text = DescribeException(e);
                    }
                };
                var startupDone = new VolatileBool();
                stuckTimer.Tick += (_, _) => {
                    if (loadingView.Content != loadingPanel) {
                        stuckTimer.Stop();
                        return;
                    }
                    UiHeartbeat();
                    var elapsed = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                    statusText.IsVisible = true;
                    statusText.Text = $"Still working… {elapsed}s elapsed. " +
                        "If this does not finish, tap the button below and report the log.";
                    copyLogButton.IsVisible = elapsed >= 15;
                };
                stuckTimer.Start();
                long mainViewExpectedTicks = 0;
                void StopStuckTimer(bool expectingMainView = false) {
                    startupDone.Value = true;
                    stuckTimer.Stop();
                    UiStage = "running";
                    if (expectingMainView) {
                        mainViewExpectedTicks = DateTime.UtcNow.Ticks;
                    }
                }

#if ANDROID
                // Watchdog: the UI thread must keep pumping. The loading timer feeds
                // the heartbeat during startup and the main window heartbeat timer
                // afterwards. When the UI thread goes silent, or the main view never
                // attaches to the visual tree, record the stage and exit the process
                // so the next tap starts a clean one instead of leaving a frozen app
                // on screen. A marker file limits automatic restarts so a
                // deterministic hang cannot create a restart loop.
                void RecoverFrozen(string reason) {
                    try {
                        var marker = Path.Combine(PathManager.Inst.DataPath, "ui-restarts.txt");
                        int count = 1;
                        long stamp = 0;
                        if (File.Exists(marker)) {
                            var parts = File.ReadAllText(marker).Split(' ');
                            stamp = long.Parse(parts[0]);
                            count = int.Parse(parts[1]);
                            if (DateTime.UtcNow.Ticks - stamp > TimeSpan.FromMinutes(4).Ticks) {
                                count = 0;
                            }
                        }
                        if (count >= 2) {
                            Log.Error("{Reason}; not auto-restarting again (count {Count}).", reason, count);
                            return;
                        }
                        File.WriteAllText(marker, $"{DateTime.UtcNow.Ticks} {count + 1}");
                        Log.Error("{Reason}; restarting the process to recover (attempt {Count}).", reason, count + 1);
                        Log.CloseAndFlush();
                    } catch (Exception e) {
                        Log.Error(e, "Failed to handle the restart marker.");
                        Log.CloseAndFlush();
                    }
                    RequestRestartProcess();
                }

                var uiFrozenWatchdog = new Thread(() => {
                    int lastLoggedIdle = int.MinValue;
                    while (true) {
                        Thread.Sleep(TimeSpan.FromSeconds(10));
                        int idle = (int)((DateTime.UtcNow.Ticks - Volatile.Read(ref LastUiHeartbeat))
                            / TimeSpan.TicksPerSecond);
                        if (idle >= 40) {
                            if (lastLoggedIdle < 0 || idle - lastLoggedIdle >= 60) {
                                Log.Error("UI thread has not pumped for {Idle}s (stage: {Stage}) — frozen.",
                                    idle, UiStage);
                                lastLoggedIdle = idle;
                            }
                            if (idle >= 60) {
                                RecoverFrozen($"UI thread frozen (stage: {UiStage})");
                                return;
                            }
                        } else {
                            lastLoggedIdle = 0;
                        }
                        // The app can also stall without the UI thread going quiet:
                        // the main view may never be attached (nothing is rendered,
                        // the last frame stays on screen).
                        if (mainViewExpectedTicks != 0 && Views.MainWindow.Instance == null) {
                            var waited = (int)((DateTime.UtcNow.Ticks - mainViewExpectedTicks)
                                / TimeSpan.TicksPerSecond);
                            if (waited % 30 == 0) {
                                Log.Error("Main view has not attached {Waited}s after startup (stage: {Stage}).",
                                    waited, UiStage);
                            }
                            if (waited >= 30) {
                                RecoverFrozen($"Main view never attached (stage: {UiStage}, waited {waited}s)");
                                return;
                            }
                        }
                    }
                }) {
                    IsBackground = true,
                    Name = "ui-freeze-watchdog",
                };
                uiFrozenWatchdog.Start();
#endif

                var mainThread = Thread.CurrentThread;
                var mainScheduler = TaskScheduler.FromCurrentSynchronizationContext();
                Task.Run(() => AppStartup.InitializeCore(mainThread, mainScheduler,
                        // Show each startup step on screen; the last visible step
                        // identifies where startup is stuck on a device.
                        step => Dispatcher.UIThread.Post(() => {
                            loadingText.Text = step;
                            Log.Information("Startup screen: {Step}", step);
                        })))
                    .ContinueWith(t => {
                        if (t.IsFaulted) {
                            Log.Error(t.Exception, "Failed to start OpenUtau.");
                            ShowStartupError(loadingView, errorText, t.Exception);
                            StopStuckTimer();
                            return;
                        }
                        var steps = t.Result;
                        var docManager = steps.FirstOrDefault(s => s.Name == "docmanager");
                        if (docManager == null || !docManager.Completed) {
                            // Creating the main window would touch singletons that may
                            // still be initializing; report instead of risking a hang.
                            Log.Error("Startup incomplete; doc manager step did not finish in time.");
                            loadingText.Text = "Startup did not complete in time.";
                            statusText.IsVisible = true;
                            statusText.Text = "Timed out: " +
                                string.Join(", ", steps.Where(s => !s.Completed).Select(s => s.Name)) +
                                ". Tap the button below and report the log.";
                            copyLogButton.IsVisible = true;
                            StopStuckTimer();
                            return;
                        }
                        bool mainViewExpected = false;
                        try {
                            loadingText.Text = "Creating main window…";
                            Log.Information("Startup stage: creating main window.");
                            UiStage = "creating main window";
                            var mainWindow = new Views.MainWindow();
                            Log.Information("Startup stage: attaching main view.");
                            UiStage = "attaching main view";
                            rootHost.Child = mainWindow;
                            rootHost.InvalidateMeasure();
                            loadingText.Text = "Opening project…";
                            Log.Information("Startup stage: initializing project.");
                            UiStage = "initializing project";
                            mainWindow.InitProject();
                            Log.Information("Startup stage: initializing loading window.");
                            LoadingWindow.InitializeLoadingWindow();
                            Log.Information("Startup completed.");
                            mainViewExpected = true;
                        } catch (Exception e) {
                            Log.Error(e, "Failed to create main window.");
                            UiStage = "main window failed";
                            ShowStartupError(loadingView, errorText, e);
                            rootHost.Child = loadingView;
                        }
                        StopStuckTimer(mainViewExpected);
                    }, mainScheduler);
            }
#endif

            base.OnFrameworkInitializationCompleted();
            UiFontManager.Apply();
        }

        private sealed class VolatileBool {
            bool backing;
            public bool Value {
                get => Volatile.Read(ref backing);
                set => Volatile.Write(ref backing, value);
            }
        }

        /// <summary>
        /// Android 側で設定されるステータスバーの高さ（ピクセル）。
        /// コンテンツをステータスバーの下に押し出す際のパディングに使用する。
        /// </summary>
        public static double StatusBarHeight { get; set; }

        /// <summary>
        /// Unix ticks of the last UI-thread heartbeat. Fed by the loading timer and,
        /// after startup, by the main window heartbeat timer, watched by the
        /// UI-freeze watchdog.
        /// </summary>
        public static long LastUiHeartbeat = DateTime.UtcNow.Ticks;

        /// <summary>The startup stage to report when the UI thread freezes.</summary>
        public static string UiStage = "loading";

        /// <summary>Records a live UI thread heartbeat (called on the UI thread).</summary>
        public static void UiHeartbeat() {
            Volatile.Write(ref LastUiHeartbeat, DateTime.UtcNow.Ticks);
        }

        /// <summary>
        /// Set by the Android host; asks the runtime to end the process so the next
        /// tap launches a clean one (recovery for a frozen UI thread).
        /// </summary>
        public static Action? RestartProcess = null;

        /// <summary>Ends the process for a clean restart (see <see cref="RestartProcess"/>).</summary>
        public static void RequestRestartProcess() {
            if (RestartProcess is { } restart) {
                restart();
                return;
            }
            Environment.Exit(1);
        }

        /// <summary>
        /// Collects the most recent log files (internal data dir + Android external
        /// app dir) for a bug report.
        /// </summary>
        internal static string CollectStartupLogs() {
            var sb = new StringBuilder();
            sb.AppendLine($"App version: {typeof(App).Assembly.GetName().Version}");
            sb.AppendLine($"Time: {DateTime.UtcNow:O}");
            var dirs = new[] { PathManager.Inst.LogsPath, PathManager.ExternalLogDirectory };
            foreach (var dir in dirs) {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) {
                    continue;
                }
                try {
                    var newest = Directory.GetFiles(dir)
                        .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                        .FirstOrDefault();
                    if (newest == null) {
                        sb.AppendLine($"(no log files in {dir})");
                        continue;
                    }
                    sb.AppendLine($"---- {newest} (last 400 lines) ----");
                    using var reader = new StreamReader(newest);
                    var allLines = new List<string>();
                    while (reader.ReadLine() is { } line) {
                        allLines.Add(line);
                    }
                    foreach (var line in allLines.TakeLast(400)) {
                        sb.AppendLine(line);
                    }
                } catch (Exception e) {
                    sb.AppendLine($"Failed to read logs in {dir}: {DescribeException(e)}");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Shows a startup failure with the full exception chain (types, messages and the
        /// deepest stack trace) instead of a single line, and copies the detail to the
        /// clipboard so it can be pasted into a bug report.
        /// </summary>
        void ShowStartupError(ScrollViewer container, TextBlock errorText, Exception? exception) {
            string detail = DescribeException(exception);
            Log.Information("Startup failure detail:\n{Detail}", detail);
            errorText.Text = "Failed to start OpenUtau:\n\n" + detail
                + "\n(This text was copied to the clipboard. Please report it.)";
            container.Content = errorText;
            // Best-effort clipboard copy; the clipboard may be unavailable on some platforms.
            try {
                _ = TopLevel.GetTopLevel(errorText)?.Clipboard?.SetTextAsync(detail);
            } catch (Exception e) {
                Log.Error(e, "Failed to copy startup error to clipboard.");
            }
        }

        internal static string DescribeException(Exception? exception) {
            var sb = new StringBuilder();
            var deepest = exception;
            int depth = 0;
            for (var cur = exception; cur != null; cur = cur.InnerException, depth++) {
                sb.Append(new string(' ', depth * 2));
                sb.Append(depth == 0 ? string.Empty : "-> ");
                sb.AppendLine($"{cur.GetType().FullName}: {cur.Message}");
                deepest = cur;
            }
            sb.AppendLine();
            sb.AppendLine("---- Stack trace of the deepest exception ----");
            sb.AppendLine(deepest?.StackTrace ?? "(no stack trace)");
            return sb.ToString();
        }

        // Program.InitLogging() only hooks AppDomain.UnhandledException, which can log but can not
        // keep the process alive. The two hooks below cover the cases a user actually hits: an
        // exception escaping an async void UI handler, and a faulted Task nobody awaited.
        // Both are logged and surfaced instead of taking the whole application down.
        void RegisterUnhandledExceptionHandlers() {
            TaskScheduler.UnobservedTaskException += (sender, args) => {
                // Cancellation is normal control flow here: renders and phonemization are
                // cancelled all the time, and those exceptions are not failures.
                var cancelled = args.Exception.InnerExceptions.All(e => e is OperationCanceledException);
                if (cancelled) {
                    Log.Debug("Unobserved task exception (cancellation).");
                } else {
                    Log.Error(args.Exception, "Unobserved task exception");
                    try {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(args.Exception));
                    } catch (Exception e) {
                        Log.Error(e, "Failed to report an unobserved task exception");
                    }
                }
                args.SetObserved();
            };

            Dispatcher.UIThread.UnhandledException += (sender, args) => {
                Log.Error(args.Exception, "Unhandled UI exception");
                args.Handled = true;
                try {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(args.Exception));
                } catch (Exception e) {
                    Log.Error(e, "Failed to report an unhandled UI exception");
                }
            };
        }

        public void InitializeCulture() {
            Log.Information("Initializing culture.");
            string sysLang = CultureInfo.InstalledUICulture.Name;
            string prefLang = Core.Util.Preferences.Default.Language;
            var languages = GetLanguages();
            if (languages.ContainsKey(prefLang)) {
                SetLanguage(prefLang);
            } else if (languages.ContainsKey(sysLang)) {
                SetLanguage(sysLang);
                Core.Util.Preferences.Default.Language = sysLang;
                Core.Util.Preferences.Save();
            } else {
                SetLanguage("en-US");
            }

            // Force using InvariantCulture to prevent issues caused by culture dependent string conversion, especially for floating point numbers.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            UiFontManager.Apply();
            Log.Information("Initialized culture.");
        }

        public static Dictionary<string, IResourceProvider> GetLanguages() {
            if (Current == null) {
                return new();
            }
            var result = new Dictionary<string, IResourceProvider>();
            foreach (string key in Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("strings-") &&
                    Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res is IResourceProvider rp) {
                    result.Add(key.Replace("strings-", ""), rp);
                }
            }
            return result;
        }

        public static void SetLanguage(string language) {
            if (Current == null) {
                return;
            }
            var languages = GetLanguages();
            foreach (var res in languages.Values) {
                Current.Resources.MergedDictionaries.Remove(res);
            }
            if (language != "en-US") {
                Current.Resources.MergedDictionaries.Add(languages["en-US"]);
            }
            if (languages.TryGetValue(language, out var res1)) {
                Current.Resources.MergedDictionaries.Add(res1);
            }
            UiFontManager.Apply();
        }

        static async void InitializeTheme() {
            Log.Information("Initializing theme.");
            try {
                CustomTheme.ListThemes();
                await OudepLoaderRegistry.LoadAllAsync();
            } catch (Exception e) {
                Log.Error(e, "Failed to load themes from packages.");
            }
            SetTheme();
            Log.Information("Initialized theme.");
        }

        /// <summary>
        /// Runs the package loaders again and re-applies the theme, after packages were installed, removed or edited.
        /// </summary>
        public static async void ReloadPackages() {
            try {
                await OudepLoaderRegistry.LoadAllAsync();
            } catch (Exception e) {
                Log.Error(e, "Failed to load themes from packages.");
            }
            SetTheme();
        }

        public static void SetTheme() {
            if (Current == null) {
                return;
            }
            var light = (IResourceDictionary) Current.Resources["themes-light"]!;
            var dark = (IResourceDictionary) Current.Resources["themes-dark"]!;
            var custom = (IResourceDictionary) Current.Resources["themes-custom"]!;
            switch (Core.Util.Preferences.Default.ThemeName) { 
                case "Light":
                    ApplyTheme(light);
                    Current.RequestedThemeVariant = ThemeVariant.Light;
                    break;
                case "Dark":
                    ApplyTheme(dark);
                    Current.RequestedThemeVariant = ThemeVariant.Dark;
                    break;
                default:
                    ApplyTheme(custom);
                    CustomTheme.ApplyTheme(Core.Util.Preferences.Default.ThemeName);
                    if (CustomTheme.Default.IsDarkMode == true) {
                        Current.RequestedThemeVariant = ThemeVariant.Dark;
                    } else {
                        Current.RequestedThemeVariant = ThemeVariant.Light;
                    }
                    break;
            }
            ThemeManager.LoadTheme();
        }

        private static void ApplyTheme(IResourceDictionary resDict) { 
            var res = Current?.Resources;
            foreach (var item in resDict) {
                res![item.Key] = item.Value;
            }
        }
    }
}
