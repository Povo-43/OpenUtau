using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OpenUtau.App.Views {
    /// <summary>
    /// A cross-platform replacement for <see cref="Window"/>.
    /// On desktop the view is hosted inside a real <see cref="Window"/> by
    /// <see cref="DialogHost"/>. On Android (single view) it is pushed onto an
    /// in-app overlay, which acts as a pseudo window.
    /// </summary>
    public class AppWindowBase : UserControl {
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<AppWindowBase, string?>(nameof(Title));

        public static readonly StyledProperty<WindowIcon?> IconProperty =
            AvaloniaProperty.Register<AppWindowBase, WindowIcon?>(nameof(Icon));

        private WindowState storedWindowState = WindowState.Normal;
        private PixelPoint storedPosition;

        /// <summary>The desktop window that currently hosts this view, if any.</summary>
        public Window? HostWindow { get; internal set; }

        public string? Title {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public WindowIcon? Icon {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public WindowStartupLocation WindowStartupLocation { get; set; } = WindowStartupLocation.Manual;
        public SizeToContent SizeToContent { get; set; } = SizeToContent.Manual;
        public bool CanResize { get; set; } = true;
        public bool ShowInTaskbar { get; set; } = true;
        public bool Topmost { get; set; }
        public WindowDecorations WindowDecorations { get; set; } = WindowDecorations.Full;
        public bool ExtendClientAreaToDecorationsHint { get; set; }
        public double ExtendClientAreaTitleBarHeightHint { get; set; } = -1;

        public WindowState WindowState {
            get => HostWindow?.WindowState ?? storedWindowState;
            set {
                storedWindowState = value;
                if (HostWindow != null) {
                    HostWindow.WindowState = value;
                }
            }
        }

        public PixelPoint Position {
            get => HostWindow?.Position ?? storedPosition;
            set {
                storedPosition = value;
                if (HostWindow != null) {
                    HostWindow.Position = value;
                }
            }
        }

        public bool IsActive => HostWindow?.IsActive ?? true;
        public Size ClientSize => TopLevel.GetTopLevel(this)?.ClientSize ?? Bounds.Size;
        public Size? FrameSize => (TopLevel.GetTopLevel(this) as Window)?.FrameSize;
        public Screens? Screens => TopLevel.GetTopLevel(this)?.Screens;
        public IFocusManager? FocusManager => TopLevel.GetTopLevel(this)?.FocusManager;

        public event EventHandler? Opened;
        public event EventHandler? Closed;
        public event EventHandler? Deactivated;
        public event EventHandler<WindowClosingEventArgs>? Closing;

        public void Show() => DialogHost.Show(this, null);
        public void Show(Visual? owner) => DialogHost.Show(this, owner);
        public Task ShowDialog(Visual? owner) => DialogHost.ShowAsync(owner, this);
        public void Activate() => DialogHost.Activate(this);
        public void Close() => DialogHost.Close(this);
        public void Hide() => DialogHost.Hide(this);

        protected virtual void OnOpened(EventArgs e) {
            Opened?.Invoke(this, e);
        }

        protected virtual void OnClosing(WindowClosingEventArgs e) {
            Closing?.Invoke(this, e);
        }

        protected virtual void OnClosed(EventArgs e) {
            Closed?.Invoke(this, e);
        }

        internal void RaiseOpened() => OnOpened(EventArgs.Empty);
        internal void RaiseDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);
        internal void RaiseClosing(WindowClosingEventArgs e) => OnClosing(e);

        private bool closed;

        internal void RaiseClosed() {
            if (closed) {
                return;
            }
            closed = true;
            OnClosed(EventArgs.Empty);
        }

        internal WindowState StoredWindowState => storedWindowState;
        internal PixelPoint StoredPosition => storedPosition;
    }
}
