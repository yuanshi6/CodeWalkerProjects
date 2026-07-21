using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodeWalker.OivsPacker
{
    /// <summary>
    /// First-run welcome dialog that explains what the OIVS format is and how a
    /// package is put together. Shown on startup until the user opts out
    /// (PackerSettings.ShowWelcome); always reachable from the toolbar.
    /// </summary>
    public class WelcomeForm : Form
    {
        private readonly Color _accent = Color.FromArgb(21, 58, 117);
        private readonly Color _text = Color.FromArgb(35, 35, 35);
        private readonly Color _muted = Color.FromArgb(95, 95, 95);

        public bool ShowOnStartup { get; private set; } = true;

        public WelcomeForm(bool showOnStartup)
        {
            ShowOnStartup = showOnStartup;

            Text = "Welcome — the OIVS format";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(660, 620);

            // ---- accent header band ----
            var header = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = _accent };
            header.Controls.Add(new Label
            {
                Text = "Welcome to OIVS Packer",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 16),
            });
            header.Controls.Add(new Label
            {
                Text = "Build a whole mod collection into ONE file your users can install in a few clicks.",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(210, 222, 240),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(22, 52),
            });

            // ---- bottom bar ----
            // Size the panel to its final docked width BEFORE adding children, so the
            // right-anchored button doesn't get displaced when docking resizes the bar.
            var bottom = new Panel { Dock = DockStyle.Bottom, Size = new Size(ClientSize.Width, 52), BackColor = Color.FromArgb(245, 245, 245) };
            bottom.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(220, 220, 220)), 0, 0, bottom.Width, 0);

            var chkStartup = new CheckBox
            {
                Text = "Show this welcome on startup",
                Checked = ShowOnStartup,
                AutoSize = true,
                ForeColor = _muted,
                Location = new Point(20, 16),
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
            };
            chkStartup.CheckedChanged += (s, e) => ShowOnStartup = chkStartup.Checked;
            bottom.Controls.Add(chkStartup);

            var btnStart = new Button
            {
                Text = "Get started",
                Size = new Size(120, 30),
                Location = new Point(ClientSize.Width - 140, 11),
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                FlatStyle = FlatStyle.Flat,
                BackColor = _accent,
                ForeColor = Color.White,
                DialogResult = DialogResult.OK,
            };
            btnStart.FlatAppearance.BorderSize = 0;
            bottom.Controls.Add(btnStart);
            AcceptButton = btnStart;

            // ---- scrollable body ----
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(0) };

            int y = 14;
            int wrapWidth = ClientSize.Width - 40 - SystemInformation.VerticalScrollBarWidth;

            void Heading(string text)
            {
                var l = new Label
                {
                    Text = text,
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    ForeColor = _accent,
                    AutoSize = true,
                    MaximumSize = new Size(wrapWidth, 0),
                    Location = new Point(20, y),
                };
                body.Controls.Add(l);
                y = l.Location.Y + l.PreferredHeight + 4;
            }
            void Para(string text)
            {
                var l = new Label
                {
                    Text = text,
                    Font = new Font("Segoe UI", 9.5f),
                    ForeColor = _text,
                    AutoSize = true,
                    MaximumSize = new Size(wrapWidth, 0),
                    Location = new Point(20, y),
                };
                body.Controls.Add(l);
                y = l.Location.Y + l.PreferredHeight + 12;
            }

            Heading("What is a .oivs package?");
            Para("OIVS (“Super OIV”) is a package format for GTA V mods, built as the successor to " +
                 "OpenIV's .oiv format. Where an .oiv installs exactly one thing, a single .oivs bundles an " +
                 "entire collection: a base mod, optional add-on modules the user can tick on or off, and " +
                 "single-choice groups (for example: pick ONE streetlight color out of three). " +
                 "Your users install it with the Package Installer — OpenIV is not needed.");

            Heading("What your users see");
            Para("When they open the .oivs, a selection wizard appears: modules show as checkboxes, groups as " +
                 "radio choices, each with its own description and image previews — including before/after " +
                 "comparisons they can click to toggle. One click installs the chosen combination, a backup is " +
                 "created automatically, and the whole package appears as one entry in Manage Mods, so it can " +
                 "be uninstalled cleanly later.");

            Heading("How it works inside");
            Para("Every module or option carries standard OIV install operations — the same assembly.xml " +
                 "grammar you already know — and/or loose folders that get copied into the game folder. " +
                 "On export, the packer extracts each attached .oiv, moves its files into a namespaced " +
                 "content folder (so two modules shipping the same file names can't collide), downscales and " +
                 "bundles your preview images so the package works offline, and writes one super.xml manifest. " +
                 "The result is a single zip — your .oivs.");

            Heading("Your workflow");
            Para("1.  Build or collect the mods: normal .oiv files, or plain folders of loose files.\n" +
                 "2.  Add them here as modules and group options; write descriptions and attach previews.\n" +
                 "3.  Save the project as .oivsproj so you can come back and edit any time.\n" +
                 "4.  Click Preview to open the package in the real installer wizard and check the result.\n" +
                 "5.  Click Export .oivs and share that one file.");

            Heading("Good to know");
            Para("•  A package does not need a required base — leave every module optional for a " +
                 "pick-any compilation.\n" +
                 "•  Drag && drop works everywhere: an .oiv or a folder becomes a module, images become " +
                 "previews for the selected item, an .oivsproj opens it.\n" +
                 "•  The Help button in the toolbar has a short step-by-step reference.");

            var spacer = new Label { Text = "", Location = new Point(20, y), Size = new Size(1, 8) };
            body.Controls.Add(spacer);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(header);

            // Layout above is authored in 96-DPI design pixels — font autoscaling
            // keeps it intact on 125/150% displays (same fix as the other forms).
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
        }
    }
}
