using System;
using System.IO;
using Microsoft.Win32;

namespace CodeWalker.OIVInstaller
{
    public static class FiveMHelper
    {
        private static string GetFiveMAppFolder()
        {
            try
            {
                // First attempt: Check Windows Registry Uninstall path
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CitizenFX_FiveM"))
                {
                    if (key != null)
                    {
                        object installLoc = key.GetValue("InstallLocation");
                        if (installLoc != null)
                        {
                            string regPath = installLoc.ToString();
                            string appPath = Path.Combine(regPath, "FiveM.app");
                            if (Directory.Exists(appPath))
                            {
                                return appPath;
                            }
                        }
                    }
                }
            }
            catch 
            {
                // Ignore registry errors and proceed to fallback
            }

            // Fallback: Default %LocalAppData% path
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string defaultPath = Path.Combine(localAppData, "FiveM", "FiveM.app");
            
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            return null;
        }

        public static string GetFiveMModsFolder()
        {
            string appFolder = GetFiveMAppFolder();
            if (!string.IsNullOrEmpty(appFolder))
            {
                return Path.Combine(appFolder, "mods");
            }
            return null;
        }

        public static bool IsFiveMInstalled()
        {
            return !string.IsNullOrEmpty(GetFiveMAppFolder());
        }
    }
}
