using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Data.Sqlite;

namespace VideoDB
{
    public partial class MainWindow : Window
    {
        private const int CaptureHotkeyId = 9001;
        private const int SceneCountHotkeyId = 9002;
        private const int WmHotkey = 0x0312;
        private const uint ModNoRepeat = 0x4000;
        private const uint VkF8 = 0x77;
        private const uint VkF9 = 0x78;
        private const string DatabasePath = "Data Source=video.db";
        private const string ScanRootPath = @"D:\_AVDB\Fake_Dir";
        private const string DefaultMpcPath = @"C:\Program Files (x86)\K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe";
        private const string DefaultVlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe";

        private readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(2) };
        private readonly SemaphoreSlim playbackControlLock = new SemaphoreSlim(1, 1);
        private readonly List<SceneItem> allScenes = new List<SceneItem>();
        private readonly Dictionary<string, bool> metadataWritePendingByPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private HwndSource? hwndSource;
        private bool isCapturingFromMpc;
        private bool isCheckingMpcSceneCount;
        private bool isSceneCountButtonHeld;
        private int sceneCountPopupRequestId;
        private SceneCountPopupWindow? sceneCountPopupWindow;
        private int? rememberedPlaybackPositionSeconds;
        private MediaPlayerKind lastActiveMediaPlayer = MediaPlayerKind.Mpc;
        private bool allScenesFullyLoaded;
        private string? activeTag;
        private string? activeActress;
        private int? activeRatingFilter;
        private string sceneSortMode = "Rating ↓";
        private string currentTheme = "Dark";

