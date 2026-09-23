using System;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    // GDI's default glyph-overhang padding differs with font size. Remove it
    // consistently so labels and controls share the same inset.
    internal class AlignedLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            if (TextAlign == ContentAlignment.MiddleRight)
                flags |= TextFormatFlags.Right;
            else if (TextAlign == ContentAlignment.MiddleCenter)
                flags |= TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
        }
    }

    internal sealed class PowerReadout : AlignedLabel
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            bool watts = Text.EndsWith(" W", StringComparison.Ordinal);
            bool estimated = Text.StartsWith("≈ ", StringComparison.Ordinal);
            string number = watts ? Text.Substring(0, Text.Length - 2) : Text;
            if (estimated)
                number = number.Substring(2);

            float scale = Font.SizeInPoints / 44f;
            using (Font small = new Font("Segoe UI", Font.Size * 0.34f, FontStyle.Regular, Font.Unit))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                    | TextFormatFlags.NoPrefix;
                Size numberSize = TextRenderer.MeasureText(e.Graphics, number, Font, Size.Empty, flags);
                int prefixWidth = estimated ? TextRenderer.MeasureText(e.Graphics, "≈", small, Size.Empty, flags).Width
                    + (int)Math.Round(8 * scale) : 0;
                int unitWidth = watts ? TextRenderer.MeasureText(e.Graphics, "W", small, Size.Empty, flags).Width
                    + (int)Math.Round(8 * scale) : 0;
                int x = TextAlign == ContentAlignment.MiddleCenter
                    ? Math.Max(0, (Width - numberSize.Width - prefixWidth - unitWidth) / 2) : 0;
                int numberHeight = numberSize.Height;
                int y = Math.Max(0, (Height - numberHeight) / 2);
                int smallHeight = TextRenderer.MeasureText(e.Graphics, "W", small,
                    Size.Empty, flags).Height;
                int smallY = y + (int)Math.Round(Ascent(Font, e.Graphics) - Ascent(small, e.Graphics));
                if (estimated)
                {
                    TextRenderer.DrawText(e.Graphics, "≈", small,
                        new Point(x, y + (numberHeight - smallHeight) / 2), ForeColor, flags);
                    x += prefixWidth;
                }
                TextRenderer.DrawText(e.Graphics, number, Font, new Point(x, y), ForeColor, flags);
                x += TextRenderer.MeasureText(e.Graphics, number, Font, Size.Empty, flags).Width;
                if (watts)
                    TextRenderer.DrawText(e.Graphics, "W", small,
                        new Point(x + (int)Math.Round(8 * scale), smallY), ForeColor, flags);
            }
        }

        private static float Ascent(Font font, Graphics graphics)
        {
            FontFamily family = font.FontFamily;
            return font.GetHeight(graphics) * family.GetCellAscent(font.Style) / family.GetLineSpacing(font.Style);
        }
    }
}
