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
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleName = "功率显示口径";
            AccessibleRole = AccessibleRole.List;
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
            if (e.Button != MouseButtons.Left)
                return;
            Focus();
            int index = e.X >= Width / 2 ? 1 : 0;
            SelectItem(index);
        }

        private void SelectItem(int index)
        {
            if (index == selectedIndex)
                return;
            SelectedIndex = index;
            EventHandler handler = SelectionChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Space)
            {
                SelectItem(e.KeyCode == Keys.Left ? 0 : e.KeyCode == Keys.Right ? 1 : 1 - selectedIndex);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Height / 28f;
            int radius = Math.Max(2, (int)Math.Round(5 * scale));
            using (GraphicsPath track = WidgetPath.Rounded(
                new Rectangle(0, 0, Width - 1, Height - 1), radius))
            using (SolidBrush trackBrush = new SolidBrush(UiTheme.Chip))
                g.FillPath(trackBrush, track);

            int pad = Math.Max(2, (int)Math.Round(2 * scale));
            int pillWidth = Width / 2 - 2 * pad;
            int pillX = selectedIndex == 0 ? pad : Width - pad - pillWidth;
            Rectangle segment = new Rectangle(pillX, pad, pillWidth, Height - 2 * pad);
            using (GraphicsPath pill = WidgetPath.Rounded(segment, radius))
            using (SolidBrush pillBrush = new SolidBrush(UiTheme.Surface))
            using (Pen pillBorder = new Pen(UiTheme.FooterRule))
            {
                g.FillPath(pillBrush, pill);
                g.DrawPath(pillBorder, pill);
            }
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(segment, -pad, -pad));

            for (int i = 0; i < items.Length; i++)
            {
                TextRenderer.DrawText(
                    g,
                    Strings.Get(items[i]),
                    Font,
                    new Rectangle(i * (Width / 2), 0, Width / 2, Height),
                    i == selectedIndex ? UiTheme.Ink : UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding);
            }
        }
    }
}
