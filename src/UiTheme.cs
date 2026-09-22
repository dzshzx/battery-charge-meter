using System.Drawing;
using System.Drawing.Drawing2D;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Single palette for the light single-surface window, the custom widgets
    /// and the tray badge. Semantic hues are reserved for the supply state and
    /// errors; everything structural is neutral.
    /// </summary>
    internal static class UiTheme
    {
        internal static readonly Color Surface = Color.White;
        internal static readonly Color FooterBand = Color.FromArgb(247, 248, 249);
        internal static readonly Color Hairline = Color.FromArgb(236, 236, 236);
        internal static readonly Color FooterRule = Color.FromArgb(229, 229, 229);
        internal static readonly Color Ink = Color.FromArgb(27, 27, 27);
        internal static readonly Color Muted = Color.FromArgb(97, 97, 97);
        internal static readonly Color Faint = Color.FromArgb(140, 140, 140);
        internal static readonly Color Track = Color.FromArgb(234, 234, 234);
        internal static readonly Color ZeroLine = Color.FromArgb(200, 200, 200);
        internal static readonly Color Chip = Color.FromArgb(234, 236, 238);

        internal static readonly Color Charging = Color.FromArgb(5, 150, 105);
        internal static readonly Color Discharging = Color.FromArgb(217, 119, 6);
        internal static readonly Color ErrorRed = Color.FromArgb(220, 38, 38);
        internal static readonly Color TrayNeutral = Color.FromArgb(107, 114, 128);

        /// <summary>
        /// Accent for readings and state text. Idle carries no hue: with no
        /// battery flow there is nothing to signal, so it stays ink.
        /// </summary>
        internal static Color Accent(BatteryAccentKind kind)
        {
            switch (kind)
            {
                case BatteryAccentKind.Charging:
                    return Charging;
                case BatteryAccentKind.Discharging:
                    return Discharging;
                case BatteryAccentKind.Error:
                    return ErrorRed;
                default:
                    return Ink;
            }
        }

        internal static Color TrayTile(BatteryAccentKind kind)
        {
            switch (kind)
            {
                case BatteryAccentKind.Charging:
                    return Charging;
                case BatteryAccentKind.Discharging:
                    return Discharging;
                default:
                    return TrayNeutral;
            }
        }
    }

    internal static class WidgetPath
    {
        internal static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
