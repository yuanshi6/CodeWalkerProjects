using System;
using System.IO;
using System.Text.Json;

namespace CodeWalker.OivsPacker
{
    /// <summary>Small persisted settings for the packer (window, paths, options).</summary>
    public class PackerSettings
    {
        public int WindowWidth { get; set; } = 1040;
        public int WindowHeight { get; set; } = 700;
        public bool Maximized { get; set; }
        public string LastDir { get; set; } = "";        // last open/save/export folder
        public string InstallerPath { get; set; } = "";  // CodeWalker.OIVInstaller.exe for Preview
        public int MaxImageWidth { get; set; } = 1920;   // preview downscale width
        public bool ShowWelcome { get; set; } = true;    // first-run OIVS format intro

        private static string PathOnDisk =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "OivsPacker", "settings.json");

        public static PackerSettings Load()
        {
            try
            {
                if (File.Exists(PathOnDisk))
                    return JsonSerializer.Deserialize<PackerSettings>(File.ReadAllText(PathOnDisk)) ?? new PackerSettings();
            }
            catch { }
            return new PackerSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
                File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
