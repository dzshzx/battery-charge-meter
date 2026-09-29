using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BatteryChargeMeter
{
    // The portable EXE carries its library assemblies (AntdUI, TaskScheduler,
    // System.CommandLine and their dependencies) as resources named
    // "lib/<assembly>.dll"; PowerMeter.csproj embeds every copy-local package
    // assembly this way instead of writing it beside the EXE.
    internal static class EmbeddedLibraries
    {
        private const string Prefix = "lib/";
        private static readonly object gate = new object();
        private static readonly Dictionary<string, Assembly> loaded =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static bool initialized;

        // Called before JIT-compiling any method that uses a library type.
        // Harnesses that load the EXE by reflection call it the same way.
        internal static void Initialize()
        {
            lock (gate)
            {
                if (initialized) return;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                initialized = true;
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            lock (gate)
            {
                Assembly assembly;
                if (loaded.TryGetValue(name, out assembly))
                    return assembly;
                using (Stream input = typeof(EmbeddedLibraries).Assembly.GetManifestResourceStream(Prefix + name + ".dll"))
                {
                    if (input == null) return null;
                    using (MemoryStream bytes = new MemoryStream())
                    {
                        input.CopyTo(bytes);
                        assembly = Assembly.Load(bytes.ToArray());
                    }
                }
                loaded[name] = assembly;
                return assembly;
            }
        }
    }
}