        public MainWindow()
        {
            App.LogStartup("MainWindow constructor started");
            InitializeComponent();
            App.LogStartup("MainWindow XAML initialized");
            ConfigureInitialWindowSize();
            App.LogStartup("Initial window size configured");
            InitializeDatabase();
            App.LogStartup("Database initialized: " + Path.GetFullPath("video.db"));
            ApplyTheme(LoadSetting("Theme", "Dark"));
            App.LogStartup("Theme applied");
            RestoreLayoutColumnWidths();
            RefreshExplorerWithoutScenesOnStartup();
            App.LogStartup("MainWindow initialization completed");
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public class VideoItem
        {
            public string ID { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public int Rating { get; set; }
            public string Subtitle { get; set; } = string.Empty;
            public int Priority { get; set; }
            public bool FileExists { get; set; }
            public bool MetadataWritePending { get; set; }
        }

        public class SceneItem
        {
            public int EventID { get; set; }

            public string ThumbnailPath =>
                System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "thumbnails",
                    EventID + ".webp");

            public string VideoID { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
            public List<string> Tags { get; set; } = new List<string>();
            public List<string> Actresses { get; set; } = new List<string>();
            public int Rating { get; set; }

            //public string Star1 => Rating >= 1 ? "★" : "☆";
            public string Star1 => Rating >= 1 ? "★" : ".";
            public string Star2 => Rating >= 2 ? "★" : ".";
            public string Star3 => Rating >= 3 ? "★" : ".";
            public string Star4 => Rating >= 4 ? "★" : ".";
            public string Star5 => Rating >= 5 ? "★" : ".";
            public string Note { get; set; } = string.Empty;
            public bool FileExists { get; set; }
            public string MetadataID { get; set; } = string.Empty;
            public bool MetadataWritePending { get; set; }
        }

        public class UsageItem
        {
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
            public bool IsActive { get; set; }
        }

        public class ActiveFilterItem
        {
            public string Type { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Label => Type + ": " + Name;
        }

        public class RatingFilterItem
        {
            public string Label { get; set; } = string.Empty;
            public int? Rating { get; set; }
            public bool IsActive { get; set; }
        }

        private class MpcStatus
        {
            public MediaPlayerKind Player { get; set; }
            public string Path { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
            public int PositionSeconds { get; set; }
            public int DurationSeconds { get; set; }
            public bool IsPlaying { get; set; }
        }

        // Keep player-specific commands behind this boundary so VLC can be added later.
        private enum MediaPlayerKind
        {
            Auto,
            Mpc,
            Vlc
        }

        private class MetadataWriteResult
        {
            public int CheckedCount { get; set; }
            public int WrittenCount { get; set; }
            public int SkippedCount { get; set; }
        }

        private class MetadataSyncTarget
        {
            public string ID { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public bool MetadataWritePending { get; set; }
        }

        private class SceneCountLookupResult
        {
            public bool VideoFound { get; set; }
            public string VideoID { get; set; } = string.Empty;
            public int SceneCount { get; set; }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr handle = new WindowInteropHelper(this).Handle;
            hwndSource = HwndSource.FromHwnd(handle);
            hwndSource?.AddHook(WndProc);

            if (!RegisterHotKey(handle, CaptureHotkeyId, ModNoRepeat, VkF8))
            {
                StatusTextBlock.Text = "F8 hotkey registration failed";
            }

            if (!RegisterHotKey(handle, SceneCountHotkeyId, ModNoRepeat, VkF9))
            {
                StatusTextBlock.Text = "F9 popup hotkey registration failed";
            }
        }

        private void ConfigureInitialWindowSize()
        {
            Rect workArea = SystemParameters.WorkArea;

            Width = Math.Max(MinWidth, workArea.Width * 0.85);
            Height = Math.Max(MinHeight, workArea.Height * 0.85);
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;
        }

        private void RestoreLayoutColumnWidths()
        {
            double tagsWidth = LoadDoubleSetting("Layout.TagsWidth", 0);
            double scenesWidth = LoadDoubleSetting("Layout.ScenesWidth", 0);
            double actressesWidth = LoadDoubleSetting("Layout.ActressesWidth", 0);

            if (tagsWidth < TagsColumn.MinWidth ||
                scenesWidth < ScenesColumn.MinWidth ||
                actressesWidth < ActressesColumn.MinWidth)
            {
                return;
            }

            double totalWidth = tagsWidth + scenesWidth + actressesWidth;

            if (totalWidth > SystemParameters.WorkArea.Width * 0.95)
                return;

            TagsColumn.Width = new GridLength(tagsWidth);
            ScenesColumn.Width = new GridLength(scenesWidth);
            ActressesColumn.Width = new GridLength(actressesWidth);
        }

        private void SaveLayoutColumnWidths()
        {
            SaveSetting("Layout.TagsWidth", TagsColumn.ActualWidth.ToString(CultureInfo.InvariantCulture));
            SaveSetting("Layout.ScenesWidth", ScenesColumn.ActualWidth.ToString(CultureInfo.InvariantCulture));
            SaveSetting("Layout.ActressesWidth", ActressesColumn.ActualWidth.ToString(CultureInfo.InvariantCulture));
        }

        private double LoadDoubleSetting(string key, double defaultValue)
        {
            string value = LoadSetting(key, defaultValue.ToString(CultureInfo.InvariantCulture));

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                return result;

            return defaultValue;
        }

        private void ApplyTheme(string theme)
        {
            currentTheme =
                string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                    ? "Light"
                    : "Dark";

            if (currentTheme == "Light")
            {
                SetBrush("AppBackgroundBrush", "#F3F3F3");
                SetBrush("PanelBackgroundBrush", "#FFFFFF");
                SetBrush("ControlBackgroundBrush", "#E8E8E8");
                SetBrush("ControlHoverBrush", "#DADADA");
                SetBrush("BorderBrush", "#C8C8C8");
                SetBrush("TextBrush", "#1F1F1F");
                SetBrush("MutedTextBrush", "#666666");
                SetBrush("SelectionBrush", "#007ACC");
                SetBrush("SelectionTextBrush", "#FFFFFF");
                SetBrush("InputBackgroundBrush", "#FFFFFF");
                SetSystemBrushes("#FFFFFF", "#1F1F1F", "#E8E8E8", "#1F1F1F");
            }
            else
            {
                SetBrush("AppBackgroundBrush", "#1E1E1E");
                SetBrush("PanelBackgroundBrush", "#252526");
                SetBrush("ControlBackgroundBrush", "#2D2D30");
                SetBrush("ControlHoverBrush", "#3A3D41");
                SetBrush("BorderBrush", "#3A3A3A");
                SetBrush("TextBrush", "#EAEAEA");
                SetBrush("MutedTextBrush", "#A8A8A8");
                SetBrush("SelectionBrush", "#007ACC");
                SetBrush("SelectionTextBrush", "#FFFFFF");
                SetBrush("InputBackgroundBrush", "#1F1F1F");
                SetSystemBrushes("#1F1F1F", "#EAEAEA", "#2D2D30", "#EAEAEA");
            }

        }

        private void SetBrush(string key, string color)
        {
            Application.Current.Resources[key] =
                new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        }

        private void SetSystemBrushes(string window, string windowText, string control, string controlText)
        {
            Application.Current.Resources[SystemColors.WindowBrushKey] = CreateBrush(window);
            Application.Current.Resources[SystemColors.WindowTextBrushKey] = CreateBrush(windowText);
            Application.Current.Resources[SystemColors.ControlBrushKey] = CreateBrush(control);
            Application.Current.Resources[SystemColors.ControlTextBrushKey] = CreateBrush(controlText);
            Application.Current.Resources[SystemColors.HighlightBrushKey] = CreateBrush("#007ACC");
            Application.Current.Resources[SystemColors.HighlightTextBrushKey] = CreateBrush("#FFFFFF");
        }

        private System.Windows.Media.SolidColorBrush CreateBrush(string color)
        {
            return new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        }

        protected override void OnClosed(EventArgs e)
        {
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(WndProc);
            }

            IntPtr handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, CaptureHotkeyId);
            UnregisterHotKey(handle, SceneCountHotkeyId);
            SaveSceneCountPopupPosition();
            sceneCountPopupWindow?.Close();
            SaveLayoutColumnWidths();
            httpClient.Dispose();
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam.ToInt32() == CaptureHotkeyId)
            {
                handled = true;
                _ = CaptureEventFromMpcAsync();
            }
            else if (msg == WmHotkey && wParam.ToInt32() == SceneCountHotkeyId)
            {
                handled = true;
                ShowSceneCountToolPopup();
            }

            return IntPtr.Zero;
        }

        private void ShowSceneCountToolPopup()
        {
            SceneCountPopupWindow popup = EnsureSceneCountPopupWindow();

            if (!popup.IsVisible)
            {
                popup.SetIdleText();
                popup.Show();
            }

            popup.Topmost = false;
            popup.Topmost = true;
        }

        private async Task ShowMpcSceneCountInToolPopupAsync()
        {
            isSceneCountButtonHeld = true;
            int requestId = ++sceneCountPopupRequestId;
            ShowSceneCountPopup("...", "Checking MPC");

            if (isCheckingMpcSceneCount)
                return;

            isCheckingMpcSceneCount = true;

            try
            {
                MpcStatus? status = await GetActiveMediaPlayerStatusAsync();

                if (!isSceneCountButtonHeld || requestId != sceneCountPopupRequestId)
                    return;

                if (status == null || string.IsNullOrWhiteSpace(status.Path))
                {
                    ShowSceneCountPopup("?", "Player status unavailable");
                    return;
                }

                SceneCountLookupResult result = GetRecordedSceneCountForMpcPath(status.Path);
                string detail =
                    result.VideoFound
                        ? result.VideoID
                        : "No DB video record";

                ShowSceneCountPopup(result.SceneCount.ToString(CultureInfo.InvariantCulture), detail);
            }
            catch (HttpRequestException)
            {
                if (isSceneCountButtonHeld && requestId == sceneCountPopupRequestId)
                    ShowSceneCountPopup("!", "Player interface unreachable");
            }
            catch (TaskCanceledException)
            {
                if (isSceneCountButtonHeld && requestId == sceneCountPopupRequestId)
                    ShowSceneCountPopup("!", "Player did not respond");
            }
            catch (JsonException)
            {
                if (isSceneCountButtonHeld && requestId == sceneCountPopupRequestId)
                    ShowSceneCountPopup("!", "VLC response could not be parsed");
            }
            catch (InvalidOperationException ex)
            {
                if (isSceneCountButtonHeld && requestId == sceneCountPopupRequestId)
                    ShowSceneCountPopup("!", ex.Message);
            }
            finally
            {
                isCheckingMpcSceneCount = false;
            }
        }

        private void ShowSceneCountPopup(string countText, string detailText)
        {
            SceneCountPopupWindow popup = EnsureSceneCountPopupWindow();
            popup.SetText(countText, detailText);

            if (!popup.IsVisible)
                popup.Show();

            popup.Topmost = false;
            popup.Topmost = true;
        }

        private void HideSceneCountPopup()
        {
            isSceneCountButtonHeld = false;
            sceneCountPopupRequestId++;
            SaveSceneCountPopupPosition();
            sceneCountPopupWindow?.Hide();
        }

        private void ResetSceneCountPopupText()
        {
            isSceneCountButtonHeld = false;
            sceneCountPopupRequestId++;
            sceneCountPopupWindow?.SetIdleText();
        }

        private SceneCountPopupWindow EnsureSceneCountPopupWindow()
        {
            if (sceneCountPopupWindow != null)
                return sceneCountPopupWindow;

            sceneCountPopupWindow =
                new SceneCountPopupWindow()
                {
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

            sceneCountPopupWindow.CaptureRequested += SceneCountPopup_CaptureRequested;
            sceneCountPopupWindow.SceneCountPressed += SceneCountPopup_SceneCountPressed;
            sceneCountPopupWindow.SceneCountReleased += SceneCountPopup_SceneCountReleased;
            sceneCountPopupWindow.RememberPositionRequested += SceneCountPopup_RememberPositionRequested;
            sceneCountPopupWindow.ReturnToPositionRequested += SceneCountPopup_ReturnToPositionRequested;
            sceneCountPopupWindow.SeekRequested += SceneCountPopup_SeekRequested;
            sceneCountPopupWindow.CloseRequested += SceneCountPopup_CloseRequested;
            RefreshRememberedPositionText();
            RestoreSceneCountPopupPosition(sceneCountPopupWindow);
            return sceneCountPopupWindow;
        }

        private void SceneCountPopup_CaptureRequested(object? sender, EventArgs e)
        {
            _ = CaptureEventFromMpcAsync();
        }

        private void SceneCountPopup_SceneCountPressed(object? sender, EventArgs e)
        {
            _ = ShowMpcSceneCountInToolPopupAsync();
        }

        private void SceneCountPopup_SceneCountReleased(object? sender, EventArgs e)
        {
            ResetSceneCountPopupText();
        }

        private void SceneCountPopup_CloseRequested(object? sender, EventArgs e)
        {
            HideSceneCountPopup();
        }

        private void SceneCountPopup_RememberPositionRequested(object? sender, EventArgs e)
        {
            _ = RememberPlaybackPositionAsync();
        }

        private void SceneCountPopup_ReturnToPositionRequested(object? sender, EventArgs e)
        {
            if (rememberedPlaybackPositionSeconds is not int seconds)
            {
                sceneCountPopupWindow?.SetPlaybackStatus("尚未記錄播放時間");
                return;
            }

            _ = SeekActiveMediaPlayerAsync(seconds);
        }

        private void SceneCountPopup_SeekRequested(object? sender, int seconds)
        {
            _ = SeekActiveMediaPlayerRelativeAsync(seconds);
        }

        private async Task RememberPlaybackPositionAsync()
        {
            await RunPlaybackCommandAsync(async () =>
            {
                MpcStatus? status = await GetActiveMediaPlayerStatusAsync();

                if (status == null)
                    throw new InvalidOperationException("無法取得 MPC 播放時間");

                rememberedPlaybackPositionSeconds = status.PositionSeconds;
                RefreshRememberedPositionText();
            });
        }

        private async Task SeekActiveMediaPlayerRelativeAsync(int offsetSeconds)
        {
            MediaPlayerKind configuredPlayer = GetConfiguredPlayerMode();

            if (configuredPlayer == MediaPlayerKind.Vlc ||
                configuredPlayer == MediaPlayerKind.Auto && lastActiveMediaPlayer == MediaPlayerKind.Vlc)
            {
                await RunPlaybackCommandAsync(() => SeekVlcRelativeAsync(offsetSeconds));
                return;
            }

            await RunPlaybackCommandAsync(async () =>
            {
                MpcStatus? status = await GetActiveMediaPlayerStatusAsync();

                if (status == null)
                    throw new InvalidOperationException("無法取得 MPC 播放時間");

                int targetSeconds = Math.Max(0, status.PositionSeconds + offsetSeconds);

                if (status.DurationSeconds > 0)
                    targetSeconds = Math.Min(targetSeconds, status.DurationSeconds);

                if (status.Player == MediaPlayerKind.Vlc)
                    await SeekVlcRelativeAsync(offsetSeconds);
                else
                    await SeekActiveMediaPlayerCoreAsync(targetSeconds);
            });
        }

        private async Task SeekActiveMediaPlayerAsync(int targetSeconds)
        {
            await RunPlaybackCommandAsync(() => SeekActiveMediaPlayerCoreAsync(Math.Max(0, targetSeconds)));
        }

        private Task<MpcStatus?> GetActiveMediaPlayerStatusAsync()
        {
            MediaPlayerKind configuredPlayer = GetConfiguredPlayerMode();

            if (configuredPlayer != MediaPlayerKind.Auto)
            {
                return GetMediaPlayerStatusAsync(configuredPlayer);
            }

            return GetAutomaticallyDetectedPlayerStatusAsync();
        }

        private async Task<MpcStatus?> GetAutomaticallyDetectedPlayerStatusAsync()
        {
            MpcStatus? vlcStatus = await TryGetMediaPlayerStatusAsync(MediaPlayerKind.Vlc);
            MpcStatus? mpcStatus = await TryGetMediaPlayerStatusAsync(MediaPlayerKind.Mpc);
            MpcStatus? selected =
                vlcStatus?.IsPlaying == true ? vlcStatus :
                mpcStatus?.IsPlaying == true ? mpcStatus :
                vlcStatus ?? mpcStatus;

            if (selected != null)
                lastActiveMediaPlayer = selected.Player;

            return selected;
        }

        private async Task<MpcStatus?> TryGetMediaPlayerStatusAsync(MediaPlayerKind player)
        {
            try
            {
                return await GetMediaPlayerStatusAsync(player);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                return null;
            }
        }

        private async Task<MpcStatus?> GetMediaPlayerStatusAsync(MediaPlayerKind player)
        {
            MpcStatus? status = player switch
            {
                MediaPlayerKind.Mpc => await GetMpcStatusAsync(),
                MediaPlayerKind.Vlc => await GetVlcStatusAsync(),
                _ => null
            };

            if (status != null)
                lastActiveMediaPlayer = status.Player;

            return status;
        }

        private Task SeekActiveMediaPlayerCoreAsync(int targetSeconds)
        {
            MediaPlayerKind player = GetConfiguredPlayerMode();

            if (player == MediaPlayerKind.Auto)
                player = lastActiveMediaPlayer;

            return player switch
            {
                MediaPlayerKind.Mpc => SeekMpcAsync(targetSeconds),
                MediaPlayerKind.Vlc => SeekVlcAsync(targetSeconds),
                _ => throw new NotSupportedException()
            };
        }

        private async Task SeekMpcAsync(int targetSeconds)
        {
            string position = FormatPlaybackPosition(targetSeconds);
            string url = "http://127.0.0.1:13579/command.html?wm_command=-1&position=" +
                         Uri.EscapeDataString(position);
            using HttpResponseMessage response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            sceneCountPopupWindow?.SetPlaybackStatus("MPC → " + FormatPlaybackPositionChinese(targetSeconds));
        }

        private async Task SeekVlcAsync(int targetSeconds)
        {
            string query = "command=seek&val=" + targetSeconds.ToString(CultureInfo.InvariantCulture);
            using JsonDocument ignored = await GetVlcJsonAsync("/requests/status.json", query);
            sceneCountPopupWindow?.SetPlaybackStatus("VLC → " + FormatPlaybackPositionChinese(targetSeconds));
        }

        private async Task SeekVlcRelativeAsync(int offsetSeconds)
        {
            string relativeValue = (offsetSeconds >= 0 ? "+" : string.Empty) +
                                   offsetSeconds.ToString(CultureInfo.InvariantCulture) + "S";
            string query = "command=seek&val=" + Uri.EscapeDataString(relativeValue);
            using JsonDocument status = await GetVlcJsonAsync("/requests/status.json", query);
            int positionSeconds = GetJsonInt32(status.RootElement, "time");
            sceneCountPopupWindow?.SetPlaybackStatus(
                "VLC " + (offsetSeconds >= 0 ? "+" : string.Empty) + offsetSeconds +
                " 秒 → " + FormatPlaybackPositionChinese(positionSeconds));
        }

        private MediaPlayerKind GetConfiguredPlayerMode()
        {
            string mode = LoadSetting("PlayerMode", "Auto");

            return string.Equals(mode, "VLC", StringComparison.OrdinalIgnoreCase) ? MediaPlayerKind.Vlc :
                   string.Equals(mode, "MPC", StringComparison.OrdinalIgnoreCase) ? MediaPlayerKind.Mpc :
                   MediaPlayerKind.Auto;
        }

        private async Task RunPlaybackCommandAsync(Func<Task> command)
        {
            await playbackControlLock.WaitAsync();

            try
            {
                await command();
            }
            catch (HttpRequestException)
            {
                sceneCountPopupWindow?.SetPlaybackStatus("無法連線播放器控制介面");
            }
            catch (TaskCanceledException)
            {
                sceneCountPopupWindow?.SetPlaybackStatus("播放器控制介面沒有回應");
            }
            catch (JsonException)
            {
                sceneCountPopupWindow?.SetPlaybackStatus("VLC 回傳資料無法解析");
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException)
            {
                sceneCountPopupWindow?.SetPlaybackStatus(ex.Message);
            }
            finally
            {
                playbackControlLock.Release();
            }
        }

        private void RefreshRememberedPositionText()
        {
            string text = rememberedPlaybackPositionSeconds is int seconds
                ? "已記錄：" + FormatPlaybackPositionChinese(seconds)
                : "尚未記錄播放時間";
            sceneCountPopupWindow?.SetPlaybackStatus(text);
        }

        private static string FormatPlaybackPosition(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int minutes = totalSeconds % 3600 / 60;
            int seconds = totalSeconds % 60;
            return $"{hours:00}:{minutes:00}:{seconds:00}";
        }

        private static string FormatPlaybackPositionChinese(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int minutes = totalSeconds % 3600 / 60;
            int seconds = totalSeconds % 60;
            return $"{hours:00} 小時 {minutes:00} 分 {seconds:00} 秒";
        }

        private void RestoreSceneCountPopupPosition(SceneCountPopupWindow popup)
        {
            if (TryLoadDoubleSetting("SceneCountPopup.Left", out double left) &&
                TryLoadDoubleSetting("SceneCountPopup.Top", out double top))
            {
                popup.Left = left;
                popup.Top = top;
                return;
            }

            popup.Left = Left + (ActualWidth - popup.Width) / 2;
            popup.Top = Top + (ActualHeight - popup.Height) / 2;
        }

        private void SaveSceneCountPopupPosition()
        {
            if (sceneCountPopupWindow == null)
                return;

            if (double.IsNaN(sceneCountPopupWindow.Left) || double.IsNaN(sceneCountPopupWindow.Top))
                return;

            SaveSetting("SceneCountPopup.Left", sceneCountPopupWindow.Left.ToString(CultureInfo.InvariantCulture));
            SaveSetting("SceneCountPopup.Top", sceneCountPopupWindow.Top.ToString(CultureInfo.InvariantCulture));
        }

        private bool TryLoadDoubleSetting(string key, out double value)
        {
            string text = LoadSetting(key, string.Empty);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            ScanFiles();
            RefreshExplorer();
            StatusTextBlock.Text = "Scan complete";
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            ScanLocationsWindow dialog =
                new ScanLocationsWindow(
                    LoadScanLocations(),
                    LoadSetting("MpcPath", DefaultMpcPath),
                    currentTheme,
                    LoadSetting("PlayerMode", "Auto"),
                    LoadSetting("VlcPath", DefaultVlcPath),
                    LoadSetting("VlcHost", "127.0.0.1"),
                    LoadSetting("VlcPort", "8080"),
                    LoadSetting("VlcPassword", string.Empty))
                {
                    Owner = this
                };

            if (dialog.ShowDialog() != true)
                return;

            SaveScanLocations(dialog.ScanLocations);
            SaveSetting("MpcPath", dialog.MpcPath);
            SaveSetting("PlayerMode", dialog.PlayerMode);
            SaveSetting("VlcPath", dialog.VlcPath);
            SaveSetting("VlcHost", dialog.VlcHost);
            SaveSetting("VlcPort", dialog.VlcPort);
            SaveSetting("VlcPassword", dialog.VlcPassword);
            ApplyTheme(dialog.Theme);
            SaveSetting("Theme", currentTheme);
            StatusTextBlock.Text = "Scan locations saved";
        }

        private void CaptureSceneButton_Click(object sender, RoutedEventArgs e)
        {
            _ = CaptureEventFromMpcAsync();
        }

        private void MpcToolButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSceneCountToolPopup();
        }

        private void WriteIdsButton_Click(object sender, RoutedEventArgs e)
        {
            MetadataWriteResult result = WriteVideoIdsToMetadata();

            RefreshExplorer();

            StatusTextBlock.Text =
                "Metadata IDs checked: " + result.CheckedCount +
                ", written: " + result.WrittenCount +
                ", skipped: " + result.SkippedCount;

            MessageBox.Show(
                "Checked: " + result.CheckedCount + Environment.NewLine +
                "Written: " + result.WrittenCount + Environment.NewLine +
                "Skipped: " + result.SkippedCount
            );
        }

        private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            activeTag = null;
            activeActress = null;
            activeRatingFilter = null;
            RefreshSceneGrid();
            RefreshActiveFilters();
            RefreshUsageChips();
        }

        private void DeleteSceneButton_Click(object sender, RoutedEventArgs e)
        {
            if (SceneGrid.SelectedItem is not SceneItem scene)
            {
                MessageBox.Show("Select a scene first.");
                return;
            }

            DeleteEvent(scene.EventID);
            RefreshExplorer();
        }

        private void AddSceneTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (SceneGrid.SelectedItem is not SceneItem scene)
            {
                MessageBox.Show("Select a scene first.");
                return;
            }

            string tagName = SceneTagBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(tagName))
                return;

