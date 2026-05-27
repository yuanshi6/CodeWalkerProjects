using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.GameFiles;

namespace CodeWalker.OIVInstaller
{
    public partial class UninstallForm : Form
    {
        private List<BackupManager> _managers = new List<BackupManager>();
        private Dictionary<BackupLog, BackupManager> _logSource = new Dictionary<BackupLog, BackupManager>();
        private List<BackupLog> _allPackages = new List<BackupLog>();

        // Add-ons tab state — we cache the AddonManager for whichever game folder the
        // user came in with (FiveM doesn't have a dlcpacks concept, so the tab is
        // hidden when only a FiveM folder is available).
        private AddonManager _addonManager;
        private List<AddonManager.AddonInfo> _addonList = new List<AddonManager.AddonInfo>();
        // ItemCheck reentrancy guard — toggling .Checked from code re-fires the event.
        private bool _suppressAddonCheck;

        public UninstallForm(string gameFolder, string fiveMFolder = null)
        {
            InitializeComponent();

            if (!string.IsNullOrEmpty(gameFolder) && Directory.Exists(gameFolder))
            {
                _managers.Add(new BackupManager(gameFolder));
                _addonManager = new AddonManager(gameFolder);
            }

            if (!string.IsNullOrEmpty(fiveMFolder) && Directory.Exists(fiveMFolder))
            {
                // Avoid adding same folder twice if user selected FiveM folder as game folder
                if (string.IsNullOrEmpty(gameFolder) || !string.Equals(Path.GetFullPath(gameFolder), Path.GetFullPath(fiveMFolder), StringComparison.OrdinalIgnoreCase))
                {
                    _managers.Add(new BackupManager(fiveMFolder));
                }
            }

            this.Text = "Manage Mods";
            LoadPackages();

            // Add-ons require a SP game folder with a mods\ tree; if we don't have
            // one (FiveM-only), drop the tab entirely so the UI isn't misleading.
            if (_addonManager == null)
            {
                tabs.TabPages.Remove(tabAddons);
            }
            else
            {
                LoadAddons();
            }
        }

        // -- Installed Packages tab -----------------------------------------

        private void LoadPackages()
        {
            lstPackages.Items.Clear();
            _allPackages.Clear();
            _logSource.Clear();

            foreach (var manager in _managers)
            {
                var mgrPackages = manager.GetInstalledPackages();
                string folderName = new DirectoryInfo(manager.GameFolder).Name;
                bool isFiveM = folderName.Equals("mods", StringComparison.OrdinalIgnoreCase) && manager.GameFolder.Contains("FiveM", StringComparison.OrdinalIgnoreCase);

                foreach (var pkg in mgrPackages)
                {
                    _allPackages.Add(pkg);
                    _logSource[pkg] = manager;

                    string platformTag = pkg.IsGen9 ? "[Gen9]" : "[Legacy]";
                    string sourceTag = isFiveM ? "[FiveM]" : "[SP]";

                    lstPackages.Items.Add($"{sourceTag} {platformTag} {pkg.PackageName} ({pkg.InstallDate})");
                }
            }
        }

        private void lstPackages_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnUninstall.Enabled = lstPackages.SelectedIndex >= 0;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void btnUninstall_Click(object sender, EventArgs e)
        {
            if (lstPackages.SelectedIndex < 0) return;
            var log = _allPackages[lstPackages.SelectedIndex];
            var manager = _logSource[log];

            // Check for running game process
            while (ProcessHelper.IsGameRunning(out string processName))
            {
                var result = MessageBox.Show(
                    $"The game process '{processName}' is currently running.\n\n" +
                    "Please close the game before uninstalling mods to prevent file locking errors.",
                    "Game is Running",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel) return;
            }

            UninstallMode mode = UninstallMode.Backup;

            bool isFiveM = manager.GameFolder.Contains("FiveM", StringComparison.OrdinalIgnoreCase);

            if (isFiveM)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to uninstall '{log.PackageName}'?\nThis will remove the file from your FiveM mods folder.",
                    "Confirm Uninstall",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No) return;

