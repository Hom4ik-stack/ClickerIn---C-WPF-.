using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ClickerIn.Services
{
    public interface IThemeService
    {
        void Apply(string name);
        void SetAccent(string hex);
        void SetFontSize(double size);
        IReadOnlyList<string> Themes { get; }
        string Current { get; }
    }

    public sealed class ThemeService : IThemeService
    {
        public IReadOnlyList<string> Themes { get; } = new[] { "Dark", "Light", "Blue", "Green", "Purple" };
        public string Current { get; private set; } = "Dark";

        private static readonly Dictionary<string, string[]> _themes = new Dictionary<string, string[]>
        {
            ["Dark"] = new[] { "#FF1E1E2E", "#FFE0E0E0", "#FF2D2D44", "#FF3E3E5E", "#FF2196F3", "#FF3A3A5C", "#FFE0E0E0", "#FF4A4A6A", "#FF1565C0", "#FF161625", "#FF252540", "#FF2A2A48", "#FF3A3A5C", "#FF1E1E2E", "#FF2D2D44", "#FF3A3A5C" },
            ["Light"] = new[] { "#FFF5F5F5", "#FF212121", "#FFFFFFFF", "#FFBDBDBD", "#FF2196F3", "#FFE0E0E0", "#FF212121", "#FFD0D0D0", "#FFBBDEFB", "#FFEEEEEE", "#FFFAFAFA", "#FFF0F0F0", "#FFE0E0E0", "#FFF5F5F5", "#FFFFFFFF", "#FFE3F2FD" },
            ["Blue"] = new[] { "#FF0D1B2A", "#FFE0E1DD", "#FF1B2838", "#FF415A77", "#FF448AFF", "#FF1B3A5C", "#FFE0E1DD", "#FF2A4F7A", "#FF1565C0", "#FF0A1628", "#FF162435", "#FF1B2D42", "#FF1B3A5C", "#FF0D1B2A", "#FF1B2838", "#FF1B3A5C" },
            ["Green"] = new[] { "#FF1A2421", "#FFD4E9D7", "#FF243028", "#FF4A6B50", "#FF66BB6A", "#FF2E5435", "#FFD4E9D7", "#FF3E6E45", "#FF2E7D32", "#FF14201A", "#FF1E2B22", "#FF243028", "#FF2E5435", "#FF1A2421", "#FF243028", "#FF2E5435" },
            ["Purple"] = new[] { "#FF1A1A2E", "#FFE0D8F0", "#FF252540", "#FF5C5080", "#FFAB47BC", "#FF3D2E5C", "#FFE0D8F0", "#FF503D75", "#FF7B1FA2", "#FF141424", "#FF1F1F35", "#FF252540", "#FF3D2E5C", "#FF1A1A2E", "#FF252540", "#FF3D2E5C" },
        };

        private static readonly string[] _keys = {
            "WindowBackgroundBrush", "ForegroundBrush", "PanelBackgroundBrush", "BorderBrush",
            "AccentBrush", "ButtonBackgroundBrush", "ButtonForegroundBrush", "HoverBackgroundBrush",
            "SelectedBackgroundBrush", "StatusBarBackgroundBrush", "GridBackgroundBrush",
            "GridAlternateBackgroundBrush", "GridHeaderBackgroundBrush", "ToolBarBackgroundBrush",
            "TabBackgroundBrush", "TabActiveBackgroundBrush"
        };

        private static readonly Dictionary<string, SolidColorBrush> _brushCache = new Dictionary<string, SolidColorBrush>();

        private static SolidColorBrush GetBrush(string hex)
        {
            if (!_brushCache.TryGetValue(hex, out var brush))
            {
                brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                _brushCache[hex] = brush;
            }
            return brush;
        }

        public void Apply(string name)
        {
            if (!_themes.TryGetValue(name, out var colors)) colors = _themes["Dark"];
            Current = name;
            var res = Application.Current.Resources;
            for (int i = 0; i < _keys.Length; i++)
                res[_keys[i]] = GetBrush(colors[i]);
        }

        public void SetAccent(string hex)
        {
            try { Application.Current.Resources["AccentBrush"] = GetBrush(hex); } catch { }
        }

        public void SetFontSize(double s)
        {
            s = s < 10 ? 10 : s > 22 ? 22 : s;
            var r = Application.Current.Resources;
            r["GlobalFontSize"] = s;
            r["HeaderFontSize"] = s + 3;
            r["SmallFontSize"] = s - 2;
        }
    }
}