            AddEventTag(scene.EventID, tagName);
            SceneTagBox.Text = string.Empty;
            ShowOnlyScene(scene.EventID);
        }

        private void AddSceneActressButton_Click(object sender, RoutedEventArgs e)
        {
            if (SceneGrid.SelectedItem is not SceneItem scene)
            {
                MessageBox.Show("Select a scene first.");
                return;
            }

            string actressName = SceneActressBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(actressName))
                return;

            AddEventActress(scene.EventID, actressName);
            SceneActressBox.Text = string.Empty;
            ShowOnlyScene(scene.EventID);
        }

        private void TagChipFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not UsageItem item)
                return;

            EnsureScenesLoadedForFiltering();

            activeTag =
                string.Equals(activeTag, item.Name, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : item.Name;

            RefreshSceneGrid();
            RefreshActiveFilters();
            RefreshUsageChips();
        }

        private void ActressChipFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not UsageItem item)
                return;

            EnsureScenesLoadedForFiltering();

            activeActress =
                string.Equals(activeActress, item.Name, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : item.Name;

            RefreshSceneGrid();
            RefreshActiveFilters();
            RefreshUsageChips();
        }

        private void SceneSortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                sceneSortMode = item.Content?.ToString() ?? "Rating ↓";
            }

            if (SceneGrid == null)
                return;

            RefreshSceneGrid();
        }

        private void RatingFilterChipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not RatingFilterItem item)
                return;

            activeRatingFilter = item.Rating;
            RefreshSceneGrid();
            RefreshActiveFilters();
        }

        private void ActiveFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ActiveFilterItem filter)
                return;

            if (filter.Type == "Tag")
            {
                activeTag = null;
            }
            else if (filter.Type == "Actress")
            {
                activeActress = null;
            }
            else if (filter.Type == "Rating")
            {
                activeRatingFilter = null;
            }

            RefreshSceneGrid();
            RefreshActiveFilters();
            RefreshUsageChips();
        }

        private void SceneGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SceneGrid.SelectedItem is SceneItem scene)
            {
                PlayScene(scene);
            }
        }

        private void SceneGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshVideoDetails();
        }

        private void ResolveVideoIdButton_Click(object sender, RoutedEventArgs e)
        {
            if (SceneGrid.SelectedItem is not SceneItem scene)
            {
                MessageBox.Show("Select a scene first.");
                return;
            }

            ResolveVideoIdWindow dialog =
                new ResolveVideoIdWindow(scene.VideoID)
                {
                    Owner = this
                };

            if (dialog.ShowDialog() != true)
                return;

            string newId = NormalizeResolvedVideoID(dialog.NewId);

            if (!LooksLikeVideoID(newId))
            {
                MessageBox.Show("New ID must look like STAR-426, FC2-PPV-1234567, or iid000001.");
                return;
            }

            ResolveVideoID(scene.VideoID, newId, scene.Path);
            RefreshExplorer();
            ReselectVideoID(newId);
            StatusTextBlock.Text = "Resolved " + scene.VideoID + " -> " + newId;
        }

        private void SceneStarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not int eventId)
                return;

            string tooltip = button.ToolTip?.ToString() ?? string.Empty;

            if (!int.TryParse(tooltip.Replace("Rating", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rating))
                return;

            if (rating < 1 || rating > 5)
                return;

            UpdateEventRating(eventId, rating);
            ShowOnlyScene(eventId);
        }

        private void RemoveTagChipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string tagName && button.Tag is int eventId)
            {
                RemoveEventTag(eventId, tagName);
                ShowOnlyScene(eventId);
            }
        }

        private void RemoveActressChipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string actressName && button.Tag is int eventId)
            {
                RemoveEventActress(eventId, actressName);
                ShowOnlyScene(eventId);
            }
        }

        private async Task CaptureEventFromMpcAsync()
        {
            if (isCapturingFromMpc)
                return;

            isCapturingFromMpc = true;

            try
            {
                MpcStatus? status = await GetActiveMediaPlayerStatusAsync();

                if (status == null || string.IsNullOrWhiteSpace(status.Path))
                {
                    MessageBox.Show("Could not read the active player's time and file path. Check MPC/VLC settings in Options.");
                    return;
                }

                VideoItem video = EnsureVideoExists(status.Path);

                AddCapturedEventWindow dialog = new AddCapturedEventWindow(
                    video.ID,
                    status.Time,
                    LoadDictionaryNames("Tags"),
                    LoadDictionaryNames("Actresses"),
                    LoadRecentDictionaryNames("Tags", "EventTags", "TagID", 20),
                    LoadRecentDictionaryNames("Actresses", "EventActresses", "ActressID", 10),
                    LoadScenesByVideoId(video.ID))
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                if (!TryNormalizeEventTime(dialog.EventTime, out string normalizedTime))
                {
                    MessageBox.Show("Time must be mm:ss or hh:mm:ss.");
                    return;
                }

                int eventId = SaveEvent(video.ID, normalizedTime, dialog.EventRating, dialog.EventNote, dialog.EventTags, dialog.EventActresses);
                ShowOnlyScene(eventId);
                StatusTextBlock.Text = "Scene added: " + video.ID + " " + normalizedTime;
            }
            catch (HttpRequestException)
            {
                MessageBox.Show("The configured player interface is not reachable. Check MPC/VLC settings in Options.");
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show("The configured player did not respond.");
            }
            catch (JsonException)
            {
                MessageBox.Show("VLC returned an unreadable response.");
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                isCapturingFromMpc = false;
            }
        }

        private void PlayScene(SceneItem scene)
        {
            if (!File.Exists(scene.Path))
            {
                MarkVideoAsMissing(scene.VideoID);
                RefreshExplorer();
                MessageBox.Show("File not found. This video was marked as unavailable, but metadata was kept.");
                return;
            }

            MediaPlayerKind player = GetConfiguredPlayerMode();

            if (player == MediaPlayerKind.Auto)
            {
                player = Process.GetProcessesByName("vlc").Length > 0
                    ? MediaPlayerKind.Vlc
                    : MediaPlayerKind.Mpc;
            }

            string playerPath = player == MediaPlayerKind.Vlc
                ? LoadSetting("VlcPath", DefaultVlcPath)
                : LoadSetting("MpcPath", DefaultMpcPath);

            if (!File.Exists(playerPath))
            {
                MessageBox.Show((player == MediaPlayerKind.Vlc ? "VLC" : "MPC-HC") + " path is not valid. Set it in Options.");
                return;
            }

            string arguments = player == MediaPlayerKind.Vlc
                ? "--start-time=" + TimeToSeconds(scene.Time).ToString(CultureInfo.InvariantCulture) + " \"" + scene.Path + "\""
                : "\"" + scene.Path + "\" /startpos " + scene.Time;

            Process.Start(new ProcessStartInfo()
            {
                FileName = playerPath,
                Arguments = arguments,
                UseShellExecute = true
            });
        }

        private void RefreshExplorer()
        {
            allScenes.Clear();
            allScenes.AddRange(LoadScenes());
            allScenesFullyLoaded = true;
            RefreshUsageChips();
            SceneTagBox.ItemsSource = LoadDictionaryNames("Tags");
            SceneActressBox.ItemsSource = LoadDictionaryNames("Actresses");
            RefreshSceneGrid();
            RefreshActiveFilters();
        }

        private void RefreshExplorerWithoutScenesOnStartup()
        {
            allScenes.Clear();
            allScenesFullyLoaded = false;
            SceneGrid.ItemsSource = new List<SceneItem>();
            RefreshSceneFilterBar(false);
            RefreshUsageChips();
            SceneTagBox.ItemsSource = LoadDictionaryNames("Tags");
            SceneActressBox.ItemsSource = LoadDictionaryNames("Actresses");
            RefreshActiveFilters();
            RefreshVideoDetails();
        }

        private void EnsureScenesLoadedForFiltering()
        {
            if (allScenesFullyLoaded)
                return;

            allScenes.Clear();
            allScenes.AddRange(LoadScenes());
            allScenesFullyLoaded = true;
        }

        private void ShowOnlyScene(int eventId)
        {
            activeTag = null;
            activeActress = null;
            activeRatingFilter = null;

            allScenes.Clear();
            allScenesFullyLoaded = false;

            SceneItem? scene = LoadSceneByEventId(eventId);

            if (scene != null)
            {
                allScenes.Add(scene);
                SceneGrid.ItemsSource = new List<SceneItem>() { scene };
                SceneGrid.SelectedItem = scene;
                SceneGrid.ScrollIntoView(scene);
                RefreshSceneFilterBar(true);
            }
            else
            {
                SceneGrid.ItemsSource = new List<SceneItem>();
                RefreshSceneFilterBar(false);
            }

            RefreshUsageChips();
            RefreshActiveFilters();
            RefreshVideoDetails();

            SceneTagBox.ItemsSource = LoadDictionaryNames("Tags");
            SceneActressBox.ItemsSource = LoadDictionaryNames("Actresses");
        }

        private void RefreshUsageChips()
        {
            List<UsageItem> tags =
                LoadUsage("Tags", "EventTags", "TagID");

            foreach (UsageItem tag in tags)
            {
                tag.IsActive =
                    string.Equals(tag.Name, activeTag, StringComparison.OrdinalIgnoreCase);
            }

            TagChipItemsControl.ItemsSource = tags;

            List<UsageItem> actresses =
                LoadUsage("Actresses", "EventActresses", "ActressID");

            foreach (UsageItem actress in actresses)
            {
                actress.IsActive =
                    string.Equals(actress.Name, activeActress, StringComparison.OrdinalIgnoreCase);
            }

            ActressChipItemsControl.ItemsSource = actresses;
        }

        private void RefreshSceneGrid()
        {
            if (SceneGrid == null)
                return;

            if (string.IsNullOrWhiteSpace(activeTag) &&
                string.IsNullOrWhiteSpace(activeActress))
            {
                SceneGrid.ItemsSource = new List<SceneItem>();
                RefreshSceneFilterBar(false);
                RefreshVideoDetails();
                return;
            }

            IEnumerable<SceneItem> scenes = allScenes.Where(x => x.FileExists);

            if (!string.IsNullOrWhiteSpace(activeTag))
            {
                scenes = scenes.Where(x => x.Tags.Any(tag => string.Equals(tag, activeTag, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(activeActress))
            {
                scenes = scenes.Where(x => x.Actresses.Any(actress => string.Equals(actress, activeActress, StringComparison.OrdinalIgnoreCase)));
            }

            List<SceneItem> baseScenes = scenes.ToList();
            RefreshSceneFilterBar(baseScenes.Count > 0);

            IEnumerable<SceneItem> filteredScenes = baseScenes;

            if (activeRatingFilter.HasValue)
            {
                filteredScenes = filteredScenes.Where(x => x.Rating == activeRatingFilter.Value);
            }

            SceneGrid.ItemsSource = SortScenes(filteredScenes).ToList();

            RefreshVideoDetails();
        }

        private IEnumerable<SceneItem> SortScenes(IEnumerable<SceneItem> scenes)
        {
            return sceneSortMode switch
            {
                "Rating ↑" => scenes
                    .OrderBy(x => x.Rating)
                    .ThenBy(x => x.VideoID)
                    .ThenBy(x => TimeToSeconds(x.Time)),
                "Newest" => scenes
                    .OrderByDescending(x => x.EventID),
                "Oldest" => scenes
                    .OrderBy(x => x.EventID),
                "Video ID" => scenes
                    .OrderBy(x => x.VideoID)
                    .ThenBy(x => TimeToSeconds(x.Time)),
                _ => scenes
                    .OrderByDescending(x => x.Rating)
                    .ThenBy(x => x.VideoID)
                    .ThenBy(x => TimeToSeconds(x.Time))
            };
        }

        private void RefreshSceneFilterBar(bool hasScenes)
        {
            if (SceneFilterBar == null || RatingFilterChipItemsControl == null)
                return;

            SceneFilterBar.Visibility = hasScenes ? Visibility.Visible : Visibility.Collapsed;

            RatingFilterChipItemsControl.ItemsSource = new List<RatingFilterItem>()
            {
                new RatingFilterItem() { Label = "ALL★", Rating = null, IsActive = !activeRatingFilter.HasValue },
                new RatingFilterItem() { Label = "5★", Rating = 5, IsActive = activeRatingFilter == 5 },
                new RatingFilterItem() { Label = "4★", Rating = 4, IsActive = activeRatingFilter == 4 },
                new RatingFilterItem() { Label = "3★", Rating = 3, IsActive = activeRatingFilter == 3 },
                new RatingFilterItem() { Label = "2★", Rating = 2, IsActive = activeRatingFilter == 2 },
                new RatingFilterItem() { Label = "1★", Rating = 1, IsActive = activeRatingFilter == 1 }
            };
        }

        private void RefreshActiveFilters()
        {
            List<ActiveFilterItem> filters = new List<ActiveFilterItem>();

            if (!string.IsNullOrWhiteSpace(activeTag))
                filters.Add(new ActiveFilterItem() { Type = "Tag", Name = activeTag });

            if (!string.IsNullOrWhiteSpace(activeActress))
                filters.Add(new ActiveFilterItem() { Type = "Actress", Name = activeActress });

            if (activeRatingFilter.HasValue)
                filters.Add(new ActiveFilterItem() { Type = "Rating", Name = activeRatingFilter.Value + "★" });

            ActiveFilterItemsControl.ItemsSource = filters;
        }

        private void ReselectScene(int eventId)
        {
            SceneItem? scene =
                SceneGrid.Items
                    .Cast<SceneItem>()
                    .FirstOrDefault(x => x.EventID == eventId);

            if (scene == null)
                return;

            SceneGrid.SelectedItem = scene;
            SceneGrid.ScrollIntoView(scene);
        }

        private void ReselectVideoID(string videoId)
        {
            SceneItem? scene =
                SceneGrid.Items
                    .Cast<SceneItem>()
                    .FirstOrDefault(x => string.Equals(x.VideoID, videoId, StringComparison.OrdinalIgnoreCase));

            if (scene == null)
                return;

            SceneGrid.SelectedItem = scene;
            SceneGrid.ScrollIntoView(scene);
        }

        private void RefreshVideoDetails()
        {
            if (SceneGrid.SelectedItem is not SceneItem scene)
            {
                DetailVideoIdTextBlock.Text = "-";
                DetailFileNameTextBlock.Text = "-";
                DetailMetadataIdTextBlock.Text = "-";
                DetailStateTextBlock.Text = "-";
                DetailPathTextBox.Text = "-";
                return;
            }

            DetailVideoIdTextBlock.Text = scene.VideoID;
            DetailFileNameTextBlock.Text = string.IsNullOrWhiteSpace(scene.Path)
                ? "-"
                : Path.GetFileName(scene.Path);
            DetailMetadataIdTextBlock.Text = string.IsNullOrWhiteSpace(scene.MetadataID)
                ? "(none)"
                : scene.MetadataID;
            DetailStateTextBlock.Text =
                "FileExists=" + scene.FileExists +
                ", Pending=" + scene.MetadataWritePending;
            DetailPathTextBox.Text = scene.Path;
        }

        private void InitializeDatabase()
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS Videos
(
    ID TEXT PRIMARY KEY,
    Path TEXT,
    Rating INTEGER DEFAULT 0,
    Subtitle TEXT DEFAULT '',
    Priority INTEGER DEFAULT 0,
    FileExists INTEGER DEFAULT 1,
    MetadataWritePending INTEGER DEFAULT 0
)");

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS Events
(
    ID INTEGER PRIMARY KEY AUTOINCREMENT,
    VideoID TEXT,
    Time TEXT,
    Rating INTEGER DEFAULT 0,
    Note TEXT DEFAULT ''
)");

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS Tags
(
    ID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT UNIQUE COLLATE NOCASE
)");

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS Actresses
(
    ID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT UNIQUE COLLATE NOCASE
)");

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS EventTags
(
    EventID INTEGER,
    TagID INTEGER,
    UNIQUE(EventID, TagID)
)");

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS EventActresses
(
    EventID INTEGER,
    ActressID INTEGER,
    UNIQUE(EventID, ActressID)
)");

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS ScanLocations
(
    ID INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT UNIQUE COLLATE NOCASE,
    IncludeSubdirectories INTEGER DEFAULT 1,
    Enabled INTEGER DEFAULT 1,
    LastScannedAt TEXT DEFAULT '',
    LastError TEXT DEFAULT ''
)");

            ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS AppSettings
(
    Key TEXT PRIMARY KEY,
    Value TEXT
)");

            EnsureColumn(connection, "Videos", "Priority", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "Videos", "FileExists", "INTEGER DEFAULT 1");
            EnsureColumn(connection, "Videos", "MetadataWritePending", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "Events", "Rating", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "Events", "Note", "TEXT DEFAULT ''");
            EnsureColumn(connection, "ScanLocations", "IncludeSubdirectories", "INTEGER DEFAULT 1");
            EnsureColumn(connection, "ScanLocations", "Enabled", "INTEGER DEFAULT 1");
            EnsureColumn(connection, "ScanLocations", "LastScannedAt", "TEXT DEFAULT ''");
            EnsureColumn(connection, "ScanLocations", "LastError", "TEXT DEFAULT ''");
            NormalizeRelationTable(connection, "EventTags", "TagID");
            NormalizeRelationTable(connection, "EventActresses", "ActressID");
            MigrateLegacyTags(connection);
            EnsureDefaultScanLocation(connection);
            EnsureDefaultSetting(connection, "MpcPath", DefaultMpcPath);
            EnsureDefaultSetting(connection, "PlayerMode", "Auto");
            EnsureDefaultSetting(connection, "VlcPath", DefaultVlcPath);
            EnsureDefaultSetting(connection, "VlcHost", "127.0.0.1");
            EnsureDefaultSetting(connection, "VlcPort", "8080");
            EnsureDefaultSetting(connection, "VlcPassword", string.Empty);
        }

        private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
        {
            if (ColumnExists(connection, table, column))
                return;

            ExecuteNonQuery(connection, "ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition);
        }

        private static bool ColumnExists(SqliteConnection connection, string table, string column)
        {
            using SqliteCommand command = new SqliteCommand("PRAGMA table_info(" + table + ")", connection);
            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql)
        {
            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.ExecuteNonQuery();
        }

        private void MigrateLegacyTags(SqliteConnection connection)
        {
            if (!ColumnExists(connection, "Events", "Tag"))
                return;

            using SqliteCommand selectCommand = new SqliteCommand("SELECT ID, Tag FROM Events WHERE Tag IS NOT NULL AND TRIM(Tag) <> ''", connection);
            using SqliteDataReader reader = selectCommand.ExecuteReader();
            List<(int EventId, string TagText)> rows = new List<(int EventId, string TagText)>();

            while (reader.Read())
            {
                rows.Add((reader.GetInt32(0), reader.GetString(1)));
            }

            foreach ((int eventId, string tagText) in rows)
            {
                foreach (string tag in SplitTokens(tagText))
                {
                    int tagId = EnsureDictionaryItem(connection, "Tags", tag);
                    LinkDictionaryItem(connection, "EventTags", "TagID", eventId, tagId);
                }
            }

            NormalizeRelationTable(connection, "EventTags", "TagID");
        }

        private void EnsureDefaultScanLocation(SqliteConnection connection)
        {
            using SqliteCommand countCommand =
                new SqliteCommand("SELECT COUNT(*) FROM ScanLocations", connection);

            long count =
                Convert.ToInt64(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

            if (count > 0)
                return;

            using SqliteCommand insertCommand =
                new SqliteCommand(
                    @"INSERT INTO ScanLocations
(Path, IncludeSubdirectories, Enabled, LastError)
VALUES
($path, 0, 1, '')",
                    connection
                );

            insertCommand.Parameters.AddWithValue("$path", NormalizeDirectoryPath(ScanRootPath));
            insertCommand.ExecuteNonQuery();
        }

        private void EnsureDefaultSetting(SqliteConnection connection, string key, string value)
        {
            using SqliteCommand command =
                new SqliteCommand(
                    @"INSERT OR IGNORE INTO AppSettings
(Key, Value)
VALUES
($key, $value)",
                    connection
                );

            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        private static void NormalizeRelationTable(SqliteConnection connection, string relationTable, string relationIdColumn)
        {
            ExecuteNonQuery(connection, @"
DELETE FROM " + relationTable + @"
WHERE rowid NOT IN
(
    SELECT MIN(rowid)
    FROM " + relationTable + @"
    GROUP BY EventID, " + relationIdColumn + @"
)");

            ExecuteNonQuery(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_" + relationTable + "_Event_Item ON " +
                relationTable + " (EventID, " + relationIdColumn + ")");
        }

        private void ScanFiles()
        {
            List<ScanLocationItem> locations =
                LoadScanLocations()
                    .Where(x => x.Enabled)
                    .ToList();

            List<ScanLocationItem> scannedLocations = new List<ScanLocationItem>();

            foreach (ScanLocationItem location in locations)
            {
                string normalizedPath = NormalizeDirectoryPath(location.Path);

                if (!Directory.Exists(normalizedPath))
                {
                    UpdateScanLocationStatus(location.ID, string.Empty, "Directory not found");
                    continue;
                }

                try
                {
                    SearchOption searchOption =
                        location.IncludeSubdirectories
                            ? SearchOption.AllDirectories
                            : SearchOption.TopDirectoryOnly;

                    foreach (string file in Directory.GetFiles(normalizedPath, "*.*", searchOption))
                    {
                        if (!IsSupportedVideoFile(file))
                            continue;

                        SaveVideo(new VideoItem()
                        {
                            ID = ResolveVideoID(file),
                            Path = file,
                            Priority = GetVideoPriority(file),
                            FileExists = true
                        });
                    }

                    UpdateScanLocationStatus(location.ID, DateTime.Now.ToString("s", CultureInfo.InvariantCulture), string.Empty);
                    scannedLocations.Add(location);
                }
                catch (UnauthorizedAccessException ex)
                {
                    UpdateScanLocationStatus(location.ID, string.Empty, ex.Message);
                }
                catch (IOException ex)
                {
                    UpdateScanLocationStatus(location.ID, string.Empty, ex.Message);
                }
            }

            MarkMissingVideos(scannedLocations);
        }

        private MetadataWriteResult WriteVideoIdsToMetadata()
        {
            MetadataWriteResult result = new MetadataWriteResult();

            foreach (MetadataSyncTarget target in LoadMetadataSyncTargets())
            {
                result.CheckedCount++;

                if (!File.Exists(target.Path))
                {
                    SetMetadataWritePending(target.ID, true);
                    result.SkippedCount++;
                    continue;
                }

                string? metadataId = TryReadVideoIdMetadata(target.Path);

                if (string.Equals(metadataId, target.ID, StringComparison.OrdinalIgnoreCase))
                {
                    SetMetadataWritePending(target.ID, false);
                    result.SkippedCount++;
                    continue;
                }

                if (TryWriteVideoIdMetadata(target.Path, target.ID))
                {
                    SetMetadataWritePending(target.ID, false);
                    result.WrittenCount++;
                }
                else
                {
                    SetMetadataWritePending(target.ID, true);
                    result.SkippedCount++;
                }
            }

            return result;
        }

        private List<MetadataSyncTarget> LoadMetadataSyncTargets()
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command =
                new SqliteCommand(
                    @"SELECT
    ID,
    Path,
    MetadataWritePending
FROM Videos
WHERE Path IS NOT NULL
  AND TRIM(Path) <> ''",
                    connection
                );

            using SqliteDataReader reader = command.ExecuteReader();
            List<MetadataSyncTarget> targets = new List<MetadataSyncTarget>();

            while (reader.Read())
            {
                targets.Add(new MetadataSyncTarget()
                {
                    ID = reader.GetString(0),
                    Path = reader.GetString(1),
                    MetadataWritePending = reader.GetInt32(2) == 1
                });
            }

            return targets;
        }

        private bool IsSupportedVideoFile(string path)
        {
            string[] supportedExtensions = { ".mkv", ".mp4", ".avi", ".ts" };

            return supportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        private VideoItem EnsureVideoExists(string path)
        {
            VideoItem video = new VideoItem()
            {
                ID = ResolveVideoID(path),
                Path = path,
                Priority = GetVideoPriority(path),
                FileExists = true
            };

            SaveVideo(video);
            return video;
        }

        private void SaveVideo(VideoItem video)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            if (metadataWritePendingByPath.TryGetValue(NormalizeFilePath(video.Path), out bool pending))
            {
                video.MetadataWritePending = pending;
            }

            string sql = @"INSERT INTO Videos
(ID, Path, Rating, Subtitle, Priority, FileExists, MetadataWritePending)
VALUES
($id, $path, $rating, $subtitle, $priority, $fileExists, $metadataWritePending)
ON CONFLICT(ID) DO UPDATE SET
    Path = CASE
        WHEN excluded.Priority >= Videos.Priority OR Videos.FileExists = 0 THEN excluded.Path
        ELSE Videos.Path
    END,
    Priority = CASE
        WHEN excluded.Priority >= Videos.Priority OR Videos.FileExists = 0 THEN excluded.Priority
        ELSE Videos.Priority
    END,
    FileExists = 1,
    MetadataWritePending = excluded.MetadataWritePending";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$id", video.ID);
            command.Parameters.AddWithValue("$path", video.Path);
            command.Parameters.AddWithValue("$rating", video.Rating);
            command.Parameters.AddWithValue("$subtitle", video.Subtitle);
            command.Parameters.AddWithValue("$priority", video.Priority);
            command.Parameters.AddWithValue("$fileExists", video.FileExists ? 1 : 0);
            command.Parameters.AddWithValue("$metadataWritePending", video.MetadataWritePending ? 1 : 0);
            command.ExecuteNonQuery();
        }

        private int SaveEvent(string videoId, string time, int rating, string note, List<string> tags, List<string> actresses)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql = @"INSERT INTO Events (VideoID, Time, Rating, Note)
VALUES ($videoId, $time, $rating, $note);
SELECT last_insert_rowid();";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$videoId", videoId);
            command.Parameters.AddWithValue("$time", time);
            command.Parameters.AddWithValue("$rating", rating);
            command.Parameters.AddWithValue("$note", note);

            int eventId = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            foreach (string tag in tags.SelectMany(SplitTokens).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int tagId = EnsureDictionaryItem(connection, "Tags", tag);
                LinkDictionaryItem(connection, "EventTags", "TagID", eventId, tagId);
            }

            foreach (string actress in actresses.SelectMany(SplitTokens).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int actressId = EnsureDictionaryItem(connection, "Actresses", actress);
                LinkDictionaryItem(connection, "EventActresses", "ActressID", eventId, actressId);
            }

            GenerateThumbnail(eventId);
            return eventId;
        }

        private void GenerateThumbnail(int eventId)
        {
            try
            {
                using SqliteConnection connection =
                    new SqliteConnection(DatabasePath);

                connection.Open();

                using SqliteCommand command =
                    new SqliteCommand(
                        @"
SELECT
    Videos.Path,
    Events.Time
FROM Events
INNER JOIN Videos
    ON Videos.ID = Events.VideoID
WHERE Events.ID = $eventId
",
                        connection);

                command.Parameters.AddWithValue("$eventId", eventId);

                using SqliteDataReader reader = command.ExecuteReader();

                if (!reader.Read())
                    return;

                string videoPath =
                    reader.IsDBNull(0)
                        ? string.Empty
                        : reader.GetString(0);

                string time =
                    reader.IsDBNull(1)
                        ? string.Empty
                        : reader.GetString(1);

                if (string.IsNullOrWhiteSpace(videoPath))
                    return;

                if (!File.Exists(videoPath))
                    return;

                if (string.IsNullOrWhiteSpace(time))
                    return;

                string ffmpegPath =
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "ffmpeg.exe");

                if (!File.Exists(ffmpegPath))
                {
                    MessageBox.Show(
                        "ffmpeg.exe not found.\n\n" +
                        "Place ffmpeg.exe beside VideoDB.exe");

                    return;
                }

                string thumbnailDirectory =
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "thumbnails");

                Directory.CreateDirectory(thumbnailDirectory);

                string outputPath =
                    Path.Combine(
                        thumbnailDirectory,
                        eventId + ".webp");

                int seconds = TimeToSeconds(time);

                // 🔥 往後 offset 一點，避免黑畫面 / transition
                double captureTime = seconds + 0.8;

                string ffmpegTime =
                    TimeSpan.FromSeconds(captureTime)
                        .ToString(
                            @"hh\:mm\:ss\.fff",
                            CultureInfo.InvariantCulture);

                ProcessStartInfo startInfo =
                    new ProcessStartInfo()
                    {
                        FileName = ffmpegPath,

                        Arguments =
                            "-y " +
                            "-ss " + ffmpegTime + " " +
                            "-i \"" + videoPath + "\" " +
                            "-frames:v 1 " +
                            "-vf \"scale=320:-1\" " +
                            "-c:v libwebp " +
                            "-quality 80 " +
                            "\"" + outputPath + "\"",

                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };

                using Process process =
                    Process.Start(startInfo)!;

                process.WaitForExit();

                if (!File.Exists(outputPath))
                {
                    string error =
                        process.StandardError.ReadToEnd();

                    MessageBox.Show(
                        "Thumbnail generation failed.\n\n" +
                        error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Thumbnail generation error.\n\n" +
                    ex.Message);
            }
        }

        private List<SceneItem> LoadScenes()
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql = @"SELECT
    Events.ID,
    Events.VideoID,
    Events.Time,
    Events.Rating,
    Events.Note,
    Videos.Path,
    Videos.FileExists,
    Videos.MetadataWritePending
FROM Events
LEFT JOIN Videos ON Videos.ID = Events.VideoID
ORDER BY Events.VideoID, Events.Time";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            using SqliteDataReader reader = command.ExecuteReader();
            List<SceneItem> result = new List<SceneItem>();

            while (reader.Read())
            {
                int eventId = reader.GetInt32(0);

                result.Add(new SceneItem()
                {
                    EventID = eventId,
                    VideoID = reader.GetString(1),
                    Time = reader.GetString(2),
                    Rating = reader.GetInt32(3),
                    Note = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Path = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    FileExists = !reader.IsDBNull(6) && reader.GetInt32(6) == 1,
                    MetadataWritePending = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,
                    MetadataID = reader.IsDBNull(5) ? string.Empty : TryReadVideoIdMetadata(reader.GetString(5)) ?? string.Empty,
                    Tags = LoadLinkedNames(connection, "Tags", "EventTags", "TagID", eventId),
                    Actresses = LoadLinkedNames(connection, "Actresses", "EventActresses", "ActressID", eventId)
                });
            }

            return result;
        }

        private SceneItem? LoadSceneByEventId(int eventId)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql = @"
