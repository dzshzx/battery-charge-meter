using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed partial class MainForm
    {
        public void RenderPreview(string path)
        {
            ShowPreviewWindow();
            CapturePreview(path, "DeviceDpi=" + ReadWindowDpi().ToString(CultureInfo.InvariantCulture));
            Hide();
        }

        public void RenderDpiTransitionPreview(string path, params int[] targetDpis)
        {
            if (targetDpis == null || targetDpis.Length == 0)
                throw new ArgumentException("At least one target DPI is required.", "targetDpis");

            ShowPreviewWindow();
            foreach (int targetDpi in targetDpis)
                SendDpiTransition(targetDpi);

            CapturePreview(
                path,
                "HandledDpi=" + dpiLayout.CurrentDpi.ToString(CultureInfo.InvariantCulture));
            Hide();
        }

        private void SendDpiTransition(int targetDpi)
        {
            if (targetDpi < 96 || targetDpi > 768)
                throw new ArgumentOutOfRangeException("targetDpi");

            NativeMethods.Rect suggested;
            NativeMethods.GetWindowRect(Handle, out suggested);
            float transitionScale = targetDpi / (float)Math.Max(96, dpiLayout.CurrentDpi);
            suggested.Left += 17;
            suggested.Top += 13;
            suggested.Right = suggested.Left + DpiLayout.ScaleValue(Width, transitionScale);
            suggested.Bottom = suggested.Top + DpiLayout.ScaleValue(Height, transitionScale);

            IntPtr suggestedPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMethods.Rect)));
            try
            {
                Marshal.StructureToPtr(suggested, suggestedPointer, false);
                long packedDpi = (long)(
                    (uint)(targetDpi & 0xffff) |
                    ((uint)(targetDpi & 0xffff) << 16));
                NativeMethods.SendMessage(
                    Handle,
                    NativeMethods.WmDpiChanged,
                    new IntPtr(packedDpi),
                    suggestedPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(suggestedPointer);
            }

            Application.DoEvents();
        }

        private void ShowPreviewWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Left + 20,
                Screen.PrimaryScreen.WorkingArea.Top + 20);
            Show();
            Application.DoEvents();
            RefreshReading();
            Application.DoEvents();
        }

        private void CapturePreview(string path, string dpiMetadata)
        {
            NativeMethods.Rect rectangle;
            NativeMethods.GetWindowRect(Handle, out rectangle);
            int captureWidth = Math.Max(1, rectangle.Right - rectangle.Left);
            int captureHeight = Math.Max(1, rectangle.Bottom - rectangle.Top);
            using (Bitmap bitmap = new Bitmap(captureWidth, captureHeight))
            {
                bool printed;
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr deviceContext = graphics.GetHdc();
                    try
                    {
                        printed = NativeMethods.PrintWindow(Handle, deviceContext, 2);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(deviceContext);
                    }
                }

                if (!printed)
                    DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(path, ImageFormat.Png);
            }

            bool positionMatched = rectangle.Left == lastSuggestedPosition.X
                && rectangle.Top == lastSuggestedPosition.Y;
            File.WriteAllText(
                path + ".txt",
                String.Format(
                    CultureInfo.InvariantCulture,
                    "{0}; Form={1}x{2}; Client={3}x{4}; Content={5}x{6}; "
                        + "WindowRect={7}x{8}; FormFontPixels={9:0.###}; "
                        + "PowerFontPixels={10:0.###}; AutoScroll={11}; "
                        + "WindowPositionApplied={12}; WindowPositionMatched={13}; ErrorArea={14}x{15}",
                    dpiMetadata,
                    Width,
                    Height,
                    ClientSize.Width,
                    ClientSize.Height,
                    AutoScrollMinSize.Width,
                    AutoScrollMinSize.Height,
                    captureWidth,
                    captureHeight,
                    Font.Size,
                    powerLabel.Font.Size,
                    AutoScroll,
                    lastDpiWindowPositionApplied,
                    positionMatched,
                    errorLabel.Width,
                    errorLabel.Height));
        }

        public void RenderTrayIconPreview(string path, string glyph, bool discharging)
        {
            Color color = discharging ? trayDischargeColor : Color.White;
            using (Icon icon = CreateTextIcon(glyph, color))
            using (Bitmap bitmap = icon.ToBitmap())
                bitmap.Save(path, ImageFormat.Png);
        }
    }
}
