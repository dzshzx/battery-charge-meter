using System;
using System.IO;
using System.Reflection;

namespace BatteryChargeMeter
{
    internal static class EmbeddedUi
    {
        private static bool initialized;
        private static Assembly toolkit;

        // Called before JIT-compiling the GUI entry point. Preview harnesses
        // call the same bootstrap before reflecting over MainForm.
        internal static void Initialize()
        {
            if (initialized) return;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                if (new AssemblyName(args.Name).Name != "AntdUI") return null;
                if (toolkit != null) return toolkit;
                using (Stream input = typeof(EmbeddedUi).Assembly.GetManifestResourceStream("AntdUI.dll"))
                using (MemoryStream bytes = new MemoryStream())
                {
                    if (input == null) throw new InvalidOperationException("Missing embedded UI toolkit.");
                    input.CopyTo(bytes);
                    toolkit = Assembly.Load(bytes.ToArray());
                    return toolkit;
                }
            };
            initialized = true;
        }
    }
}
