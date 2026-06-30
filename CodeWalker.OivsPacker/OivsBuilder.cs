using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CodeWalker.OivsPacker
{
    // ============================================================================
    // Turns a OivsProject into a .oivs package (Model B). C# port of build_oivs.py:
    //   - each module/option's .oiv content is extracted into content/<ns>/oiv/,
    //     and its <content> operations are inlined with source= re-namespaced;
    //   - <folder> sources are copied into content/<ns>/<sub>/;
    //   - preview images are downscaled and bundled under media/;
    //   - one super.xml manifest + icon.png at the root; zipped with '/' entries.
    // ns = module id, or "<groupId>_<optionId>" for group options.
    // ============================================================================

    public class OivsBuilder
    {
        public int MaxImageWidth = 1920;   // downscale previews wider than this
        public int MaxIconWidth = 256;

        private readonly Action<string> _log;
        public OivsBuilder(Action<string> log = null) { _log = log ?? (_ => { }); }

        public void Build(OivsProject project, string outputPath)
        {
            string work = Path.Combine(Path.GetTempPath(), "OivsPacker_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                Directory.CreateDirectory(Path.Combine(work, "content"));
                string mediaDir = Path.Combine(work, "media");

                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sb.AppendLine("<!-- Built with OIVS Packer -->");
                sb.AppendLine($"<superpackage version=\"1.0\" id=\"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}\" target=\"Five\">");
                var gv = (project.Meta.GameVersion ?? "").Trim().ToLowerInvariant();
                if (gv == "enhanced" || gv == "legacy") sb.AppendLine($"<gameversion>{gv}</gameversion>");
                AppendMetadata(sb, project.Meta);

                // modules
                sb.AppendLine("\n<modules>");
                foreach (var m in project.Modules)
                {
                    _log($"Module: {m.Name}");
                    string req = m.Required ? "true" : "false";
                    string extra = m.Required ? "" : $" default=\"{(m.Default ? "true" : "false")}\"";
                    sb.AppendLine($"<module id=\"{Esc(m.Id)}\" required=\"{req}\"{extra} name=\"{Esc(m.Name)}\">");
                    sb.AppendLine($"  <description><![CDATA[{m.Description}]]></description>");
                    sb.Append(MediaXml(m, m.Id, work, mediaDir, "  "));
                    sb.AppendLine("  <install>");
                    sb.Append(InstallXml(m, m.Id, work, "    "));
                    sb.AppendLine("  </install>");
                    sb.AppendLine("</module>");
                }
                sb.AppendLine("</modules>");

                // groups
                if (project.Groups.Count > 0)
                {
                    sb.AppendLine("\n<groups>");
                    foreach (var g in project.Groups)
                    {
                        _log($"Group: {g.Title}");
                        string def = string.IsNullOrEmpty(g.Default) ? "none" : g.Default;
                        sb.AppendLine($"<group id=\"{Esc(g.Id)}\" type=\"single\" title=\"{Esc(g.Title)}\" allowNone=\"{(g.AllowNone ? "true" : "false")}\" default=\"{Esc(def)}\">");
                        sb.AppendLine($"  <description><![CDATA[{g.Description}]]></description>");
                        foreach (var o in g.Options)
                        {
                            string ns = g.Id + "_" + o.Id;
                            sb.AppendLine($"  <option id=\"{Esc(o.Id)}\" name=\"{Esc(o.Name)}\">");
                            sb.AppendLine($"    <description><![CDATA[{o.Description}]]></description>");
                            sb.Append(MediaXml(o, ns, work, mediaDir, "    "));
                            sb.AppendLine("    <install>");
                            sb.Append(InstallXml(o, ns, work, "      "));
                            sb.AppendLine("    </install>");
                            sb.AppendLine("  </option>");
                        }
                        sb.AppendLine("</group>");
                    }
                    sb.AppendLine("</groups>");
                }

                sb.AppendLine("</superpackage>");

                File.WriteAllText(Path.Combine(work, "super.xml"), sb.ToString(), new UTF8Encoding(false));

                // icon
                if (!string.IsNullOrWhiteSpace(project.Meta.IconPath) && File.Exists(project.Meta.IconPath))
                    SaveImage(project.Meta.IconPath, Path.Combine(work, "icon.png"), MaxIconWidth);

                // zip with forward-slash entries
                _log("Zipping…");
                if (File.Exists(outputPath)) File.Delete(outputPath);
                using (var fs = File.Open(outputPath, FileMode.CreateNew))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    string baseDir = work.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    foreach (var file in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(baseDir.Length).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
                    }
                }
                _log($"Done: {outputPath} ({new FileInfo(outputPath).Length / 1024.0 / 1024.0:N1} MB)");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        // ---- metadata --------------------------------------------------------

        private void AppendMetadata(StringBuilder sb, PackMeta m)
        {
            sb.AppendLine("<metadata>");
            sb.AppendLine($"<name>{Esc(m.Name)}</name>");
            sb.AppendLine($"<version><major>{m.VersionMajor}</major><minor>{m.VersionMinor}</minor></version>");
            sb.AppendLine("<author>");
            sb.AppendLine($"<displayName>{Esc(m.AuthorName)}</displayName>");
            if (!string.IsNullOrWhiteSpace(m.ActionLink)) sb.AppendLine($"<actionlink>{Esc(m.ActionLink)}</actionlink>");
            if (!string.IsNullOrWhiteSpace(m.Web)) sb.AppendLine($"<web>{Esc(m.Web)}</web>");
            if (!string.IsNullOrWhiteSpace(m.Youtube)) sb.AppendLine($"<youtube linkKind=\"channel\">{Esc(m.Youtube)}</youtube>");
            sb.AppendLine("</author>");
            sb.AppendLine($"<description><![CDATA[{m.Description}]]></description>");
            if (!string.IsNullOrWhiteSpace(m.License)) sb.AppendLine($"<licence>{Esc(m.License)}</licence>");
            sb.AppendLine("</metadata>");
            sb.AppendLine("<colors>");
            sb.AppendLine($"<headerBackground useBlackTextColor=\"{(m.UseBlackTextColor ? "True" : "False")}\">{Esc(m.HeaderBackground)}</headerBackground>");
            sb.AppendLine($"<iconBackground>{Esc(m.HeaderBackground)}</iconBackground>");
            sb.AppendLine("</colors>");
        }

        // ---- install block (content ops + folder verbs) ----------------------

        private string InstallXml(PackInstallable item, string ns, string work, string pad)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(item.OivPath) && File.Exists(item.OivPath))
            {
                string ops = ExtractOivContent(item.OivPath, ns, work);
                sb.AppendLine($"{pad}<content>");
                // indent the inner ops one level
                foreach (var line in ops.Replace("\r\n", "\n").Split('\n'))
                    sb.AppendLine(string.IsNullOrWhiteSpace(line) ? line : pad + "  " + line);
                sb.AppendLine($"{pad}</content>");
            }
            int fi = 0;
            foreach (var f in item.Folders)
            {
                if (string.IsNullOrWhiteSpace(f.Path) || !Directory.Exists(f.Path)) continue;
                string sub = string.IsNullOrWhiteSpace(f.Sub) ? $"folder{++fi}" : f.Sub;
                string nsDir = Path.Combine(work, "content", ns);
                CopyFolder(f.Path, Path.Combine(nsDir, sub));
                sb.AppendLine($"{pad}<folder source=\"{Esc(ns + "\\" + sub)}\"/>");
            }
            return sb.ToString();
        }

        /// <summary>Extracts an .oiv's content into content/&lt;ns&gt;/oiv and returns its
        /// &lt;content&gt; operations with source= re-namespaced.</summary>
        private string ExtractOivContent(string oivPath, string ns, string work)
        {
            string dest = Path.Combine(work, "content", ns, "oiv");
            string asm = null;
            using (var z = ZipFile.OpenRead(oivPath))
            {
                foreach (var e in z.Entries)
                {
                    string nn = e.FullName.Replace('\\', '/');
                    if (nn.Equals("assembly.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        using var sr = new StreamReader(e.Open());
                        asm = sr.ReadToEnd();
                    }
                    else if (nn.StartsWith("content/", StringComparison.OrdinalIgnoreCase) && !nn.EndsWith("/"))
                    {
                        string rel = nn.Substring("content/".Length).Replace('/', Path.DirectorySeparatorChar);
                        string outp = Path.Combine(dest, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(outp));
                        e.ExtractToFile(outp, true);
                    }
                }
            }
            if (asm == null) throw new InvalidDataException($"no assembly.xml in {Path.GetFileName(oivPath)}");

            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.LoadXml(asm.TrimStart('﻿'));
            var content = doc.SelectSingleNode("/package/content") ?? doc.SelectSingleNode("//content");
            if (content == null) return "";
            string pref = ns + "\\oiv\\";
            foreach (XmlNode el in content.SelectNodes(".//*"))
            {
                var src = el.Attributes?["source"];
                if (src != null) src.Value = pref + src.Value.TrimStart('\\', '/');
            }
            return content.InnerXml;
        }

        // ---- media -----------------------------------------------------------

        private string MediaXml(PackInstallable item, string ns, string work, string mediaDir, string pad)
        {
            if (item.Media.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"{pad}<media>");
            int ci = 0, ii = 0;
            foreach (var md in item.Media)
            {
                if (md.IsCompare)
                {
                    ci++;
                    string b = BundleImage(md.BeforePath, mediaDir, $"{ns}_cmp{ci}_b");
                    string a = BundleImage(md.AfterPath, mediaDir, $"{ns}_cmp{ci}_a");
                    if (b == null || a == null) continue;
                    string t = string.IsNullOrWhiteSpace(md.Title) ? "" : $" title=\"{Esc(md.Title)}\"";
                    sb.AppendLine($"{pad}  <compare{t} before=\"{b}\" after=\"{a}\"/>");
                }
                else
                {
                    ii++;
                    string s = BundleImage(md.ImagePath, mediaDir, $"{ns}_img{ii}");
                    if (s == null) continue;
                    string c = string.IsNullOrWhiteSpace(md.Title) ? "" : $" caption=\"{Esc(md.Title)}\"";
                    sb.AppendLine($"{pad}  <image src=\"{s}\"{c}/>");
                }
            }
            sb.AppendLine($"{pad}</media>");
            return sb.ToString();
        }

        /// <summary>Downscales and copies an image into media/, returns its "media/x.png"
        /// reference (or null if the source is missing).</summary>
        private string BundleImage(string srcPath, string mediaDir, string baseName)
        {
            if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath)) return null;
            Directory.CreateDirectory(mediaDir);
            string outp = Path.Combine(mediaDir, baseName + ".png");
            SaveImage(srcPath, outp, MaxImageWidth);
            return "media/" + baseName + ".png";
        }

        private void SaveImage(string srcPath, string destPath, int maxW)
        {
            using var src = Image.FromFile(srcPath);
            if (src.Width <= maxW)
            {
                using var copy = new Bitmap(src);
                copy.Save(destPath, ImageFormat.Png);
                return;
            }
            int h = (int)((long)src.Height * maxW / src.Width);
            using var bmp = new Bitmap(maxW, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, maxW, h);
            }
            bmp.Save(destPath, ImageFormat.Png);
        }

        private static void CopyFolder(string srcDir, string destDir)
        {
            foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(srcDir.Length).TrimStart('\\', '/');
                string outp = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(outp));
                File.Copy(file, outp, true);
            }
        }

        private static string Esc(string s) => System.Security.SecurityElement.Escape(s ?? "");
    }
}
