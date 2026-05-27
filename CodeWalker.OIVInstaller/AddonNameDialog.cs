using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodeWalker.OIVInstaller
{
    /// <summary>
    /// Tiny modal prompt that asks the user for a folder name to install a bare
    /// <c>dlc.rpf</c> under. Live-validates against AddonManager.IsValidAddonName
    /// so the OK button stays disabled until the input is sane.
    /// </summary>
    public class AddonNameDialog : Form
    {
        private TextBox _txtName;
        private Label _lblError;
        private Button _btnOk;
        private Button _btnCancel;

        public string AddonName { get; private set; } = "";

        public AddonNameDialog(string suggestedName)
        {
            InitializeLayout();
            _txtName.Text = suggestedName ?? "";
            _txtName.SelectAll();
            ValidateInput();
        }

        private void InitializeLayout()
        {
            this.Text = "Name this add-on";
            this.ClientSize = new Size(380, 150);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var lbl = new Label
            {
                Text = "Folder name under mods\\update\\x64\\dlcpacks\\:",
                Location = new Point(16, 14),
                AutoSize = true,
                ForeColor = Color.FromArgb(60, 60, 60),
            };

            _txtName = new TextBox
            {
                Location = new Point(16, 38),
                Size = new Size(348, 23),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.75F),
            };
            _txtName.TextChanged += (s, e) => ValidateInput();
            _txtName.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && _btnOk.Enabled)
                {
                    AddonName = _txtName.Text.Trim();
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            };

            _lblError = new Label
            {
                Location = new Point(16, 68),
                Size = new Size(348, 30),
                ForeColor = Color.FromArgb(180, 30, 30),
                Font = new Font("Segoe UI", 8.25F),
                Text = "",
            };

            _btnOk = new Button
            {
                Text = "Install",
                Location = new Point(186, 110),
                Size = new Size(88, 28),
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat,
            };
            _btnOk.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            _btnOk.Click += (s, e) => { AddonName = _txtName.Text.Trim(); };

            _btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(280, 110),
                Size = new Size(84, 28),
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);

            this.Controls.AddRange(new Control[] { lbl, _txtName, _lblError, _btnOk, _btnCancel });
            this.AcceptButton = _btnOk;
            this.CancelButton = _btnCancel;
        }

        private void ValidateInput()
        {
            string name = _txtName.Text.Trim();
            bool ok = AddonManager.IsValidAddonName(name, out string err);
            _lblError.Text = err ?? "";
            _btnOk.Enabled = ok;
        }
    }
}
