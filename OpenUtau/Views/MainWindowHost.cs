#if !ANDROID
using System;
using Avalonia;
using Avalonia.Controls;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    /// <summary>
    /// Desktop window that hosts the cross-platform <see cref="MainWindow"/>
    /// view. Android uses the view directly as the single-view root.
    /// </summary>
    public class MainWindowHost : Window {
        private readonly MainWindow mainWindow;

        public MainWindowHost(MainWindow mainWindow) {
            this.mainWindow = mainWindow;
            Title = "OpenUtau";
            mainWindow.HostWindow = this;
            Content = mainWindow;

            var prefs = Preferences.Default.MainWindowSize;
            Width = prefs.Width;
            Height = prefs.Height;
            if (prefs.TryGetPosition(out int x, out int y)) {
                Position = new PixelPoint(x, y);
            } else {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            WindowState = (WindowState)prefs.State;

            Closing += OnHostClosing;
        }

        private void OnHostClosing(object? sender, WindowClosingEventArgs e) {
            mainWindow.WindowClosing(sender, e);
            if (e.Cancel) {
                return;
            }
            Preferences.Default.MainWindowSize.Set(
                ClientSize.Width, ClientSize.Height,
                Position.X, Position.Y, (int)WindowState);
            Preferences.Save();
        }
    }
}
#endif
