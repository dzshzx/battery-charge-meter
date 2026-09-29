using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using static BatteryChargeMeter.Tests.Fixtures;

namespace BatteryChargeMeter.Tests
{
    public sealed class StartupRouteTests
    {
        [Fact]
        public void Routes()
        {
                        int launches = 0;
            Func<ElevationResult> launch = delegate
            {
                launches++;
                return new ElevationResult { Started = true };
            };
            Check("ordinary GUI requests elevation once and exits parent on success", Startup.Route(StartupRoute.Parse(new string[0]), false, launch).Started && launches == 1);
            Startup.Route(StartupRoute.Parse(new string[0]), true, launch);
            Startup.Route(StartupRoute.Parse(new string[] { "--no-elevate" }), false, launch);
            Startup.Route(StartupRoute.Parse(new string[] { "--elevation-attempted" }), false, launch);
            string[][] cli = new string[][]
            {
                new string[] { "--self-test", "x" },
                new string[] { "--power-probe", "x", "2" },
                new string[] { "--third-party-notices", "x" },
                new string[] { "--screenshot", "x" },
                new string[] { "--dpi-preview", "x", "168" },
                new string[] { "--tray-preview", "x", "42", "charging" }
            };
            foreach (string[] args in cli)
            {
                StartupRoute route = StartupRoute.Parse(args);
                Check("CLI route valid without GUI: " + args[0], route.Valid && route.Command != "gui");
                Startup.Route(route, false, launch);

                StartupRoute missingOutput = StartupRoute.Parse(new string[] { args[0] });
                Check("missing CLI arguments fail without UAC: " + args[0],
                    !missingOutput.Valid && !Startup.Route(missingOutput, false, launch).Started && launches == 1);
            }
            Check("already elevated, explicit ordinary GUI, child marker and all CLI bypass UAC", launches == 1);
            string[][] invalid = new string[][]
            {
                new string[] { "--wat" },
                new string[] { "--self-test" },
                new string[] { "--no-elevate", "extra" },
                new string[] { "--power-probe", "x", "0" },
                new string[] { "--dpi-preview", "x", "bad" },
                new string[] { "--dpi-preview", "x" },
                new string[] { "--tray-preview", "x", "42" },
                new string[] { "--tray-preview", "x", "42", "bogus" },
                new string[] { "--dpi-preview", "x", "96", "96", "96" },
                new string[] { "--self-test", " " },
                new string[] { "--no-elevate", "--self-test", "x" },
                new string[] { "--self-test", "x", "--no-elevate" },
                new string[] { "--no-elevate", "--autostart" },
                new string[] { "--help" },
                new string[] { "--version" },
                new string[] { "@args.rsp" },
                new string[] { "[diagram]", "--self-test", "x" }
            };
            foreach (string[] args in invalid)
            {
                StartupRoute route = StartupRoute.Parse(args);
                Check("invalid arguments rejected: " + String.Join(" ", args), !route.Valid);
                Startup.Route(route, false, launch);
            }
            Check("invalid route cannot launch UAC", launches == 1);
            StartupRoute probe = StartupRoute.Parse(new string[] { "--POWER-PROBE", "out.txt", "12" });
            StartupRoute defaultProbe = StartupRoute.Parse(new string[] { "--power-probe", "out.txt" });
            StartupRoute dpi = StartupRoute.Parse(new string[] { "--dpi-preview", "shot.png", "168", "96" });
            StartupRoute tray = StartupRoute.Parse(new string[] { "--tray-preview", "icon.png", "99+", "Discharging" });
            Check("CLI routes carry parsed arguments for dispatch",
                probe.Valid && probe.Command == "--power-probe" && probe.Path == "out.txt" && probe.Seconds == 12
                && defaultProbe.Seconds == 5
                && dpi.Path == "shot.png" && dpi.Dpis.Length == 2 && dpi.Dpis[0] == 168 && dpi.Dpis[1] == 96
                && tray.Path == "icon.png" && tray.TrayGlyph == "99+" && tray.TrayDischarging);
            ElevationResult cancel = Startup.Launch(delegate { throw new System.ComponentModel.Win32Exception(1223); });
            ElevationResult failure = Startup.Launch(delegate { throw new InvalidOperationException("failure"); });
            Check("cancel and launch failure retain ordinary GUI with reason", !cancel.Started && cancel.Message.Contains("取消") && !failure.Started && failure.Message.Contains("failure") && !Startup.Launch(delegate { return false; }).Started);
        }

        [Fact]
        public void AutostartRoutes()
        {
            int calls = 0;
            StartupRoute route = StartupRoute.Parse(new string[] { "--autostart" });
            Startup.Route(route, false, delegate
            {
                calls++;
                return new ElevationResult { Started = true };
            });
            Check("autostart hides GUI and never requests UAC even with ordinary token",
                route.Valid && route.Command == "gui" && route.StartHidden && route.SuppressElevation && calls == 0);
            StartupRoute cleanup = StartupRoute.Parse(new string[] { "--remove-autostart" });
            Startup.Route(cleanup, false, delegate
            {
                calls++;
                return new ElevationResult { Started = true };
            });
            Check("uninstall cleanup is non-GUI and never elevates",
                cleanup.Valid && cleanup.Command != "gui" && calls == 0);
            Check("autostart rejects extra arguments",
                !StartupRoute.Parse(new string[] { "--autostart", "extra" }).Valid);
            StartupRoute sync = StartupRoute.Parse(new string[] { "--sync-autostart" });
            Startup.Route(sync, false, delegate
            {
                calls++;
                return new ElevationResult { Started = true };
            });
            Check("installer startup synchronization is non-GUI and never elevates",
                sync.Valid && sync.Command == "--sync-autostart" && calls == 0
                && !StartupRoute.Parse(new string[] { "--sync-autostart", "extra" }).Valid);
        }
    }
}
