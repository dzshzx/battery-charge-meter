using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace BatteryChargeMeter
{
    /// <summary>
    /// The one parse of the command line. A valid route carries every argument
    /// its command needs, already converted, so the dispatcher never re-reads
    /// the raw arguments.
    /// </summary>
    internal sealed class StartupRoute
    {
        public string Command = "gui";
        public bool SuppressElevation;
        public bool Valid;
        public bool StartHidden;
        public bool Handoff;
        // Output file of --self-test, --third-party-notices, --screenshot,
        // --power-probe, --dpi-preview and --tray-preview.
        public string Path;
        public int Seconds = 5;
        public int[] Dpis;
        public string TrayGlyph;
        public bool TrayDischarging;

        public static StartupRoute Parse(string[] args)
        {
            StartupRoute route = new StartupRoute();
            // Command names are case-insensitive, as they always were.
            string[] tokens = (string[])args.Clone();
            if (tokens.Length > 0)
                tokens[0] = tokens[0].ToLowerInvariant();
            ParseResult result = StartupGrammar.Root.Parse(tokens, StartupGrammar.Configuration);
            // A GUI marker or a command must stand alone.
            System.CommandLine.Command command = result.CommandResult.Command;
            route.Valid = result.Errors.Count == 0 && (command == StartupGrammar.Root
                ? tokens.Length <= 1
                : tokens[0] == command.Name);
            if (!route.Valid)
                return route;
            if (command == StartupGrammar.Root)
            {
                route.SuppressElevation = tokens.Length == 1;
                route.StartHidden = result.GetValue(StartupGrammar.Autostart);
                route.Handoff = result.GetValue(StartupGrammar.ElevationAttempted);
                return route;
            }
            route.Command = command.Name;
            if (command.Arguments.Contains(StartupGrammar.Output))
                route.Path = result.GetValue(StartupGrammar.Output);
            if (command == StartupGrammar.PowerProbe)
                route.Seconds = result.GetValue(StartupGrammar.Seconds);
            else if (command == StartupGrammar.DpiPreview)
                route.Dpis = result.GetValue(StartupGrammar.Dpis);
            else if (command == StartupGrammar.TrayPreview)
            {
                route.TrayGlyph = result.GetValue(StartupGrammar.Glyph);
                route.TrayDischarging = String.Equals(result.GetValue(StartupGrammar.TrayMode), "discharging", StringComparison.OrdinalIgnoreCase);
            }
            return route;
        }
    }

    // The command line grammar: three GUI markers, and CLI commands spelled
    // like options that take positional arguments. Built once; parsing never
    // invokes actions, prints help or expands response files.
    internal static class StartupGrammar
    {
        internal static readonly Option<bool> NoElevate = new Option<bool>("--no-elevate");
        internal static readonly Option<bool> ElevationAttempted = new Option<bool>("--elevation-attempted");
        internal static readonly Option<bool> Autostart = new Option<bool>("--autostart");
        // Output file of --self-test, --third-party-notices, --screenshot,
        // --power-probe, --dpi-preview and --tray-preview.
        internal static readonly Argument<string> Output = NonBlank(new Argument<string>("output"));
        internal static readonly Argument<int> Seconds = Range(new Argument<int>("seconds")
        {
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = delegate { return 5; }
        }, 1, 3600);
        internal static readonly Argument<int[]> Dpis = Range(new Argument<int[]>("dpi") { Arity = new ArgumentArity(1, 2) }, 96, 768);
        internal static readonly Argument<string> Glyph = NonBlank(new Argument<string>("glyph"));
        internal static readonly Argument<string> TrayMode = OneOf(new Argument<string>("state"), "charging", "discharging");
        internal static readonly Command PowerProbe = Define("--power-probe", Output, Seconds);
        internal static readonly Command DpiPreview = Define("--dpi-preview", Output, Dpis);
        internal static readonly Command TrayPreview = Define("--tray-preview", Output, Glyph, TrayMode);
        internal static readonly RootCommand Root = BuildRoot();
        internal static readonly ParserConfiguration Configuration = new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null
        };

        private static RootCommand BuildRoot()
        {
            RootCommand root = new RootCommand();
            // No --help, --version or [directives]: unknown input is invalid.
            root.Options.Clear();
            root.Directives.Clear();
            root.Options.Add(NoElevate);
            root.Options.Add(ElevationAttempted);
            root.Options.Add(Autostart);
            root.Subcommands.Add(Define("--remove-autostart"));
            root.Subcommands.Add(Define("--sync-autostart"));
            root.Subcommands.Add(Define("--self-test", Output));
            root.Subcommands.Add(Define("--third-party-notices", Output));
            root.Subcommands.Add(Define("--screenshot", Output));
            root.Subcommands.Add(PowerProbe);
            root.Subcommands.Add(DpiPreview);
            root.Subcommands.Add(TrayPreview);
            // The GUI is the root itself; without an action the parser would
            // demand a subcommand. Nothing here is ever invoked.
            root.SetAction(delegate(ParseResult result) { return 0; });
            return root;
        }

        private static Command Define(string name, params Argument[] arguments)
        {
            Command command = new Command(name);
            foreach (Argument argument in arguments)
                command.Arguments.Add(argument);
            return command;
        }

        private static Argument<string> NonBlank(Argument<string> argument)
        {
            argument.Validators.Add(delegate(ArgumentResult result)
            {
                if (String.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                    result.AddError(argument.Name + " must not be blank.");
            });
            return argument;
        }

        private static Argument<string> OneOf(Argument<string> argument, params string[] allowed)
        {
            argument.Validators.Add(delegate(ArgumentResult result)
            {
                string value = result.GetValueOrDefault<string>();
                if (Array.FindIndex(allowed, delegate(string item) { return String.Equals(item, value, StringComparison.OrdinalIgnoreCase); }) < 0)
                    result.AddError(argument.Name + " is not one of: " + String.Join(", ", allowed));
            });
            return argument;
        }

        private static Argument<T> Range<T>(Argument<T> argument, int minimum, int maximum)
        {
            argument.Validators.Add(delegate(ArgumentResult result)
            {
                foreach (Token token in result.Tokens)
                {
                    int number;
                    if (!Int32.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                        || number < minimum || number > maximum)
                        result.AddError(argument.Name + " must be an integer from " + minimum + " to " + maximum + ".");
                }
            });
            return argument;
        }
    }

    internal sealed class ElevationResult
    {
        public bool Started;
        public string Message;
    }

    internal static class Startup
    {
        private static string identity;

        // The installation this process represents: its own path, or for the
        // protected logon copy the installation it was copied from. Single-
        // instance locks and restore messages use it, so a manual launch
        // restores the running logon instance.
        internal static string Identity
        {
            get
            {
                if (identity == null)
                    using (WindowsIdentity user = WindowsIdentity.GetCurrent())
                        identity = ProtectedCopy.IdentityFor(Application.ExecutablePath, user.User.Value);
                return identity;
            }
        }

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