                mode = UninstallMode.Backup;
            }
            else
            {
                // Custom Dialog for Uninstall Choice (SP only)
                using (var prompt = new Form())
                {
                    prompt.Width = 450;
                    prompt.Height = 220;
                    prompt.Text = "Uninstall Options";
                    prompt.StartPosition = FormStartPosition.CenterParent;
                    prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                    prompt.MaximizeBox = false;
                    prompt.MinimizeBox = false;

                    var iconBox = new PictureBox { Image = SystemIcons.Question.ToBitmap(), Size = new Size(32, 32), Location = new Point(20, 20) };
                    prompt.Controls.Add(iconBox);

                    var lbl = new Label
                    {
                        Text = $"How do you want to uninstall '{log.PackageName}'?",
                        Location = new Point(70, 25),
                        Size = new Size(350, 40)
                    };
                    prompt.Controls.Add(lbl);

                    var btnBackup = new Button
                    {
                        Text = "Revert to Previous State\n(Using Backups)",
                        Location = new Point(70, 70),
                        Size = new Size(160, 50),
                        DialogResult = DialogResult.Yes
                    };
                    btnBackup.Click += (s, ev) => { mode = UninstallMode.Backup; prompt.Close(); };
                    prompt.Controls.Add(btnBackup);

                    var btnVanilla = new Button
                    {
                        Text = "Reset to Vanilla\n(Ignore Backups)",
                        Location = new Point(240, 70),
                        Size = new Size(160, 50),
                        DialogResult = DialogResult.OK
                    };
                    btnVanilla.Click += (s, ev) => { mode = UninstallMode.Vanilla; prompt.Close(); };
                    prompt.Controls.Add(btnVanilla);

                    var btnCancel = new Button
                    {
                        Text = "Cancel",
                        Location = new Point(320, 140),
                        Size = new Size(80, 25),
                        DialogResult = DialogResult.Cancel
                    };
                    btnCancel.Click += (s, ev) => { prompt.Close(); };
                    prompt.Controls.Add(btnCancel);
                    prompt.CancelButton = btnCancel;

                    var result = prompt.ShowDialog(this);
                    if (result == DialogResult.Cancel) return;
                }
            }

            lblStatus.Text = "Uninstalling...";
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            btnUninstall.Enabled = false;
            lstPackages.Enabled = false;

