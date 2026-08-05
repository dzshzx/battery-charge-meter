using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed class DpiLayout : IDisposable
    {
        private sealed class FontBaseline
        {
            public Control Control;
            public string FamilyName;
            public float SizeInPoints;
            public FontStyle Style;
        }

        private readonly Form form;
        private readonly Size designClientSize;
        private readonly Dictionary<Control, Rectangle> designBounds =
            new Dictionary<Control, Rectangle>();
        private readonly List<FontBaseline> fontBaselines = new List<FontBaseline>();
        private Dictionary<Control, Font> appliedFonts = new Dictionary<Control, Font>();
        private bool disposed;

        public DpiLayout(Form form, Size designClientSize)
        {
            if (form == null)
                throw new ArgumentNullException("form");

            this.form = form;
            this.designClientSize = designClientSize;
            CaptureFont(form);
            CaptureControls(form.Controls);
        }

        public int CurrentDpi { get; private set; }

        public void Apply(int dpi)
        {
            if (disposed)
                throw new ObjectDisposedException("DpiLayout");
            if (dpi <= 0)
                dpi = 96;
            if (CurrentDpi == dpi)
                return;

            float scale = dpi / 96f;
            Dictionary<Control, Font> nextFonts = CreateFonts(dpi);

            form.SuspendLayout();
            try
            {
                form.ClientSize = new Size(
                    ScaleValue(designClientSize.Width, scale),
                    ScaleValue(designClientSize.Height, scale));

                foreach (FontBaseline baseline in fontBaselines)
                    baseline.Control.Font = nextFonts[baseline.Control];

                foreach (KeyValuePair<Control, Rectangle> item in designBounds)
                {
                    Rectangle baseline = item.Value;
                    Control control = item.Key;
                    Point location = new Point(
                        ScaleValue(baseline.X, scale),
                        ScaleValue(baseline.Y, scale));

                    if (control.AutoSize)
                    {
                        control.Location = location;
                    }
                    else
                    {
                        control.Bounds = new Rectangle(
                            location,
                            new Size(
                                ScaleValue(baseline.Width, scale),
                                ScaleValue(baseline.Height, scale)));
                    }
                }

                CurrentDpi = dpi;
            }
            finally
            {
                form.ResumeLayout(true);
            }

            DisposeFonts(appliedFonts);
            appliedFonts = nextFonts;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            DisposeFonts(appliedFonts);
            appliedFonts.Clear();
        }

        internal static int ScaleValue(int value, float scale)
        {
            return (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }

        private void CaptureControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                designBounds[control] = control.Bounds;
                CaptureFont(control);
                if (control.HasChildren)
                    CaptureControls(control.Controls);
            }
        }

        private void CaptureFont(Control control)
        {
            Font font = control.Font;
            fontBaselines.Add(new FontBaseline
            {
                Control = control,
                FamilyName = font.FontFamily.Name,
                SizeInPoints = font.SizeInPoints,
                Style = font.Style
            });
        }

        private Dictionary<Control, Font> CreateFonts(int dpi)
        {
            Dictionary<Control, Font> fonts = new Dictionary<Control, Font>();
            try
            {
                foreach (FontBaseline baseline in fontBaselines)
                {
                    float pixelSize = baseline.SizeInPoints * dpi / 72f;
                    fonts.Add(
                        baseline.Control,
                        new Font(
                            baseline.FamilyName,
                            pixelSize,
                            baseline.Style,
                            GraphicsUnit.Pixel));
                }
                return fonts;
            }
            catch
            {
                DisposeFonts(fonts);
                throw;
            }
        }

        private static void DisposeFonts(Dictionary<Control, Font> fonts)
        {
            foreach (Font font in fonts.Values)
                font.Dispose();
        }
    }
}