SELECT
    Events.ID,
    Events.VideoID,
    Events.Time,
    Events.Rating,
    Events.Note,
    Videos.Path,
    Videos.FileExists,
    Videos.MetadataWritePending
FROM Events
LEFT JOIN Videos ON Videos.ID = Events.VideoID
WHERE Events.ID = $eventId";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$eventId", eventId);

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            int id = reader.GetInt32(0);

            return new SceneItem()
            {
                EventID = id,
                VideoID = reader.GetString(1),
                Time = reader.GetString(2),
                Rating = reader.GetInt32(3),
                Note = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Path = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                FileExists = !reader.IsDBNull(6) && reader.GetInt32(6) == 1,
                MetadataWritePending = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,
                MetadataID = reader.IsDBNull(5) ? string.Empty : TryReadVideoIdMetadata(reader.GetString(5)) ?? string.Empty,
                Tags = LoadLinkedNames(connection, "Tags", "EventTags", "TagID", id),
                Actresses = LoadLinkedNames(connection, "Actresses", "EventActresses", "ActressID", id)
            };
        }

        private List<SceneItem> LoadScenesByVideoId(string videoId)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql = @"
SELECT
    Events.ID,
    Events.VideoID,
    Events.Time,
    Events.Rating,
    Events.Note,
    Videos.Path,
    Videos.FileExists,
    Videos.MetadataWritePending
