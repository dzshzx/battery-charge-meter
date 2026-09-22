using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed class StartupRoute
    {
        public string Command = "gui";
        public bool SuppressElevation;
        public bool Valid;

        public static StartupRoute Parse(string[] args)
        {
            StartupRoute route = new StartupRoute();
            if (args.Length == 0)
            {
                route.Valid = true;
                return route;
            }
            string command = args[0].ToLowerInvariant();
            if (args.Length == 1 && (command == "--no-elevate" || command == "--elevation-attempted"))
            {
                route.Valid = true;
                route.SuppressElevation = true;
                return route;
            }
            route.Command = command;
            int number;
            switch (command)
            {
                case "--self-test":
                case "--third-party-notices":
                case "--screenshot":
                    route.Valid = args.Length == 2;
                    break;
                case "--power-probe":
                    route.Valid = args.Length == 2 || (args.Length == 3
                        && Int32.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                        && number >= 1 && number <= 3600);
                    break;
                case "--dpi-preview":
                    route.Valid = args.Length == 3 || args.Length == 4;
                    for (int i = 2; route.Valid && i < args.Length; i++)
                        route.Valid = Int32.TryParse(args[i], out number) && number >= 96 && number <= 768;
                    break;
                case "--tray-preview":
                    route.Valid = args.Length == 4 && !String.IsNullOrWhiteSpace(args[2])
                        && (String.Equals(args[3], "charging", StringComparison.OrdinalIgnoreCase)
                            || String.Equals(args[3], "discharging", StringComparison.OrdinalIgnoreCase));
                    break;
            }
            if (args.Length > 1 && String.IsNullOrWhiteSpace(args[1]))
                route.Valid = false;
            return route;
        }
    }

    internal sealed class ElevationResult
    {
        public bool Started;
        public string Message;
    }

    internal static class Startup
    {
        internal static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        // OS calls are injected at this boundary; self-tests never display UAC.
        internal static ElevationResult Route(StartupRoute route, bool elevated, Func<ElevationResult> launch)
        {
            if (!route.Valid || route.Command != "gui" || elevated || route.SuppressElevation)
                return new ElevationResult { Message = elevated ? "管理员模式" : "普通模式：平台功率可能需要管理员权限。" };
            return launch();
        }

        internal static ElevationResult TryElevate()
        {
            return Launch(delegate
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = Application.ExecutablePath;
                start.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                start.UseShellExecute = true;
                start.Verb = "runas";
                start.Arguments = "--elevation-attempted";
                using (Process child = Process.Start(start))
                    return child != null;
            });
        }

        internal static ElevationResult Launch(Func<bool> start)
        {
            try
            {
                return start()
                    ? new ElevationResult { Started = true }
                    : new ElevationResult { Message = "管理员启动未成功，继续普通模式。" };
            }
            catch (Win32Exception error)
            {
                return new ElevationResult
                {
                    Message = error.NativeErrorCode == 1223
                        ? "已取消管理员授权，继续普通模式。"
                        : "管理员启动失败，继续普通模式：" + error.Message
                };
            }
            catch (Exception error)
            {
                return new ElevationResult { Message = "管理员启动失败，继续普通模式：" + error.Message };
            }
        }
    }
}
