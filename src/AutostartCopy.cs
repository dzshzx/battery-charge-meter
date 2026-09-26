using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace BatteryChargeMeter
{
    // The elevated logon task must only execute files that the signed-in
    // user's ordinary token cannot replace. Enabling startup copies the running
    // executable, plus a user-supplied IntelMSR.bin beside it, into a per-user
    // directory below Program Files whose explicit, non-inherited DACL grants
    // write access only to SYSTEM and Administrators.
    //
    // Invariants:
    // - Only an elevated process writes here, and it copies only its own
    //   running image; it never copies a file named by another process.
    // - A process running from a protected copy never copies from its recorded
    //   source, so logon startup cannot pick up a replaced user-writable file.
    // - source.txt records which installation owns the copy. It is only used
    //   for ownership decisions and single-instance naming, never as a file to
    //   execute or copy.
    internal sealed class ProtectedCopy
    {
        internal const string ExecutableName = "PowerMeter.exe";
        internal const string ModuleName = "IntelMSR.bin";
        internal const string SourceName = "source.txt";
        private const string RetiredMarker = ".retired-";

        private readonly string root;
        private readonly string directory;
        private readonly string executable;

        internal ProtectedCopy(string rootDirectory, string userSid)
        {
            root = Path.GetFullPath(rootDirectory);
            // The root and its parent receive an explicit DACL, so both must
            // be application-owned folders directly below Program Files.
            if (!SamePath(Path.GetDirectoryName(Path.GetDirectoryName(root)),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
                throw new ArgumentException("Protected copy root must be <Program Files>\\<folder>\\<folder>.", "rootDirectory");
            directory = Path.Combine(root, userSid);
            executable = Path.Combine(directory, ExecutableName);
        }

        // Program Files is resolved through the shell folder API, not the
        // user-controllable %ProgramFiles% environment variable.
        internal static string DefaultRoot
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Power Meter", "autostart");
            }
        }

        internal string Directory { get { return directory; } }
        internal string Executable { get { return executable; } }
        private string Module { get { return Path.Combine(directory, ModuleName); } }
        private string SourceFile { get { return Path.Combine(directory, SourceName); } }

        // A protected copy identifies itself by the installation it was taken
        // from, so launching that installation restores the running logon
        // instance instead of starting a second one. This affects only naming;
        // callers still refuse to copy whenever image and identity differ.
        internal static string IdentityFor(string image, string userSid)
        {
            string full = Path.GetFullPath(image);
            string folder = Path.GetDirectoryName(full);
            if (!String.Equals(Path.GetFileName(full), ExecutableName, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Path.GetFileName(folder), userSid, StringComparison.OrdinalIgnoreCase))
                return full;
            string source = ReadSourceFile(Path.Combine(folder, SourceName));
            return source ?? full;
        }

        internal string ReadSource()
        {
            return ReadSourceFile(SourceFile);
        }

        private static string ReadSourceFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path, Encoding.UTF8).Trim();
                return text.Length == 0 || !Path.IsPathRooted(text) ? null : Path.GetFullPath(text);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        internal bool OwnedBy(string identity)
        {
            string source = ReadSource();
            return source != null && SamePath(source, identity);
        }

        internal bool IsSelf(string image)
        {
            return SamePath(image, executable);
        }

        // True when the protected executable and optional module are byte-for-
        // byte the files beside the given image.
        internal bool Matches(string image)
        {
            if (!File.Exists(executable) || !SameBytes(image, executable))
                return false;
            string sourceModule = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(image)), ModuleName);
            bool hasSource = File.Exists(sourceModule);
            if (hasSource != File.Exists(Module))
                return false;
            return !hasSource || SameBytes(sourceModule, Module);
        }

        // Returns the number of running logon instances stopped so that the
        // caller can restart them through the scheduler.
        internal int Install(string image, string identity)
        {
            RequireElevated();
            image = Path.GetFullPath(image);
            if (IsSelf(image))
                return 0;
            if (!SamePath(image, identity))
                throw new InvalidOperationException("只能从正在运行的程序本身创建自启副本。");
            // Each level gets an explicit DACL and Administrators as owner:
            // an owner can always rewrite a DACL, so no object here may be
            // owned by the user, whatever the default-owner policy says.
            ProtectDirectory(Path.GetDirectoryName(root));
            ProtectDirectory(root);
            ProtectDirectory(directory);
            RemoveRetired();

            // Stage every file first, verify the staged bytes, then swap.
            string stagedExe = Stage(image);
            string sourceModule = Path.Combine(Path.GetDirectoryName(image), ModuleName);
            string stagedModule = File.Exists(sourceModule) ? Stage(sourceModule) : null;
            string stagedSource = Path.Combine(directory, SourceName + RetiredMarker + Guid.NewGuid().ToString("N") + ".new");
            File.WriteAllText(stagedSource, identity, new UTF8Encoding(false));
            OwnByAdministrators(stagedSource);
            int stopped = StopInstances();
            try
            {
                Swap(stagedExe, executable);
                stagedExe = null;
                if (stagedModule != null)
                {
                    Swap(stagedModule, Module);
                    stagedModule = null;
                }
                else if (File.Exists(Module))
                {
                    Retire(Module);
                }
                Swap(stagedSource, SourceFile);
                stagedSource = null;
            }
            finally
            {
                foreach (string leftover in new string[] { stagedExe, stagedModule, stagedSource })
                    if (leftover != null && File.Exists(leftover)) Retire(leftover);
            }
            if (!Matches(image) || !OwnedBy(identity))
                throw new InvalidOperationException("自启副本写入后的校验失败。");
            return stopped;
        }

        internal void Remove()
        {
            RequireElevated();
            if (!System.IO.Directory.Exists(directory))
                return;
            StopInstances();
            foreach (string path in new string[] { executable, Module, SourceFile })
                if (File.Exists(path)) Retire(path);
            RemoveRetired();
            if (File.Exists(executable) || File.Exists(SourceFile))
                throw new InvalidOperationException("自启副本删除后的校验失败。");
            // Leave no empty application folders behind; other users' copies
            // keep theirs.
            foreach (string folder in new string[] { directory, root, Path.GetDirectoryName(root) })
            {
                try
                {
                    if (System.IO.Directory.GetFileSystemEntries(folder).Length != 0)
                        break;
                    System.IO.Directory.Delete(folder);
                }
                catch (IOException) { break; }
                catch (UnauthorizedAccessException) { break; }
            }
        }

        private string Stage(string source)
        {
            byte[] bytes;
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bytes = new byte[input.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = input.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) throw new IOException("读取自启副本来源时文件被截断。");
                    offset += read;
                }
            }
            string staged = Path.Combine(directory, Path.GetFileName(source) + RetiredMarker + Guid.NewGuid().ToString("N") + ".new");
            using (FileStream output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes, 0, bytes.Length);
                output.Flush(true);
            }
            OwnByAdministrators(staged);
            if (!HashEquals(Hash(bytes), HashFile(staged)))
            {
                Retire(staged);
                throw new IOException("自启副本写入校验失败。");
            }
            return staged;
        }

        private void Swap(string staged, string target)
        {
            if (File.Exists(target))
                Retire(target);
            File.Move(staged, target);
        }

        // A running image cannot be deleted but can be renamed. The retired
        // file is deleted now when possible, otherwise at the next reboot and
        // on every later install or removal.
        private void Retire(string path)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string retired = path.IndexOf(RetiredMarker, StringComparison.Ordinal) >= 0
                ? path : path + RetiredMarker + Guid.NewGuid().ToString("N");
            if (!String.Equals(retired, path, StringComparison.Ordinal))
                File.Move(path, retired);
            try
            {
                File.Delete(retired);
            }
            catch (IOException) { DeleteAtReboot(retired); }
            catch (UnauthorizedAccessException) { DeleteAtReboot(retired); }
        }

        private void RemoveRetired()
        {
            if (!System.IO.Directory.Exists(directory))
                return;
            foreach (string path in System.IO.Directory.GetFiles(directory, "*" + RetiredMarker + "*"))
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private int StopInstances()
        {
            int stopped = 0;
            int self = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == self || !SamePath(process.MainModule.FileName, executable))
                            continue;
                        process.Kill();
                        process.WaitForExit(10000);
                        stopped++;
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            return stopped;
        }

        private static void ProtectDirectory(string path)
        {
            DirectorySecurity security = new DirectorySecurity();
            SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.SetOwner(admins);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(admins,
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, inherit, PropagationFlags.None, AccessControlType.Allow));
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path, security);
            else
                System.IO.Directory.SetAccessControl(path, security);
        }

        private static void OwnByAdministrators(string path)
        {
            FileSecurity security = new FileSecurity();
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            File.SetAccessControl(path, security);
        }

        private static void RequireElevated()
        {
            if (!Startup.IsElevated())
                throw new InvalidOperationException("写入或删除自启副本需要管理员权限。");
        }

        internal static bool SamePath(string left, string right)
        {
            return left != null && right != null && String.Equals(
                Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameBytes(string left, string right)
        {
            return HashEquals(HashFile(left), HashFile(right));
        }

        internal static byte[] HashFile(string path)
        {
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (SHA256 sha = SHA256.Create())
                return sha.ComputeHash(input);
        }

        private static byte[] Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return sha.ComputeHash(bytes);
        }

        private static bool HashEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        private static void DeleteAtReboot(string path)
        {
            // Best effort; the next install or removal deletes it otherwise.
            MoveFileEx(path, null, 4);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string replacement, int flags);
    }
}
