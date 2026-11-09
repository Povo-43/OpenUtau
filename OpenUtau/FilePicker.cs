using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Util;
#if MACOS
using MonoMac.Foundation;
using MonoMac.AppKit;
using Serilog;
#endif
#if ANDROID
using Android.App;
using Android.Content;
#endif

namespace OpenUtau.App {
    internal class FilePicker {
        #if MACOS
        private static int NSAppInitState;
        #endif
        public static FilePickerFileType ProjectFiles { get; } = new("Project Files") {
            Patterns = new[] { "*.ustx", "*.vsqx", "*.ust", "*.mid", "*.midi", "*.ufdata", "*.musicxml", "*.svp" },
        };
        public static FilePickerFileType USTX { get; } = new("USTX") {
            Patterns = new[] { "*.ustx" },
        };
        public static FilePickerFileType VSQX { get; } = new("VSQX") {
            Patterns = new[] { "*.vsqx" },
        };
        public static FilePickerFileType UST { get; } = new("UST") {
            Patterns = new[] { "*.ust" },
        };
        public static FilePickerFileType MIDI { get; } = new("MIDI") {
            Patterns = new[] { "*.mid", "*.midi" },
        };
        public static FilePickerFileType UFDATA { get; } = new("UFDATA") {
            Patterns = new[] { "*.ufdata" },
        };
        public static FilePickerFileType MUSICXML { get; } = new("MUSICXML") {
            Patterns = new[] { "*.musicxml" },
        };
        public static FilePickerFileType SVP { get; } = new("SVP") {
            Patterns = new[] { "*.svp" },
        };
        public static FilePickerFileType AudioFiles { get; } = new("Audio Files") {
            Patterns = new[] { "*.wav", "*.mp3", "*.ogg", "*.opus", "*.flac", "*.m4a" },
        };
        public static FilePickerFileType FontFiles { get; } = new("Font Files") {
            Patterns = new[] { "*.ttf", "*.otf" },
        };
        public static FilePickerFileType WAV { get; } = new("WAV") {
            Patterns = new[] { "*.wav" },
        };
        public static FilePickerFileType ArchiveFiles { get; } = new("Archive File") {
            Patterns = new[] { "*.zip", "*.rar", "*.uar", "*.vogeon", "*.oudep" },
        };
        public static FilePickerFileType ZIP { get; } = new("ZIP") {
            Patterns = new[] { "*.zip" },
        };
        public static FilePickerFileType EXE { get; } = new("EXE") {
            Patterns = new[] { "*.exe" },
        };
        public static FilePickerFileType APP { get; } = new("APP") {
            AppleUniformTypeIdentifiers = new[] { 
                "com.apple.application-bundle", 
                "com.apple.package",
                "public.executable" 
            },
            Patterns = new[] { "*.app" },
        };
        public static FilePickerFileType PrefixMap { get; } = new("Prefix Map") {
            Patterns = new[] { "*.map" },
        };
        public static FilePickerFileType DS { get; } = new("DS") {
            Patterns = new[] { "*.ds" },
        };
        public static FilePickerFileType ExpressionGraph { get; } = new("Expression Graph") {
            Patterns = new[] { "*.ougraph" },
        };
        public static FilePickerFileType OUDEP { get; } = new("OpenUtau dependency") {
            Patterns = new[] { "*.oudep" },
        };
        public static FilePickerFileType UnixExecutable { get; } = new("Executable") {
            MimeTypes = new[] { "application/x-executable" },
            AppleUniformTypeIdentifiers = new[] { "public.unix-executable" },
        };

        public async static Task<string?> OpenFile(
            Visual? visual, string titleKey, params FilePickerFileType[] types) {
            return await OpenFile(visual, titleKey, null, types);
        }

        public async static Task<string?> OpenFileAboutSinger(
            Visual? visual, string titleKey, params FilePickerFileType[] types) {
            var path = await OpenFile(visual, titleKey, Preferences.Default.RecentOpenSingerDirectory, types);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) {
                Preferences.Default.RecentOpenSingerDirectory = dir;
                Preferences.Save();
            }
            return path;
        }