FROM Events
LEFT JOIN Videos ON Videos.ID = Events.VideoID
WHERE Events.VideoID = $videoId
ORDER BY Events.Time";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$videoId", videoId);

            using SqliteDataReader reader = command.ExecuteReader();
            List<SceneItem> result = new List<SceneItem>();

            while (reader.Read())
            {
                int id = reader.GetInt32(0);

                result.Add(new SceneItem()
                {
                    EventID = id,
                    VideoID = reader.GetString(1),
                    Time = reader.GetString(2),
                    Rating = reader.GetInt32(3),
                    Note = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Path = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    FileExists = !reader.IsDBNull(6) && reader.GetInt32(6) == 1,
                    MetadataWritePending = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,
                    MetadataID = reader.IsDBNull(5) ? string.Empty : TryReadVideoIdMetadata(reader.GetString(5)) ?? string.Empty,
                    Tags = LoadLinkedNames(connection, "Tags", "EventTags", "TagID", id),
                    Actresses = LoadLinkedNames(connection, "Actresses", "EventActresses", "ActressID", id)
                });
            }

            return result;
        }

        private SceneCountLookupResult GetRecordedSceneCountForMpcPath(string path)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            SceneCountLookupResult? result = FindSceneCountByPath(connection, path);

            if (result != null)
                return result;

            string normalizedPath = NormalizeFilePath(path);

            if (!string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                result = FindSceneCountByPath(connection, normalizedPath);

                if (result != null)
                    return result;
            }

            IEnumerable<string> candidateIds =
                new[]
                {
                    TryReadVideoIdMetadata(path),
                    TryParseVideoID(Path.GetFileNameWithoutExtension(path))
                }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string candidateId in candidateIds)
            {
                result = FindSceneCountByVideoId(connection, candidateId);

                if (result != null)
                    return result;
            }

            return new SceneCountLookupResult();
        }

        private SceneCountLookupResult? FindSceneCountByPath(SqliteConnection connection, string path)
        {
            using SqliteCommand command =
                new SqliteCommand(
                    @"
SELECT
    Videos.ID,
    COUNT(Events.ID)
FROM Videos
LEFT JOIN Events ON Events.VideoID = Videos.ID
WHERE Videos.Path = $path COLLATE NOCASE
GROUP BY Videos.ID
LIMIT 1",
                    connection
                );

            command.Parameters.AddWithValue("$path", path);

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            return new SceneCountLookupResult()
            {
                VideoFound = true,
                VideoID = reader.GetString(0),
                SceneCount = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture)
            };
        }

        private SceneCountLookupResult? FindSceneCountByVideoId(SqliteConnection connection, string videoId)
        {
            using SqliteCommand command =
                new SqliteCommand(
                    @"
SELECT
    Videos.ID,
    COUNT(Events.ID)
FROM Videos
LEFT JOIN Events ON Events.VideoID = Videos.ID
WHERE Videos.ID = $videoId COLLATE NOCASE
GROUP BY Videos.ID
LIMIT 1",
                    connection
                );

            command.Parameters.AddWithValue("$videoId", videoId);

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            return new SceneCountLookupResult()
            {
                VideoFound = true,
                VideoID = reader.GetString(0),
                SceneCount = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture)
            };
        }

        private List<UsageItem> LoadUsage(string dictionaryTable, string relationTable, string relationIdColumn)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string dictionaryId = dictionaryTable == "Tags" ? "TagID" : "ActressID";
            string sql = @"SELECT d.Name, COUNT(DISTINCT r.EventID) AS UsageCount
