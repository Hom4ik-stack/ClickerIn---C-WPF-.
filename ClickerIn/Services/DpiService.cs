using System;
using System.Runtime.InteropServices;
using ClickerIn.Helpers;

namespace ClickerIn.Services
{
    public interface IDpiService
    {
        double GetSystemDpiScale();
        double GetDpiScaleForWindow(IntPtr hWnd);
        (int x, int y) AdjustCoordinates(int x, int y, double recordedDpi, double currentDpi);
        (int width, int height) AdjustSize(int width, int height, double recordedDpi, double currentDpi);
    }

    public sealed class DpiService : IDpiService
    {
        private const int MDT_EFFECTIVE_DPI = 0;
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        private bool _initialized;

        public DpiService()
        {
            EnsureDpiAware();
        }

        private void EnsureDpiAware()
        {
            if (_initialized) return;
            try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
            catch
            {
                try { SetProcessDPIAware(); }
                catch { }
            }
            _initialized = true;
        }

        public double GetSystemDpiScale()
        {
            try
            {
                int dpi = GetDpiForSystem();
                return dpi / 96.0;
            }
            catch
            {
                try
                {
                    using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                        return g.DpiX / 96.0;
                }
                catch { return 1.0; }
            }
        }

        public double GetDpiScaleForWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return GetSystemDpiScale();
            try
            {
                int dpi = GetDpiForWindow(hWnd);
                if (dpi > 0) return dpi / 96.0;
            }
            catch { }
            try
            {
                IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    int hr = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
                    if (hr == 0 && dpiX > 0) return dpiX / 96.0;
                }
            }
            catch { }
            return GetSystemDpiScale();
        }

        public (int x, int y) AdjustCoordinates(int x, int y, double recordedDpi, double currentDpi)
        {
            if (Math.Abs(recordedDpi - currentDpi) < 0.01) return (x, y);
            if (recordedDpi <= 0) recordedDpi = 1.0;
            double ratio = currentDpi / recordedDpi;
            return ((int)(x * ratio), (int)(y * ratio));
        }

        public (int width, int height) AdjustSize(int width, int height, double recordedDpi, double currentDpi)
        {
            if (Math.Abs(recordedDpi - currentDpi) < 0.01) return (width, height);
            if (recordedDpi <= 0) recordedDpi = 1.0;
            double ratio = currentDpi / recordedDpi;
            return ((int)(width * ratio), (int)(height * ratio));
        }
    }
}