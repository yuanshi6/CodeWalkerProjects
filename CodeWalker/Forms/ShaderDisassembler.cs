using CodeWalker.GameFiles;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CodeWalker.Forms
{
    // Locates dxc.exe / fxc.exe and runs '-dumpbin -Fc' to produce HLSL-style
    // disassembly listings. Used by FxcForm for AWC shader library entries.
    internal static class ShaderDisassembler
    {
        private static string _dxcPath;
        private static string _fxcPath;
        private static bool _dxcResolved;
        private static bool _fxcResolved;

        // Local override locations (checked first so a user can drop tools next
        // to CodeWalker.exe without touching the SDK).
        private static readonly string[] CandidateSubdirs = new[]
        {
            "",
            "tools",
            "dxcompilers",
            "tools\\dxcompilers",
        };

        public static string DxcPath => _dxcResolved ? _dxcPath : (_dxcPath = Resolve("dxc.exe", out _dxcResolved));
        public static string FxcPath => _fxcResolved ? _fxcPath : (_fxcPath = Resolve("fxc.exe", out _fxcResolved));

        private static string Resolve(string exeName, out bool resolved)
        {
            resolved = true;

            foreach (var sub in CandidateSubdirs)
            {
                var rel = string.IsNullOrEmpty(sub) ? exeName : Path.Combine(sub, exeName);
                var p = PathUtil.GetFilePath(rel);
                if (File.Exists(p)) return p;
            }

            var sdk = ResolveFromWindowsSdk(exeName);
            if (sdk != null) return sdk;

            // Let the OS resolve via PATH as a final fallback.
            return exeName;
        }

        private static string ResolveFromWindowsSdk(string exeName)
        {
            // Standard Windows 10/11 SDK install layout:
            //   C:\Program Files (x86)\Windows Kits\10\bin\<version>\<arch>\<exe>
            string[] roots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    "Windows Kits", "10", "bin"),
            };
            string arch = Environment.Is64BitProcess ? "x64" : "x86";

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                // Prefer the newest 10.0.* sub-directory (lexicographic order works for SDK versions).
                var versionDirs = Directory.GetDirectories(root, "10.0.*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);
                foreach (var ver in versionDirs)
                {
                    var p = Path.Combine(ver, arch, exeName);
                    if (File.Exists(p)) return p;
                }

                // A few SDK installs drop the binaries directly under bin\<arch>.
                var direct = Path.Combine(root, arch, exeName);
                if (File.Exists(direct)) return direct;
            }
            return null;
        }

        public static string Disassemble(byte[] binary, string shaderName, out string error)
        {
            error = null;
            if (binary == null || binary.Length < 4)
            {
                error = "Empty shader binary.";
                return null;
            }

            string tmpIn = Path.Combine(Path.GetTempPath(), "cw_disasm_" + Guid.NewGuid().ToString("N") + ".cso");
            string tmpOut = tmpIn + ".asm";
            try
            {
                File.WriteAllBytes(tmpIn, binary);

                // Try dxc first (DXIL / SM6+). Fall back to fxc (DXBC / SM5).
                var asm = TryRun(DxcPath, "-dumpbin -Fc \"" + tmpOut + "\" \"" + tmpIn + "\"", tmpOut, out var dxcErr);
                if (!string.IsNullOrEmpty(asm)) return asm;

                asm = TryRun(FxcPath, "/dumpbin /Fc \"" + tmpOut + "\" \"" + tmpIn + "\"", tmpOut, out var fxcErr);
                if (!string.IsNullOrEmpty(asm)) return asm;

                error = "Disassembly failed for " + shaderName + "."
                      + (string.IsNullOrEmpty(dxcErr) ? string.Empty : "\r\n  dxc: " + dxcErr.Trim())
                      + (string.IsNullOrEmpty(fxcErr) ? string.Empty : "\r\n  fxc: " + fxcErr.Trim());
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            finally
            {
                try { if (File.Exists(tmpIn)) File.Delete(tmpIn); } catch { }
                try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch { }
            }
        }

        private static string TryRun(string exe, string args, string outFile, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);

                    if (File.Exists(outFile))
                    {
                        var text = File.ReadAllText(outFile);
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                    error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return null;
                }
            }
            catch (Exception ex)
            {
                error = exe + ": " + ex.Message;
                return null;
            }
        }
    }
}
