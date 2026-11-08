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
                singleView.MainView = loadingView;

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
                stuckTimer.Tick += (_, _) => {
                    if (loadingView.Content != loadingPanel) {
                        stuckTimer.Stop();
                        return;
                    }
                    var elapsed = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                    statusText.IsVisible = true;
                    statusText.Text = $"Still working… {elapsed}s elapsed. " +
                        "If this does not finish, tap the button below and report the log.";
                    copyLogButton.IsVisible = elapsed >= 15;
                };
                stuckTimer.Start();
                void StopStuckTimer() => stuckTimer.Stop();

                var mainThread = Thread.CurrentThread;
                var mainScheduler = TaskScheduler.FromCurrentSynchronizationContext();
                Task.Run(() => AppStartup.InitializeCore(mainThread, mainScheduler,
                        // Show each startup step on screen; the last visible step
                        // identifies where startup is stuck on a device.
                        step => Dispatcher.UIThread.Post(() => loadingText.Text = step)))
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
                        try {
                            loadingText.Text = "Creating main window…";
                            var mainWindow = new Views.MainWindow();
                            singleView.MainView = mainWindow;
                            loadingText.Text = "Opening project…";
                            mainWindow.InitProject();
                            LoadingWindow.InitializeLoadingWindow();
                        } catch (Exception e) {
                            Log.Error(e, "Failed to create main window.");
                            ShowStartupError(loadingView, errorText, e);
                            singleView.MainView = loadingView;
                        }
                        StopStuckTimer();
                    }, mainScheduler);
            }
#endif

            base.OnFrameworkInitializationCompleted();
            UiFontManager.Apply();
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
