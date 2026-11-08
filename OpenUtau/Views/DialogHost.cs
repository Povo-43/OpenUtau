using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App.Views {
    /// <summary>
    /// Hosts <see cref="AppWindowBase"/> views. Desktop uses real windows,
    /// Android uses a stacked in-app overlay (pseudo windows).
    /// </summary>
    public static class DialogHost {
        private sealed class Entry {
            public Panel? Layer;
            public Window? Host;
            public TaskCompletionSource<bool>? Completion;
        }

        private static readonly Dictionary<AppWindowBase, Entry> entries = new();

        private static WindowClosingEventArgs CreateClosingArgs() {
            return (WindowClosingEventArgs)Activator.CreateInstance(
                typeof(WindowClosingEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object?[] { WindowCloseReason.WindowClosing, true },
                null)!;
        }

        /// <summary>Whether the given view is currently shown.</summary>
        public static bool IsOpen(AppWindowBase view) => entries.ContainsKey(view);

        /// <summary>Finds an already open pseudo window of the given type.</summary>
        public static T? Find<T>() where T : AppWindowBase {
            foreach (var view in entries.Keys) {
                if (view is T match) {
                    return match;
                }
            }
            return null;
        }

        /// <summary>
        /// Closes the topmost overlay, if any. Used by the Android back button.
        /// Returns true when an overlay was found and asked to close.
        /// </summary>
        public static bool CloseTopOverlay() {
            var host = MainWindow.Instance?.OverlayHost;
            if (host == null) {
                return false;
            }
            for (int i = host.Children.Count - 1; i >= 0; i--) {
                var child = host.Children[i];
                foreach (var pair in entries) {
                    if (ReferenceEquals(pair.Value.Layer, child)) {
                        pair.Key.Close();
                        return true;
                    }
                }
            }
            return false;
        }

        private static Window? FindOwnerWindow(Visual? owner) {
            if (owner != null && TopLevel.GetTopLevel(owner) is Window ownerWindow) {
                return ownerWindow;
            }
            if (MainWindow.Instance is { } main && TopLevel.GetTopLevel(main) is Window mainWindow) {
                return mainWindow;
            }
            return null;
        }

        private static Window CreateHostWindow(AppWindowBase view) {
            var host = new Window {
                Content = view,
                Title = view.Title ?? string.Empty,
                Icon = view.Icon,
                WindowStartupLocation = view.WindowStartupLocation,
                SizeToContent = view.SizeToContent,
                CanResize = view.CanResize,
                ShowInTaskbar = view.ShowInTaskbar,
                Topmost = view.Topmost,
            };
            if (!double.IsNaN(view.Width)) {
                host.Width = view.Width;
            }
            if (!double.IsNaN(view.Height)) {
                host.Height = view.Height;
            }
            if (!double.IsNaN(view.MinWidth)) {
                host.MinWidth = view.MinWidth;
            }
            if (!double.IsNaN(view.MinHeight)) {
                host.MinHeight = view.MinHeight;
            }
            if (!double.IsNaN(view.MaxWidth)) {
                host.MaxWidth = view.MaxWidth;
            }
            if (!double.IsNaN(view.MaxHeight)) {
                host.MaxHeight = view.MaxHeight;
            }
            if (view.Background != null) {
                host.Background = view.Background;
            }
            return host;
        }

        private static void AttachDesktop(AppWindowBase view, Window host) {
            var entry = new Entry { Host = host };
            entries[view] = entry;
            view.HostWindow = host;
            host.Closing += (_, e) => view.RaiseClosing(e);
            host.Closed += (_, _) => {
                view.HostWindow = null;
                entries.Remove(view);
                view.RaiseClosed();
            };
            view.RaiseOpened();
        }

        public static async Task ShowAsync(Visual? owner, AppWindowBase view) {
            var ownerWindow = FindOwnerWindow(owner);
            if (ownerWindow != null) {
                var host = CreateHostWindow(view);
                AttachDesktop(view, host);
                await host.ShowDialog(ownerWindow);
                return;
            }
            await ShowOverlayAsync(view);
        }

        public static void Show(AppWindowBase view, Visual? owner) {
            var ownerWindow = FindOwnerWindow(owner);
            if (ownerWindow != null) {
                var host = CreateHostWindow(view);
                AttachDesktop(view, host);
                host.Show(ownerWindow);
                return;
            }
            _ = ShowOverlayAsync(view);
        }

        public static void Activate(AppWindowBase view) {
            if (entries.TryGetValue(view, out var entry)) {
                if (entry.Host != null) {
                    entry.Host.Activate();
                } else if (entry.Layer?.Parent is Panel panel) {
                    panel.Children.Remove(entry.Layer);
                    panel.Children.Add(entry.Layer);
                }
            } else if (view.HostWindow != null) {
                view.HostWindow.Activate();
            }
        }

        public static void Hide(AppWindowBase view) {
            if (entries.TryGetValue(view, out var entry)) {
                if (entry.Host != null) {
                    entry.Host.Hide();
                } else if (entry.Layer != null) {
                    entry.Layer.IsVisible = false;
                }
            } else {
                view.IsVisible = false;
            }
        }

        public static void Close(AppWindowBase view) {
            if (!entries.TryGetValue(view, out var entry)) {
                view.RaiseClosed();
                return;
            }
            if (entry.Host != null) {
                // The host raises Closing/Closed, which are forwarded to the view.
                entry.Host.Close();
                return;
            }
            var args = CreateClosingArgs();
            view.RaiseClosing(args);
            if (args.Cancel) {
                return;
            }
            entries.Remove(view);
            view.RaiseClosed();
            if (entry.Layer?.Parent is Panel panel) {
                panel.Children.Remove(entry.Layer);
            }
            entry.Completion?.TrySetResult(true);
        }

        private static Task ShowOverlayAsync(AppWindowBase view) {
            var main = MainWindow.Instance;
            var overlayHost = main?.OverlayHost;
            if (overlayHost == null) {
                // No overlay available yet; nothing sensible to show.
                return Task.CompletedTask;
            }

            if (entries.TryGetValue(view, out var existing) && existing.Layer != null) {
                existing.Layer.IsVisible = true;
                if (existing.Layer.Parent is Panel oldParent) {
                    oldParent.Children.Remove(existing.Layer);
                }
                overlayHost.Children.Add(existing.Layer);
                view.RaiseOpened();
                return existing.Completion?.Task ?? Task.CompletedTask;
            }

            var completion = new TaskCompletionSource<bool>();
            var entry = new Entry { Completion = completion };
            entries[view] = entry;

            var layer = BuildOverlay(view, main!);
            entry.Layer = layer;
            overlayHost.Children.Add(layer);
            view.RaiseOpened();
            return completion.Task;
        }

        private static Panel BuildOverlay(AppWindowBase view, MainWindow main) {
            // Diagnose sizing on devices where reported bounds and the visible
            // surface disagree.
            var topLevel = TopLevel.GetTopLevel(main);
            double topLevelWidth = topLevel?.ClientSize.Width ?? 0;
            double topLevelHeight = topLevel?.ClientSize.Height ?? 0;
            double scaling = topLevel?.RenderScaling ?? 1;
            Log.Information(
                "Overlay sizing: main bounds {MainW:F0}x{MainH:F0}, top level client {TopW:F0}x{TopH:F0}, scaling {Scaling:F2}, view min {MinW}x{MinH}, fixed {FixW}x{FixH}",
                main.Bounds.Width, main.Bounds.Height, topLevelWidth, topLevelHeight, scaling,
                view.MinWidth, view.MinHeight, view.Width, view.Height);

            bool autoSize = view.SizeToContent != SizeToContent.Manual;
            double? fixedWidth = double.IsNaN(view.Width) ? null : view.Width;
            double? fixedHeight = double.IsNaN(view.Height) ? null : view.Height;
            bool hasFixedSize = fixedWidth.HasValue && fixedHeight.HasValue;

            // The visible surface is the real limit; the main control itself may
            // report a bigger size when it overflows the screen by the insets.
            double availableWidth = Math.Max(240,
                Math.Min(main.Bounds.Width - 24,
                    topLevelWidth > 0 ? topLevelWidth - 24 : double.MaxValue));
            double availableHeight = Math.Max(160,
                Math.Min(main.Bounds.Height - 24,
                    topLevelHeight > 0 ? topLevelHeight - 24 : double.MaxValue));

            // The frame controls the size; let the view fill it.
            view.Width = double.NaN;
            view.Height = double.NaN;
            view.HorizontalAlignment = HorizontalAlignment.Stretch;
            view.VerticalAlignment = VerticalAlignment.Stretch;

            if (view.Background == null && Application.Current != null) {
                view.Background = Application.Current.FindResource("SystemControlBackgroundAltHighBrush") as IBrush;
            }

            var title = new TextBlock {
                Text = view.Title ?? string.Empty,
                FontSize = 16,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var close = new Button {
                Content = "✕",
                Width = 44,
                Height = 40,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            close.Click += (_, _) => view.Close();
            var titleBar = new Grid {
                Height = 44,
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };
            titleBar.Children.Add(title);
            Grid.SetColumn(close, 1);
            titleBar.Children.Add(close);

            var titleBorder = new Border {
                Background = new SolidColorBrush(Color.Parse("#22000000")),
                Child = titleBar,
            };

            // On Android, dialogs designed for desktop (min sizes larger than the
            // phone screen) become a near-full-screen frame one-to-one (no
            // shrinking), with scrollable content so every control stays at its
            // natural size and reachable. Small dialogs stay centered.
            bool android = OperatingSystem.IsAndroid();
            bool fullFrame = android &&
                (!hasFixedSize || fixedWidth!.Value > availableWidth || fixedHeight!.Value > availableHeight);

            // The content scrolls when it does not fit, so nothing is scaled down
            // and nothing outside the frame is lost.
            var content = new ContentControl {
                Content = view,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            var scroll = new ScrollViewer {
                Content = content,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            };

            var dock = new DockPanel();
            DockPanel.SetDock(titleBorder, Dock.Top);
            dock.Children.Add(titleBorder);
            dock.Children.Add(scroll);

            var frame = new Border {
                Child = dock,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Background = view.Background ?? Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.Parse("#33000000")),
                BorderThickness = new Thickness(1),
            };

            var layer = new Panel {
                Background = new SolidColorBrush(Color.Parse("#66000000")),
            };

            frame.MaxWidth = availableWidth;
            frame.MaxHeight = availableHeight;

            if (fullFrame) {
                // Near-full-screen: use the whole available surface.
                frame.HorizontalAlignment = HorizontalAlignment.Center;
                frame.VerticalAlignment = VerticalAlignment.Center;
                frame.Margin = new Thickness(4);
                frame.Width = availableWidth;
                frame.Height = availableHeight;
                layer.Children.Add(frame);
            } else if (hasFixedSize || autoSize) {
                frame.HorizontalAlignment = HorizontalAlignment.Center;
                frame.VerticalAlignment = VerticalAlignment.Center;
                frame.Margin = new Thickness(12);
                if (hasFixedSize) {
                    frame.Width = Math.Min(fixedWidth!.Value, availableWidth);
                    frame.Height = Math.Min(fixedHeight!.Value, availableHeight);
                } else {
                    // Auto-sized; the scroll viewer keeps a big content within
                    // the frame instead of clipping it.
                }
                layer.Children.Add(frame);
            } else {
                frame.Margin = new Thickness(8);
                layer.Children.Add(frame);
            }
            return layer;
        }
    }
}