FROM " + relationTable + @" r
INNER JOIN " + dictionaryTable + @" d ON d.ID = r." + dictionaryId + @"
INNER JOIN Events e ON e.ID = r.EventID
INNER JOIN Videos v ON v.ID = e.VideoID
WHERE v.FileExists = 1
GROUP BY d.ID, d.Name
ORDER BY UsageCount DESC, d.Name ASC";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            using SqliteDataReader reader = command.ExecuteReader();
            List<UsageItem> result = new List<UsageItem>();

            while (reader.Read())
            {
                result.Add(new UsageItem()
                {
                    Name = reader.GetString(0),
                    Count = reader.GetInt32(1)
                });
            }

            return result;
        }

        private List<string> LoadDictionaryNames(string table)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command = new SqliteCommand("SELECT Name FROM " + table + " ORDER BY Name", connection);
            using SqliteDataReader reader = command.ExecuteReader();
            List<string> names = new List<string>();

            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        private List<string> LoadRecentDictionaryNames(string dictionaryTable, string relationTable, string relationIdColumn, int limit)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql = @"SELECT d.Name, MAX(r.EventID) AS LastEventID
FROM " + relationTable + @" r
INNER JOIN " + dictionaryTable + @" d ON d.ID = r." + relationIdColumn + @"
GROUP BY d.ID, d.Name
ORDER BY LastEventID DESC, d.Name ASC
LIMIT $limit";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$limit", limit);
            using SqliteDataReader reader = command.ExecuteReader();
            List<string> names = new List<string>();

            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        private static List<string> LoadLinkedNames(SqliteConnection connection, string dictionaryTable, string relationTable, string relationIdColumn, int eventId)
        {
            string sql = @"SELECT DISTINCT d.Name
FROM " + relationTable + @" r
INNER JOIN " + dictionaryTable + @" d ON d.ID = r." + relationIdColumn + @"
WHERE r.EventID = $eventId
ORDER BY d.Name";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$eventId", eventId);
            using SqliteDataReader reader = command.ExecuteReader();
            List<string> names = new List<string>();

            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        private static int EnsureDictionaryItem(SqliteConnection connection, string table, string name)
        {
            string cleanName = name.Trim();
            ExecuteNonQueryWithParameter(connection, "INSERT OR IGNORE INTO " + table + " (Name) VALUES ($name)", "$name", cleanName);

            using SqliteCommand command = new SqliteCommand("SELECT ID FROM " + table + " WHERE Name = $name", connection);
            command.Parameters.AddWithValue("$name", cleanName);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void LinkDictionaryItem(SqliteConnection connection, string relationTable, string relationIdColumn, int eventId, int itemId)
        {
            string sql = "INSERT OR IGNORE INTO " + relationTable + " (EventID, " + relationIdColumn + ") VALUES ($eventId, $itemId)";
            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$eventId", eventId);
            command.Parameters.AddWithValue("$itemId", itemId);
            command.ExecuteNonQuery();
        }

        private static void ExecuteNonQueryWithParameter(SqliteConnection connection, string sql, string name, object value)
        {
            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        private void RemoveEventTag(int eventId, string tagName)
        {
            RemoveRelation(eventId, tagName, "Tags", "EventTags", "TagID");
        }

        private void RemoveEventActress(int eventId, string actressName)
        {
            RemoveRelation(eventId, actressName, "Actresses", "EventActresses", "ActressID");
        }

        private void AddEventTag(int eventId, string tagName)
        {
            AddRelation(eventId, tagName, "Tags", "EventTags", "TagID");
        }

        private void AddEventActress(int eventId, string actressName)
        {
            AddRelation(eventId, actressName, "Actresses", "EventActresses", "ActressID");
        }

        private void AddRelation(int eventId, string name, string dictionaryTable, string relationTable, string relationIdColumn)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            int itemId = EnsureDictionaryItem(connection, dictionaryTable, name);
            LinkDictionaryItem(connection, relationTable, relationIdColumn, eventId, itemId);
        }

        private void RemoveRelation(int eventId, string name, string dictionaryTable, string relationTable, string relationIdColumn)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql = "DELETE FROM " + relationTable + " WHERE EventID = $eventId AND " + relationIdColumn + " IN (SELECT ID FROM " + dictionaryTable + " WHERE Name = $name)";
            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$eventId", eventId);
            command.Parameters.AddWithValue("$name", name);
            command.ExecuteNonQuery();
        }

        private void DeleteEvent(int eventId)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            ExecuteNonQueryWithParameter(connection, "DELETE FROM EventTags WHERE EventID = $id", "$id", eventId);
            ExecuteNonQueryWithParameter(connection, "DELETE FROM EventActresses WHERE EventID = $id", "$id", eventId);
            ExecuteNonQueryWithParameter(connection, "DELETE FROM Events WHERE ID = $id", "$id", eventId);
        }

        private void UpdateEventRating(int eventId, int rating)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command =
                new SqliteCommand(
                    "UPDATE Events SET Rating = $rating WHERE ID = $eventId",
                    connection
                );

            command.Parameters.AddWithValue("$eventId", eventId);
            command.Parameters.AddWithValue("$rating", rating);
            command.ExecuteNonQuery();
        }

        private List<ScanLocationItem> LoadScanLocations()
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql =
                @"SELECT
    ID,
    Path,
    IncludeSubdirectories,
    Enabled,
    LastError