            try
            {
                // Ensure keys are loaded (needed for RPF operations on SP).
                if (GTA5Keys.PC_AES_KEY == null)
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            bool isGen9 = File.Exists(Path.Combine(manager.GameFolder, "eboot.bin")) ||
                                         File.Exists(Path.Combine(manager.GameFolder, "GTA5_Enhanced.exe"));
                            GTA5Keys.LoadFromPath(manager.GameFolder, isGen9, null);
                        }
                        catch
                        {
                            // Ignore key loading failure for FiveM / standalone usage.
                        }
                    });
                }

                await Task.Run(() => PerformUninstall(manager, log, mode));

                MessageBox.Show("Uninstallation complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadPackages();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during uninstall: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                lblStatus.Text = "";
                progressBar.Visible = false;
                lstPackages.Enabled = true;
                btnUninstall.Enabled = lstPackages.SelectedIndex >= 0;
            }
        }

        private void PerformUninstall(BackupManager manager, BackupLog log, UninstallMode mode)
        {
            var progress = new Progress<string>(msg =>
            {
                if (msg.StartsWith("Reverting:")) return;
                UpdateStatus(msg);
            });
            manager.Uninstall(log, progress, mode);
        }

        private void UpdateStatus(string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(UpdateStatus), text);
                return;
            }
            lblStatus.Text = text;
        }

        // -- DLC Add-ons tab ------------------------------------------------

        private void tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Always show a fresh list when the user switches to the Add-ons tab —
            // dlcpacks folder contents may have changed externally between sessions.
            if (tabs.SelectedTab == tabAddons && _addonManager != null)
            {
                LoadAddons();
            }
        }

        private void btnRefreshAddons_Click(object sender, EventArgs e)
        {
            LoadAddons();
        }

        private void LoadAddons()
        {
            if (_addonManager == null) return;

            _suppressAddonCheck = true;
            try
            {
                lstAddons.BeginUpdate();
                lstAddons.Items.Clear();

                if (!_addonManager.ModsFolderExists)
                {
                    lblAddonsHint.Text = "mods\\update\\update.rpf not found — set up your mods folder before managing add-ons.";
                    _addonList = new List<AddonManager.AddonInfo>();
                    lstAddons.EndUpdate();
                    return;
                }

                _addonList = _addonManager.ListAddons();

                foreach (var info in _addonList)
                {
                    // IsStockDLC takes priority over the folder-existence check —
                    // most Rockstar vanilla packs have no separate dlcpacks\ folder
                    // because their content lives inside update.rpf itself.
                    string status =
                        info.IsStockDLC ? "Vanilla DLC" :
                        !info.FolderExists ? "Orphan" :
                        info.IsEnabled ? "Enabled" : "Disabled";
                    string folder =
                        info.FolderExists ? info.FolderPath :
                        info.IsStockDLC ? "(Rockstar vanilla — content inside update.rpf)" :
                        "(no folder — orphan dlclist entry)";
                    var item = new ListViewItem(new[] { info.Name, status, folder })
                    {
                        Checked = info.IsEnabled,
                        Tag = info,
                    };
                    if (info.IsStockDLC)
                    {
                        // Vanilla DLC visual: pale grey row + italic text so the user
                        // sees at a glance "don't touch — this is Rockstar's content".
                        // ItemCheck cancels any toggle attempt, but the styling alone
                        // should make it obvious nothing here is user-actionable.
                        item.BackColor = System.Drawing.Color.FromArgb(245, 246, 248);
                        item.ForeColor = System.Drawing.Color.FromArgb(120, 120, 130);
                        item.Font = new System.Drawing.Font(lstAddons.Font, System.Drawing.FontStyle.Italic);
                    }
                    else if (!info.FolderExists)
                    {
                        // Non-stock orphan: user installed something, deleted the folder
                        // by hand, but the dlclist entry lingered. Read-only too.
                        item.ForeColor = System.Drawing.Color.Gray;
                    }
                    lstAddons.Items.Add(item);
                }

                // Counts: keep them mutually exclusive so the line adds up.
                int userTotal    = _addonList.Count(a => !a.IsStockDLC && a.FolderExists);
                int userEnabled  = _addonList.Count(a => !a.IsStockDLC && a.FolderExists && a.IsEnabled);
                int stockCount   = _addonList.Count(a => a.IsStockDLC);
                int orphanCount  = _addonList.Count(a => !a.IsStockDLC && !a.FolderExists);
                lblAddonsHint.Text =
                    $"{userEnabled} / {userTotal} user add-ons enabled" +
                    (stockCount > 0 ? $"  ·  {stockCount} vanilla" : "") +
                    (orphanCount > 0 ? $"  ·  {orphanCount} orphan" : "");
                lstAddons.EndUpdate();
            }
            finally
            {
                _suppressAddonCheck = false;
            }
        }

        // ItemCheck fires BEFORE the .Checked property actually changes, which is
        // the only place we can cancel the toggle for read-only rows.
        private void lstAddons_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressAddonCheck) return;

            var item = lstAddons.Items[e.Index];
            if (!(item.Tag is AddonManager.AddonInfo info)) return;

            if (info.IsStockDLC || !info.FolderExists)
            {
                // Revert any attempt to toggle a read-only row.
                e.NewValue = e.CurrentValue;
            }
        }

        private async void lstAddons_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suppressAddonCheck) return;
            if (!(e.Item.Tag is AddonManager.AddonInfo info)) return;
            if (info.IsStockDLC || !info.FolderExists) return;

            bool enable = e.Item.Checked;
            // If we're already in the requested state, no-op (LoadAddons sets
            // Checked from disk state during rebuild; that path is already guarded
            // by _suppressAddonCheck, but treat this defensively).
            if (info.IsEnabled == enable) return;

            string action = enable ? "Enabling" : "Disabling";
            lblStatus.Text = $"{action} {info.Name}…";
            lstAddons.Enabled = false;
            btnRefreshAddons.Enabled = false;
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Marquee;

            try
            {
                await Task.Run(() => _addonManager.SetEnabled(info.Name, enable));
                info.IsEnabled = enable;
                e.Item.SubItems[1].Text = enable ? "Enabled" : "Disabled";
                lblStatus.Text = $"{(enable ? "Enabled" : "Disabled")} {info.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update dlclist.xml:\n\n{ex.Message}",
                    "Add-on toggle failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // Roll the checkbox back to reflect actual state.
                _suppressAddonCheck = true;
                try { e.Item.Checked = info.IsEnabled; }
                finally { _suppressAddonCheck = false; }
                lblStatus.Text = "";
            }
            finally
            {
                progressBar.Visible = false;
                lstAddons.Enabled = true;
                btnRefreshAddons.Enabled = true;
            }
        }
    }
}