        public async static Task<string?> OpenFileAboutProject(
            Visual? visual, string titleKey, params FilePickerFileType[] types) {
            var path = await OpenFile(visual, titleKey, Preferences.Default.RecentOpenProjectDirectory, types);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) {
                Preferences.Default.RecentOpenProjectDirectory = dir;
                Preferences.Save();
            }
            return path;
        }

        public async static Task<string?> OpenFile(
            Visual? visual, string titleKey, string? startLocation, params FilePickerFileType[] types) {
            var location = startLocation == null
                ? null
                : await TopLevel.GetTopLevel(visual)!.StorageProvider.TryGetFolderFromPathAsync(startLocation);
        #if MACOS
            // Due to an avalonia bug, we need to call the native API for looking for APP
            if (types.Contains(APP)) {
                var tcs = new TaskCompletionSource<string?>();
                var title = ThemeManager.GetString(titleKey);
                if (NSAppInitState.Equals(0)) {
                    NSAppInitState = 1;
                    NSApplication.Init();
                }

                NSApplication.SharedApplication.InvokeOnMainThread(() => {
                    try {
                        var panel = new NSOpenPanel();
                        panel.Title = title;
                        panel.CanChooseFiles = true;
                        panel.CanChooseDirectories = false;
                        panel.AllowsMultipleSelection = false;
                        panel.TreatsFilePackagesAsDirectories = false;
                        
                        if (!string.IsNullOrEmpty(startLocation)) {
                            panel.DirectoryUrl = NSUrl.FromFilename(startLocation);
                        }
                        
                        var result = panel.RunModal();
                        if (result == 1) {
                            tcs.SetResult(panel.Urls.FirstOrDefault()?.Path);
                        } else {
                            tcs.SetResult(null);
                        }
                    } catch (Exception ex) {
                        Log.Error($"Failed to open native macOS file manager: {ex}");
                        tcs.SetException(ex);
                    }
                });

                return await tcs.Task;
            }
        #endif

#if ANDROID
            return await OpenFileAndroid(visual, titleKey, startLocation, types);
#else
            var files = await TopLevel.GetTopLevel(visual)!.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions() {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = false,
                    FileTypeFilter = types,
                    SuggestedStartLocation = location,
                });
            return files
                ?.Select(f => f.TryGetLocalPath())
                ?.OfType<string>()
                ?.FirstOrDefault();
#endif
        }

#if ANDROID
        private static async Task<string?> OpenFileAndroid(
            Visual? visual, string titleKey, string? startLocation, params FilePickerFileType[] types) {
            var location = startLocation == null
                ? null
                : await TopLevel.GetTopLevel(visual)!.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var files = await TopLevel.GetTopLevel(visual)!.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions() {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = false,
                    FileTypeFilter = types,
                    SuggestedStartLocation = location,
                });
            if (files == null || files.Count == 0) return null;
            var f = files[0];
            var localPath = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath)) return localPath;
            // Android SAF: TryGetLocalPath() returns null for content:// URIs,
            // so read the stream and copy to a temp file.
            using var stream = await f.OpenReadAsync();
            var tempName = f.Name ?? "file";
            var tempPath = Path.Combine(Path.GetTempPath(), tempName);
            using var fileStream = File.Create(tempPath);
            await stream.CopyToAsync(fileStream);
            return tempPath;
        }
#endif

        public async static Task<string[]?> OpenFilesAboutProject(
                Visual? visual, string titleKey, params FilePickerFileType[] types) {
            var result = await OpenFiles(visual, titleKey, Preferences.Default.RecentOpenProjectDirectory, types);
            if (result != null) {
                var dir = Path.GetDirectoryName(result.FirstOrDefault());
                if (dir != null) {
                    Preferences.Default.RecentOpenProjectDirectory = dir;
                    Preferences.Save();
                }
            }
            return result;
        }

        public async static Task<string[]?> OpenFiles(
            Visual? visual, string titleKey, string? startLocation, params FilePickerFileType[] types) {
#if ANDROID
            return await OpenFilesAndroid(visual, titleKey, startLocation, types);
#else
            var location = startLocation == null
                ? null
                : await TopLevel.GetTopLevel(visual)!.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var files = await TopLevel.GetTopLevel(visual)!.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions() {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = true,
                    FileTypeFilter = types,
                    SuggestedStartLocation = location
                });
            return files
                ?.Select(f => f.TryGetLocalPath())
                ?.OfType<string>()
                ?.ToArray();
#endif
        }