FROM ScanLocations
ORDER BY Path";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            using SqliteDataReader reader = command.ExecuteReader();
            List<ScanLocationItem> locations = new List<ScanLocationItem>();

            while (reader.Read())
            {
                locations.Add(new ScanLocationItem()
                {
                    ID = reader.GetInt32(0),
                    Path = reader.GetString(1),
                    IncludeSubdirectories = reader.GetInt32(2) == 1,
                    Enabled = reader.GetInt32(3) == 1,
                    LastError = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                });
            }

            return locations;
        }

        private void SaveScanLocations(List<ScanLocationItem> locations)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            ExecuteNonQuery(connection, "DELETE FROM ScanLocations");

            foreach (ScanLocationItem location in locations)
            {
                string path = NormalizeDirectoryPath(location.Path);

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                using SqliteCommand command =
                    new SqliteCommand(
                        @"INSERT OR IGNORE INTO ScanLocations
(Path, IncludeSubdirectories, Enabled, LastError)
VALUES
($path, $includeSubdirectories, $enabled, $lastError)",
                        connection
                    );

                command.Parameters.AddWithValue("$path", path);
                command.Parameters.AddWithValue("$includeSubdirectories", location.IncludeSubdirectories ? 1 : 0);
                command.Parameters.AddWithValue("$enabled", location.Enabled ? 1 : 0);
                command.Parameters.AddWithValue("$lastError", location.LastError ?? string.Empty);
                command.ExecuteNonQuery();
            }
        }

        private string LoadSetting(string key, string defaultValue)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command =
                new SqliteCommand(
                    "SELECT Value FROM AppSettings WHERE Key = $key",
                    connection
                );

            command.Parameters.AddWithValue("$key", key);

            object? value = command.ExecuteScalar();

            if (value == null || value == DBNull.Value)
                return defaultValue;

            string text = value.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;

            return text;
        }

        private void SaveSetting(string key, string value)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command =
                new SqliteCommand(
                    @"INSERT INTO AppSettings
(Key, Value)
VALUES
($key, $value)
ON CONFLICT(Key) DO UPDATE SET
    Value = excluded.Value",
                    connection
                );

            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        private void UpdateScanLocationStatus(int id, string lastScannedAt, string lastError)
        {
            if (id <= 0)
                return;

            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            string sql =
                @"UPDATE ScanLocations
SET
    LastScannedAt = CASE WHEN $lastScannedAt = '' THEN LastScannedAt ELSE $lastScannedAt END,
    LastError = $lastError
WHERE ID = $id";

            using SqliteCommand command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$lastScannedAt", lastScannedAt);
            command.Parameters.AddWithValue("$lastError", lastError);
            command.ExecuteNonQuery();
        }

        private void MarkMissingVideos(List<ScanLocationItem> scannedLocations)
        {
            if (scannedLocations.Count == 0)
                return;

            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command = new SqliteCommand("SELECT ID, Path FROM Videos WHERE FileExists = 1", connection);
            using SqliteDataReader reader = command.ExecuteReader();
            List<string> missingIds = new List<string>();

            while (reader.Read())
            {
                string id = reader.GetString(0);
                string path = reader.GetString(1);

                bool belongsToScannedLocation =
                    scannedLocations.Any(x => IsPathUnderDirectory(path, x.Path));

                if (belongsToScannedLocation && !File.Exists(path))
                {
                    missingIds.Add(id);
                }
            }

            foreach (string id in missingIds)
            {
                MarkVideoAsMissing(id);
            }
        }

        private bool IsPathUnderDirectory(string filePath, string directoryPath)
        {
            string normalizedFilePath =
                Path.GetFullPath(filePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string normalizedDirectoryPath =
                NormalizeDirectoryPath(directoryPath) + Path.DirectorySeparatorChar;

            return normalizedFilePath.StartsWith(normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
        }

        private void MarkVideoAsMissing(string videoId)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();
            ExecuteNonQueryWithParameter(connection, "UPDATE Videos SET FileExists = 0 WHERE ID = $id", "$id", videoId);
        }

        private void SetMetadataWritePending(string videoId, bool pending)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command =
                new SqliteCommand(
                    "UPDATE Videos SET MetadataWritePending = $pending WHERE ID = $id",
                    connection
                );

            command.Parameters.AddWithValue("$id", videoId);
            command.Parameters.AddWithValue("$pending", pending ? 1 : 0);
            command.ExecuteNonQuery();
        }

        private void ResolveVideoID(string currentId, string newId, string path)
        {
            if (string.Equals(currentId, newId, StringComparison.OrdinalIgnoreCase))
                return;

            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            bool targetExists = VideoExists(connection, transaction, newId);

            if (targetExists)
            {
                using SqliteCommand updateEventsCommand =
                    new SqliteCommand(
                        "UPDATE Events SET VideoID = $newId WHERE VideoID = $currentId",
                        connection,
                        transaction
                    );

                updateEventsCommand.Parameters.AddWithValue("$newId", newId);
                updateEventsCommand.Parameters.AddWithValue("$currentId", currentId);
                updateEventsCommand.ExecuteNonQuery();

                using SqliteCommand deleteCurrentCommand =
                    new SqliteCommand(
                        "DELETE FROM Videos WHERE ID = $currentId",
                        connection,
                        transaction
                    );

                deleteCurrentCommand.Parameters.AddWithValue("$currentId", currentId);
                deleteCurrentCommand.ExecuteNonQuery();
            }
            else
            {
                using SqliteCommand updateVideoCommand =
                    new SqliteCommand(
                        "UPDATE Videos SET ID = $newId WHERE ID = $currentId",
                        connection,
                        transaction
                    );

                updateVideoCommand.Parameters.AddWithValue("$newId", newId);
                updateVideoCommand.Parameters.AddWithValue("$currentId", currentId);
                updateVideoCommand.ExecuteNonQuery();

                using SqliteCommand updateEventsCommand =
                    new SqliteCommand(
                        "UPDATE Events SET VideoID = $newId WHERE VideoID = $currentId",
                        connection,
                        transaction
                    );

                updateEventsCommand.Parameters.AddWithValue("$newId", newId);
                updateEventsCommand.Parameters.AddWithValue("$currentId", currentId);
                updateEventsCommand.ExecuteNonQuery();
            }

            transaction.Commit();

            bool metadataWritten =
                !string.IsNullOrWhiteSpace(path) &&
                File.Exists(path) &&
                TryWriteVideoIdMetadata(path, newId);

            SetMetadataWritePending(newId, !metadataWritten);
        }

        private bool VideoExists(SqliteConnection connection, SqliteTransaction transaction, string videoId)
        {
            using SqliteCommand command =
                new SqliteCommand(
                    "SELECT COUNT(*) FROM Videos WHERE ID = $id",
                    connection,
                    transaction
                );

            command.Parameters.AddWithValue("$id", videoId);

            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }

        private async Task<MpcStatus?> GetMpcStatusAsync()
        {
            string html = await httpClient.GetStringAsync("http://127.0.0.1:13579/status.html");
            Match match = Regex.Match(
                html,
                @"OnStatus\(""(?<title>(?:\\""|[^""])*?)"",\s*""(?<state>(?:\\""|[^""])*?)"",\s*(?<position>\d+),\s*""(?<positionText>[^""]*)"",\s*(?<duration>\d+),\s*""(?<durationText>[^""]*)"",\s*(?<muted>\d+),\s*(?<volume>-?\d+),\s*""(?<path>[^""]*)""\)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            string path = WebUtility.HtmlDecode(match.Groups["path"].Value).Trim();
            string positionText = WebUtility.HtmlDecode(match.Groups["positionText"].Value).Trim();
            int positionSeconds = int.Parse(match.Groups["position"].Value, CultureInfo.InvariantCulture) / 1000;
            int durationSeconds = int.Parse(match.Groups["duration"].Value, CultureInfo.InvariantCulture) / 1000;

            if (!TryNormalizeEventTime(positionText, out string normalizedTime))
            {
                normalizedTime = SecondsToMPCFormat(positionSeconds);
            }

            return new MpcStatus()
            {
                Player = MediaPlayerKind.Mpc,
                Path = path,
                Time = normalizedTime,
                PositionSeconds = positionSeconds,
                DurationSeconds = durationSeconds,
                IsPlaying = string.Equals(match.Groups["state"].Value, "Playing", StringComparison.OrdinalIgnoreCase)
            };
        }

        private async Task<MpcStatus?> GetVlcStatusAsync()
        {
            using JsonDocument statusDocument = await GetVlcJsonAsync("/requests/status.json");
            JsonElement root = statusDocument.RootElement;
            int positionSeconds = GetJsonInt32(root, "time");
            int durationSeconds = GetJsonInt32(root, "length");
            string state = GetJsonString(root, "state");
            string path = FindVlcMediaPath(root);

            if (string.IsNullOrWhiteSpace(path))
            {
                using JsonDocument playlistDocument = await GetVlcJsonAsync("/requests/playlist.json");
                path = FindCurrentVlcPlaylistPath(playlistDocument.RootElement);
            }

            return new MpcStatus()
            {
                Player = MediaPlayerKind.Vlc,
                Path = path,
                Time = SecondsToMPCFormat(positionSeconds),
                PositionSeconds = positionSeconds,
                DurationSeconds = durationSeconds,
                IsPlaying = string.Equals(state, "playing", StringComparison.OrdinalIgnoreCase)
            };
        }

        private async Task<JsonDocument> GetVlcJsonAsync(string path, string query = "")
        {
            string host = LoadSetting("VlcHost", "127.0.0.1");

            if (!int.TryParse(LoadSetting("VlcPort", "8080"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
                port = 8080;

            UriBuilder uri = new UriBuilder("http", host, port, path)
            {
                Query = query
            };
            string credentials = ":" + LoadSetting("VlcPassword", string.Empty);
            string authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            HttpResponseMessage? response = null;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri.Uri);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authorization);

                try
                {
                    response = await httpClient.SendAsync(request);
                    break;
                }
                catch (HttpRequestException) when (attempt < 3)
                {
                    await Task.Delay(400);
                }
            }

            if (response == null)
                throw new HttpRequestException("VLC HTTP interface is not reachable.");

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException("VLC HTTP 密碼錯誤，請到 Options 重新輸入");

            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
            }
        }

        private static int GetJsonInt32(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value)
                ? value.ToString()
                : string.Empty;
        }

        private static string FindVlcMediaPath(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (string propertyName in new[] { "url", "uri" })
                {
                    if (element.TryGetProperty(propertyName, out JsonElement value))
                    {
                        string path = ConvertVlcUriToPath(value.ToString());

                        if (!string.IsNullOrWhiteSpace(path))
                            return path;
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string path = FindVlcMediaPath(property.Value);

                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string path = FindVlcMediaPath(item);

                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
            }

            return string.Empty;
        }

        private static string FindCurrentVlcPlaylistPath(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                bool isCurrent = element.TryGetProperty("current", out JsonElement current) &&
                                 !string.IsNullOrWhiteSpace(current.ToString());

                if (isCurrent && element.TryGetProperty("uri", out JsonElement uri))
                    return ConvertVlcUriToPath(uri.ToString());

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string path = FindCurrentVlcPlaylistPath(property.Value);

                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string path = FindCurrentVlcPlaylistPath(item);

                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
            }

            return string.Empty;
        }

        private static string ConvertVlcUriToPath(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                return uri.LocalPath;

            return Path.IsPathRooted(value) ? value : string.Empty;
        }

        private string? TryParseVideoID(string filename)
        {
            string text = filename.ToUpperInvariant().Replace("_", "-").Replace(" ", "-");
            text = Regex.Replace(text, @"\[(.*?)\]", "");
            text = Regex.Replace(text, @"\((.*?)\)", "");
            text = Regex.Replace(text, @"[^A-Z0-9\-]", "-");
            text = Regex.Replace(text, @"\-+", "-").Trim('-');

            Match fc2Match = Regex.Match(text, @"FC2\-PPV\-?(\d{5,7})");

            if (fc2Match.Success)
                return "FC2-PPV-" + fc2Match.Groups[1].Value;

            Match normalMatch = Regex.Match(text, @"([A-Z]{2,10})\-?(\d{2,6})");

            if (normalMatch.Success)
                return normalMatch.Groups[1].Value + "-" + normalMatch.Groups[2].Value;

            return null;
        }

        private string ResolveVideoID(string path)
        {
            string normalizedPath = NormalizeFilePath(path);
            metadataWritePendingByPath[normalizedPath] = false;

            string? metadataId = TryReadVideoIdMetadata(path);

            if (!string.IsNullOrWhiteSpace(metadataId))
                return metadataId;

            string? parsedId = TryParseVideoID(Path.GetFileNameWithoutExtension(path));

            if (!string.IsNullOrWhiteSpace(parsedId))
            {
                if (!TryWriteVideoIdMetadata(path, parsedId))
                {
                    metadataWritePendingByPath[normalizedPath] = true;
                }

                return parsedId;
            }

            string? existingId = TryFindVideoIDByPath(path);

            if (!string.IsNullOrWhiteSpace(existingId))
            {
                if (!TryWriteVideoIdMetadata(path, existingId))
                {
                    metadataWritePendingByPath[normalizedPath] = true;
                }

                return existingId;
            }

            string temporaryId = GenerateNextTemporaryIID();

            if (!TryWriteVideoIdMetadata(path, temporaryId))
            {
                metadataWritePendingByPath[normalizedPath] = true;
            }

            return temporaryId;
        }

        private string? TryReadVideoIdMetadata(string path)
        {
            try
            {
                string streamPath = GetVideoIdMetadataStreamPath(path);

                if (!File.Exists(path))
                    return null;

                string value = File.ReadAllText(streamPath).Trim();

                if (LooksLikeVideoID(value))
                {
                    if (value.StartsWith("iid", StringComparison.OrdinalIgnoreCase))
                        return value.ToLowerInvariant();

                    return value.ToUpperInvariant();
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }

            return null;
        }

        private bool TryWriteVideoIdMetadata(string path, string videoId)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                File.WriteAllText(GetVideoIdMetadataStreamPath(path), NormalizeVideoIDForStorage(videoId));
                return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }

            return false;
        }

        private string? TryFindVideoIDByPath(string path)
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command =
                new SqliteCommand(
                    "SELECT ID FROM Videos WHERE Path = $path LIMIT 1",
                    connection
                );

            command.Parameters.AddWithValue("$path", path);

            object? result = command.ExecuteScalar();

            if (result == null || result == DBNull.Value)
                return null;

            return result.ToString();
        }

        private string GenerateNextTemporaryIID()
        {
            using SqliteConnection connection = new SqliteConnection(DatabasePath);
            connection.Open();

            using SqliteCommand command =
                new SqliteCommand(
                    "SELECT ID FROM Videos WHERE ID LIKE 'iid%'",
                    connection
                );

            using SqliteDataReader reader = command.ExecuteReader();
            int maxNumber = 0;

            while (reader.Read())
            {
                string id = reader.GetString(0);

                Match match =
                    Regex.Match(id, @"^iid(\d+)$", RegexOptions.IgnoreCase);

                if (!match.Success)
                    continue;

                int number =
                    int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

                if (number > maxNumber)
                    maxNumber = number;
            }

            return "iid" + (maxNumber + 1).ToString("D6", CultureInfo.InvariantCulture);
        }

        private static string GetVideoIdMetadataStreamPath(string path)
        {
            return path + ":VideoDB.ID";
        }

        private static string NormalizeVideoIDForStorage(string videoId)
        {
            if (videoId.StartsWith("iid", StringComparison.OrdinalIgnoreCase))
                return videoId.ToLowerInvariant();

            return videoId.ToUpperInvariant();
        }

        private static string NormalizeResolvedVideoID(string videoId)
        {
            string trimmed = videoId.Trim().Replace("_", "-");

            if (trimmed.StartsWith("iid", StringComparison.OrdinalIgnoreCase))
                return trimmed.ToLowerInvariant();

            return trimmed.ToUpperInvariant();
        }

        private bool LooksLikeVideoID(string value)
        {
            return Regex.IsMatch(
                value.Trim(),
                @"^(iid\d{6}|FC2-PPV-\d{5,7}|[A-Z]{2,10}-\d{2,6})$",
                RegexOptions.IgnoreCase
            );
        }

        private static string NormalizeFilePath(string path)
        {
            return Path.GetFullPath(path);
        }

        private int GetVideoPriority(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mkv" => 100,
                ".mp4" => 90,
                ".avi" => 50,
                ".ts" => 10,
                _ => 0
            };
        }

        private bool TryNormalizeEventTime(string input, out string normalizedTime)
        {
            normalizedTime = string.Empty;
            string[] parts = input.Trim().Split(':');

            if (parts.Length != 2 && parts.Length != 3)
                return false;

            if (!parts.All(x => int.TryParse(x, out _)))
                return false;

            normalizedTime = SecondsToMPCFormat(TimeToSeconds(input.Trim()));
            return true;
        }

        private int TimeToSeconds(string timeText)
        {
            string[] parts = timeText.Split(':');

            if (parts.Length == 2)
            {
                return int.Parse(parts[0], CultureInfo.InvariantCulture) * 60 + int.Parse(parts[1], CultureInfo.InvariantCulture);
            }

            return int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600 +
                   int.Parse(parts[1], CultureInfo.InvariantCulture) * 60 +
                   int.Parse(parts[2], CultureInfo.InvariantCulture);
        }

        private string SecondsToMPCFormat(int totalSeconds)
        {
            return TimeSpan.FromSeconds(totalSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private static IEnumerable<string> SplitTokens(string text)
        {
            return text
                .Split(new[] { ',', ';', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }
    }
}
