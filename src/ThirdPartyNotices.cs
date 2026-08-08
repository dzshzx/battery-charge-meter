using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace BatteryChargeMeter
{
    internal static class ThirdPartyNotices
    {
        public static void WriteTo(string path)
        {
            if (String.IsNullOrEmpty(path))
                throw new ArgumentException("A notice output path is required.", "path");

            string notice = ReadResource("THIRD_PARTY_NOTICE.md");
            string license = ReadResource("LGPL-2.1.txt");
            File.WriteAllText(
                path,
                notice + Environment.NewLine
                    + Environment.NewLine
                    + "---" + Environment.NewLine
                    + "GNU Lesser General Public License 2.1" + Environment.NewLine
                    + "---" + Environment.NewLine
                    + license,
                Encoding.UTF8);
        }

        private static string ReadResource(string name)
        {
            Assembly assembly = typeof(ThirdPartyNotices).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded resource: " + name);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
    }
}
