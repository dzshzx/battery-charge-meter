using System;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    // GDI's default glyph-overhang padding differs with font size. Remove it
    // consistently so text, chart edges and controls share the same inset.
    internal class AlignedLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            if (TextAlign == ContentAlignment.MiddleRight)
                flags |= TextFormatFlags.Right;
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

            float scale = Font.SizeInPoints / 32f;
            using (Font small = new Font("Segoe UI", Font.Size * 0.48f, FontStyle.Regular, Font.Unit))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                    | TextFormatFlags.NoPrefix;
                int x = 0;
                int numberHeight = TextRenderer.MeasureText(e.Graphics, number, Font,
                    Size.Empty, flags).Height;
                int y = Math.Max(0, (Height - numberHeight) / 2);
                int smallHeight = TextRenderer.MeasureText(e.Graphics, "W", small,
                    Size.Empty, flags).Height;
                int smallY = y + numberHeight - smallHeight - (int)Math.Round(3 * scale);
                if (estimated)
                {
                    TextRenderer.DrawText(e.Graphics, "≈", small, new Point(x, smallY), ForeColor, flags);
                    x += TextRenderer.MeasureText(e.Graphics, "≈", small, Size.Empty, flags).Width
                        + (int)Math.Round(6 * scale);
                }
                TextRenderer.DrawText(e.Graphics, number, Font, new Point(x, y), ForeColor, flags);
                x += TextRenderer.MeasureText(e.Graphics, number, Font, Size.Empty, flags).Width;
                if (watts)
                    TextRenderer.DrawText(e.Graphics, "W", small,
                        new Point(x + (int)Math.Round(8 * scale), smallY), ForeColor, flags);
            }
        }
    }
}
