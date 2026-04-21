using ClickerIn.Services;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;

namespace ClickerIn
{

    public partial class App : Application
    {
        public static MouseHookService MouseHook { get; private set; }
        public static ILogService Log { get; private set; }
        public static IThemeService Theme { get; private set; }
        public static ISettingsService Settings { get; private set; }
        public static IWindowManager WindowManager { get; private set; }
        public static INotificationService Notifications { get; private set; }
        public static IDpiService Dpi { get; private set; }

        private System.Windows.Forms.NotifyIcon _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Log = new LogService();
            Settings = new SettingsService();
            Theme = new ThemeService();
            WindowManager = new WindowManagerService();
            Notifications = new NotificationService();
            Dpi = new DpiService();
            MouseHook = new MouseHookService();

            var s = Settings.Load();
            Theme.Apply(s.ThemeName);
            Theme.SetFontSize(s.FontSize);
            if (!string.IsNullOrEmpty(s.AccentColor)) Theme.SetAccent(s.AccentColor);

            SetupTrayIcon();
            Log.Info("ClickerIn запущен (DPI: " + Dpi.GetSystemDpiScale().ToString("F2") + "x)");
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "ClickerIn — работает в фоне",
                Visible = true
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Открыть", null, (s, e) => ShowMainWindow());
            menu.Items.Add("Оверлей", null, (s, e) => ShowOverlay());
            menu.Items.Add("-");
            menu.Items.Add("Выход", null, (s, e) => ExitApp());
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        private System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
               
                var uri = new Uri("pack://application:,,,/pointinghand_100160.ico");
                var stream = GetResourceStream(uri);
                if (stream != null)
                    return new System.Drawing.Icon(stream.Stream);
            }
            catch { }

            try
            {
                
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string iconPath = Path.Combine(exeDir, "pointinghand_100160.ico");
                if (File.Exists(iconPath))
                    return new System.Drawing.Icon(iconPath);
            }
            catch { }

            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
            catch { }


            return System.Drawing.SystemIcons.Application;
        }

        public static void ShowMainWindow()
        {
            var mw = Current.MainWindow;
            if (mw == null)
            {
                mw = new MainWindow();
                Current.MainWindow = mw;
            }
            mw.Show();
            mw.WindowState = WindowState.Normal;
            mw.Activate();
        }

        public static void ShowOverlay()
        {
            if (Current.MainWindow is MainWindow main)
                main.ShowOverlayWindow();
        }

        public void ExitApp()
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            MouseHook?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            MouseHook?.Dispose();
            base.OnExit(e);
        }
    }
}