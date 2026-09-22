using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Two-option segmented switch for the display mode. The tray menu remains
    /// the parallel path; <see cref="DpiLayout"/> manages <see cref="Font"/>,
    /// so painting reuses it instead of creating unscaled fonts.
    /// </summary>
    internal sealed class SegmentedControl : Panel
    {
        private readonly string[] items;
        private int selectedIndex;

        internal event EventHandler SelectionChanged;

        internal SegmentedControl(string first, string second)
        {
            items = new string[] { first, second };
            DoubleBuffered = true;
            BackColor = UiTheme.FooterBand;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        internal int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (value == selectedIndex)
                    return;
                selectedIndex = value;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = e.X >= Width / 2 ? 1 : 0;
            if (index == selectedIndex)
                return;
            SelectedIndex = index;
            EventHandler handler = SelectionChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath track = WidgetPath.Rounded(
                new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2))
            using (SolidBrush trackBrush = new SolidBrush(UiTheme.Chip))
                g.FillPath(trackBrush, track);

            Rectangle segment = new Rectangle(
                3 + selectedIndex * (Width / 2), 3, Width / 2 - 5, Height - 6);
            using (GraphicsPath pill = WidgetPath.Rounded(segment, (Height - 6) / 2))
            using (SolidBrush pillBrush = new SolidBrush(UiTheme.Surface))
            using (Pen pillBorder = new Pen(UiTheme.FooterRule))
            {
                g.FillPath(pillBrush, pill);
                g.DrawPath(pillBorder, pill);
            }

            for (int i = 0; i < items.Length; i++)
            {
                TextRenderer.DrawText(
                    g,
                    items[i],
                    Font,
                    new Rectangle(i * (Width / 2), 0, Width / 2, Height),
                    i == selectedIndex ? UiTheme.Ink : UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding);
            }
        }
    }
}