#if ANDROID
        private static async Task<string[]?> OpenFilesAndroid(
            Visual? visual, string titleKey, string? startLocation, params FilePickerFileType[] types) {
            var location = startLocation == null
                ? null
                : await TopLevel.GetTopLevel(visual)!.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var files = await TopLevel.GetTopLevel(visual)!.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions() {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = true,
                    FileTypeFilter = types,
                    SuggestedStartLocation = location
                });
            if (files == null || files.Count == 0) return null;
            var paths = new List<string>();
            foreach (var f in files) {
                var localPath = f.TryGetLocalPath();
                if (!string.IsNullOrEmpty(localPath)) {
                    paths.Add(localPath);
                    continue;
                }
                using var stream = await f.OpenReadAsync();
                var tempName = f.Name ?? "file";
                var tempPath = Path.Combine(Path.GetTempPath(), tempName);
                using var fileStream = File.Create(tempPath);
                await stream.CopyToAsync(fileStream);
                paths.Add(tempPath);
            }
            return paths.ToArray();
        }
#endif

        public async static Task<string?> OpenFolderAboutSinger(Visual? visual, string titleKey) {
            var dir = await OpenFolder(visual, titleKey, Preferences.Default.RecentOpenSingerDirectory);
            if (dir != null) {
                Preferences.Default.RecentOpenSingerDirectory = dir;
                Preferences.Save();
            }
            return dir;
        }

        public async static Task<string?> OpenFolder(Visual? visual, string titleKey, string? startLocation) {
            var location = startLocation == null
                ? null
                : await TopLevel.GetTopLevel(visual)!.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var dirs = await TopLevel.GetTopLevel(visual)!.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = false,
                    SuggestedStartLocation = location
                });
            return dirs
                ?.Select(f => f.TryGetLocalPath())
                ?.OfType<string>()
                ?.FirstOrDefault();
        }

        public async static Task<string?> SaveFile
            (Visual? visual, string titleKey, params FilePickerFileType[] types) {
            return await SaveFile(visual, titleKey, null, null, types);
        }

        public async static Task<string?> SaveFileAboutProject
            (Visual? visual, string titleKey, params FilePickerFileType[] types) {
            var path = await SaveFile(visual, titleKey, Preferences.Default.RecentOpenProjectDirectory, 
                Path.GetFileName(Path.ChangeExtension(DocManager.Inst.Project.FilePath, null)), types);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) {
                Preferences.Default.RecentOpenProjectDirectory = dir;
                Preferences.Save();
            }
            return path;
        }

        public async static Task<string?> SaveFile
            (Visual? visual, string titleKey, string? startLocation, string? filename, params FilePickerFileType[] types) {
#if ANDROID
            return await SaveFileAndroid(visual, titleKey, startLocation, filename, types);
#else
            var location = startLocation == null
                ? null
                : await TopLevel.GetTopLevel(visual)!.StorageProvider.TryGetFolderFromPathAsync(startLocation);

            var file = await TopLevel.GetTopLevel(visual)!.StorageProvider.SaveFilePickerAsync(
                 new FilePickerSaveOptions {
                     Title = ThemeManager.GetString(titleKey),
                     FileTypeChoices = types,
                     ShowOverwritePrompt = true,
                     SuggestedStartLocation = location,
                     SuggestedFileName = filename,
                 });
            return file?.TryGetLocalPath();
#endif
        }

#if ANDROID
        private static async Task<string?> SaveFileAndroid(
            Visual? visual, string titleKey, string? startLocation, string? filename, params FilePickerFileType[] types) {
            var location = startLocation == null
                ? null
                : await TopLevel.GetTopLevel(visual)!.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var file = await TopLevel.GetTopLevel(visual)!.StorageProvider.SaveFilePickerAsync(
                 new FilePickerSaveOptions {
                     Title = ThemeManager.GetString(titleKey),
                     FileTypeChoices = types,
                     ShowOverwritePrompt = true,
                     SuggestedStartLocation = location,
                     SuggestedFileName = filename,
                 });
            if (file == null) return null;
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath)) return localPath;
            // Android SAF: TryGetLocalPath() returns null for content:// URIs.
            // The caller renders into a temp file and then copies it to the SAF
            // stream; do not write an empty placeholder here, or the destination
            // ends up as a 0-byte file.
            // Place the temp render target on external storage so the exported
            // file is reachable from a PC over USB/MTP, not buried in app-private
            // internal storage where normal file managers cannot reach it.
            string tempDir = Path.GetTempPath();
#if ANDROID
            var extDir = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
            if (!string.IsNullOrEmpty(extDir)) {
                tempDir = extDir;
            }
#endif
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, filename ?? "openutau-temp");
            File.Create(tempPath).Dispose();
            return tempPath;
        }
#endif
    }
}
