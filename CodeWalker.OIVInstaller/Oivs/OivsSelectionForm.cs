using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Windows.Forms;

namespace CodeWalker.OIVInstaller
{
    /// <summary>
    /// Vortex-style selection wizard for a Super OIV (.oivs) package: lists the
    /// required base mod, optional modules (checkboxes) and single-choice groups
    /// (radios) on the left, with a description + before/after comparison viewer
    /// on the right. Returns a <see cref="OivsSelection"/> via <see cref="Selection"/>.
    /// </summary>
    public class OivsSelectionForm : Form
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly Dictionary<string, Image> _imgCache = new Dictionary<string, Image>();

        private readonly OivsPackage _pkg;
        private readonly Color _accent;
        // Light / OpenIV-style palette, matching MainForm (white body, accent header,
        // white buttons with a 180-grey border, dark-grey body text).
        private readonly Color _bg = Color.White;
        private readonly Color _panel = Color.FromArgb(245, 245, 245);
        private readonly Color _border = Color.FromArgb(210, 210, 210);
        private readonly Color _text = Color.FromArgb(40, 40, 40);
        private readonly Color _muted = Color.FromArgb(110, 110, 110);

        public OivsSelection Selection { get; private set; }

        // left-side controls -> ids
        private readonly Dictionary<CheckBox, string> _moduleChecks = new Dictionary<CheckBox, string>();
        private readonly Dictionary<string, List<(RadioButton rb, string optId)>> _groupRadios =
            new Dictionary<string, List<(RadioButton, string)>>();

        // right-side preview controls
        private Label _prevTitle, _prevDesc, _compareName, _compareHint, _imgStatus;
        private PictureBox _pic;
        private ComboBox _cmbCompare;
        private Panel _comparePanel;
        private Panel _navBar;   // top strip that hosts the view dropdown

        // preview state
        private List<OivsMedia> _compares = new List<OivsMedia>();
        private int _compareIdx;
        private bool _showAfter = true;
        // gallery state (multiple single <image> entries, e.g. base-mod showcase)
        private List<OivsMedia> _images = new List<OivsMedia>();
        private int _imageIdx;
        private bool _galleryMode;
        private int _previewToken;   // guards against out-of-order async image loads
        private readonly bool _preview;   // preview-only (no install) — used by the packer

        public OivsSelectionForm(OivsPackage pkg, bool previewMode = false)
        {
            _pkg = pkg;
            _preview = previewMode;
            _accent = ParseAccent(pkg.Metadata.HeaderBackground, Color.FromArgb(21, 58, 117));

            Text = (previewMode ? "Preview — " : "Install ") + pkg.Metadata.Name;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(820, 560);
            Size = new Size(960, 700);
            BackColor = _bg;
            ForeColor = _text;
            Font = new Font("Segoe UI", 9f);

            // Add docked controls in z-order so the Fill panel is at the BACK and the
            // edges (Top/Bottom/Left) reserve their space first. Fill must be added
            // first; otherwise it covers the header band and bottom button bar.
            BuildPreview();     // Fill (back-most)
            BuildList();        // Left
            BuildBottomBar();   // Bottom
            BuildHeader();      // Top (front-most edge)

            // Layout above is authored in 96-DPI design pixels — opt into the same
            // font-based autoscaling the Designer forms use so displays at 125/150%
            // scaling get proportionally sized controls instead of clipped text.
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;

            // initial preview = base module (or first item)
            if (_pkg.Modules.Count > 0) ShowPreview(_pkg.Modules[0]);
        }

        // ---- layout ----------------------------------------------------------

        private void BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = _accent };
            bool blackText = _pkg.Metadata.UseBlackTextColor;
            var fg = blackText ? Color.Black : Color.White;

            var title = new Label
            {
                Text = _pkg.Metadata.Name,
                ForeColor = fg,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 10),
                BackColor = Color.Transparent,
            };
            bool hasRequired = _pkg.Modules.Exists(m => m.Required);
            var sub = new Label
            {
                Text = hasRequired
                    ? "Choose the components to install. The base mod is required; everything else is optional."
                    : "Choose which mods to install — pick any combination.",
                ForeColor = Color.FromArgb(blackText ? 60 : 220, fg.R, fg.G, fg.B),
                AutoSize = true,
                Location = new Point(20, 40),
                BackColor = Color.Transparent,
            };
            header.Controls.Add(sub);
            header.Controls.Add(title);
            Controls.Add(header);
        }

        private void BuildBottomBar()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = _panel };
            bar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(_border), 0, 0, bar.Width, 0);  // top divider

            var install = new Button
            {
                Text = _preview ? "Close Preview" : "Install",
                Width = _preview ? 150 : 130,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = _accent,
                ForeColor = _pkg.Metadata.UseBlackTextColor ? Color.Black : Color.White,
                Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
            };
            install.FlatAppearance.BorderSize = 0;
            if (_preview)
                install.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            else
                install.Click += (s, e) => { CommitSelection(); DialogResult = DialogResult.OK; Close(); };

            // In preview mode there's nothing to cancel — a "Preview only" note sits
            // where Cancel would be.
            var cancel = new Button
            {
                Text = "Cancel",
                Width = 100,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Visible = !_preview,
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            var note = new Label
            {
                Text = "Preview only — nothing will be installed.",
                AutoSize = true,
                ForeColor = _muted,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Visible = _preview,
            };

            bar.Resize += (s, e) =>
            {
                install.Location = new Point(bar.Width - install.Width - 16, 11);
                cancel.Location = new Point(bar.Width - install.Width - cancel.Width - 28, 11);
                note.Location = new Point(16, 19);
            };
            bar.Controls.Add(install);
            bar.Controls.Add(cancel);
            bar.Controls.Add(note);
            Controls.Add(bar);

            AcceptButton = install;
            CancelButton = cancel;
        }

        private void BuildList()
        {
            var host = new Panel { Dock = DockStyle.Left, Width = 360, BackColor = _bg, Padding = new Padding(0) };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(14, 12, 14, 12),
                BackColor = _bg,
            };

            // BASE — only when the package actually has required module(s).
            bool anyRequired = _pkg.Modules.Exists(m => m.Required);
            bool anyOptional = _pkg.Modules.Exists(m => !m.Required);

            if (anyRequired)
            {
                flow.Controls.Add(SectionLabel("BASE"));
                foreach (var m in _pkg.Modules)
                {
                    if (!m.Required) continue;
                    flow.Controls.Add(MakeModuleRow(m, flow));
                }
            }

            // OPTIONAL MODULES — when there is no required base this is the main
            // list, so the package can be a pick-any compilation of mods.
            if (anyOptional)
            {
                flow.Controls.Add(SectionLabel(anyRequired ? "OPTIONAL ADD-ONS" : "MODULES  —  CHOOSE WHAT TO INSTALL"));
                foreach (var m in _pkg.Modules)
                {
                    if (m.Required) continue;
                    flow.Controls.Add(MakeModuleRow(m, flow));
                }
            }

            // GROUPS (single-choice)
            foreach (var g in _pkg.Groups)
            {
                flow.Controls.Add(SectionLabel((g.Title ?? g.Id).ToUpperInvariant() + "  —  CHOOSE ONE"));
                var radios = new List<(RadioButton, string)>();
                var groupPanel = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 6),
                    BackColor = _bg,
                };

                if (g.AllowNone)
                {
                    var rbNone = MakeRadio("None", g.Default == "none" || string.IsNullOrEmpty(g.Default));
                    rbNone.Click += (s, e) => ShowGroupNonePreview(g);
                    rbNone.Enter += (s, e) => ShowGroupNonePreview(g);
                    groupPanel.Controls.Add(rbNone);
                    radios.Add((rbNone, "none"));
                }
                foreach (var o in g.Options)
                {
                    bool isDefault = string.Equals(g.Default, o.Id, StringComparison.OrdinalIgnoreCase);
                    var rb = MakeRadio(string.IsNullOrEmpty(o.Name) ? o.Id : o.Name, isDefault);
                    var opt = o;
                    rb.Click += (s, e) => ShowPreview(opt);
                    rb.Enter += (s, e) => ShowPreview(opt);
                    groupPanel.Controls.Add(rb);
                    radios.Add((rb, o.Id));
                }
                // if nothing defaulted and no None, select first option
                if (!g.AllowNone && !radios.Exists(r => r.Item1.Checked) && radios.Count > 0)
                    radios[0].Item1.Checked = true;

                _groupRadios[g.Id] = radios;
                flow.Controls.Add(groupPanel);
            }

            host.Controls.Add(flow);

            // separator line on the right edge
            var sep = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = _border };
            host.Controls.Add(sep);

            Controls.Add(host);
        }

        private void BuildPreview()
        {
            var right = new Panel { Dock = DockStyle.Fill, BackColor = _bg, Padding = new Padding(20, 16, 20, 16) };

            _prevTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "",
                ForeColor = _text,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                AutoEllipsis = true,
            };
            _prevDesc = new Label
            {
                Dock = DockStyle.Top,
                Height = 70,
                Text = "",
                ForeColor = _muted,
                Font = new Font("Segoe UI", 9.5f),
            };

            // comparison area
            _comparePanel = new Panel { Dock = DockStyle.Fill, BackColor = _panel, Padding = new Padding(1) };
            _comparePanel.Paint += (s, e) => e.Graphics.DrawRectangle(
                new Pen(_border), 0, 0, _comparePanel.Width - 1, _comparePanel.Height - 1);

            _pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = _panel,
                Cursor = Cursors.Hand,
            };
            _pic.Click += (s, e) => OnPictureClicked();

            _imgStatus = new Label
            {
                Text = "",
                ForeColor = _muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                // solid (matches panel) — a Transparent label won't paint over the PictureBox
                BackColor = _panel,
            };

            var capBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(235, 235, 235) };
            _compareName = new Label { Text = "", ForeColor = _text, AutoSize = true, Location = new Point(8, 7), BackColor = Color.Transparent };
            _compareHint = new Label
            {
                Text = "Click image to compare Before / After",
                ForeColor = _muted,
                AutoSize = true,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                BackColor = Color.Transparent,
            };
            capBar.Resize += (s, e) => _compareHint.Location = new Point(capBar.Width - _compareHint.Width - 10, 7);
            capBar.Controls.Add(_compareName);
            capBar.Controls.Add(_compareHint);

            // top strip hosting a compact, owner-drawn view selector (so it doesn't
            // render as a full-width blue bar like a default focused DropDownList)
            _navBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = _panel, Visible = false };
            _navBar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(_border), 0, _navBar.Height - 1, _navBar.Width, _navBar.Height - 1);

            _cmbCompare = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                DrawMode = DrawMode.OwnerDrawFixed,
                BackColor = Color.White,
                ForeColor = _text,
                Width = 260,
                Location = new Point(6, 7),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                TabStop = false,
            };
            // Owner-draw: white box + dark text, light-blue highlight only for the
            // hovered item in the open list. Kills the default blue closed-box bar.
            _cmbCompare.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool editBox = (e.State & DrawItemState.ComboBoxEdit) != 0;
                bool hot = (e.State & DrawItemState.Selected) != 0 && !editBox;
                using (var b = new SolidBrush(hot ? Color.FromArgb(210, 228, 250) : Color.White))
                    e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, _cmbCompare.Items[e.Index].ToString(),
                    _cmbCompare.Font, e.Bounds, _text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            _cmbCompare.SelectedIndexChanged += (s, e) =>
            {
                int idx = _cmbCompare.SelectedIndex;
                if (idx < 0) return;
                if (_galleryMode)
                {
                    if (idx < _images.Count) { _imageIdx = idx; UpdateGalleryPicture(); }
                }
                else if (idx < _compares.Count)
                {
                    _compareIdx = idx;
                    _showAfter = true;
                    UpdateComparePicture();
                }
            };
            _navBar.Controls.Add(_cmbCompare);

            _comparePanel.Controls.Add(_pic);
            _comparePanel.Controls.Add(_imgStatus);
            _comparePanel.Controls.Add(capBar);
            _comparePanel.Controls.Add(_navBar);
            _imgStatus.BringToFront();

            right.Controls.Add(_comparePanel);
            right.Controls.Add(_prevDesc);
            right.Controls.Add(_prevTitle);
            Controls.Add(right);
        }

        // ---- preview ---------------------------------------------------------

        private void ShowPreview(OivsInstallable item)
        {
            _previewToken++;
            _prevTitle.Text = string.IsNullOrEmpty(item.Name) ? item.Id : item.Name;
            _prevDesc.Text = item.Description ?? "";

            _compares = new List<OivsMedia>();
            _images = new List<OivsMedia>();
            foreach (var m in item.Media)
            {
                if (m.IsCompare) _compares.Add(m);
                else if (!string.IsNullOrEmpty(m.Image)) _images.Add(m);
            }

            // compare media present -> before/after viewer
            if (_compares.Count > 0)
            {
                _galleryMode = false;
                _comparePanel.Visible = true;
                _pic.Visible = true;          // re-show after a no-media option
                _imgStatus.Visible = false;
                _compareIdx = 0;
                _showAfter = true;

                _navBar.Visible = _compares.Count > 1;
                if (_compares.Count > 1)
                {
                    _cmbCompare.Items.Clear();
                    for (int i = 0; i < _compares.Count; i++)
                        _cmbCompare.Items.Add(string.IsNullOrEmpty(_compares[i].Title) ? $"View {i + 1}" : _compares[i].Title);
                    _cmbCompare.SelectedIndex = 0;
                }
                UpdateComparePicture();
                return;
            }

            // one or more single images -> gallery (browse with dropdown + click)
            if (_images.Count > 0)
            {
                _galleryMode = true;
                _comparePanel.Visible = true;
                _pic.Visible = true;          // re-show after a no-media option
                _imgStatus.Visible = false;
                _imageIdx = 0;

                _navBar.Visible = _images.Count > 1;
                if (_images.Count > 1)
                {
                    _cmbCompare.Items.Clear();
                    for (int i = 0; i < _images.Count; i++)
                        _cmbCompare.Items.Add(string.IsNullOrEmpty(_images[i].Caption) ? $"Screenshot {i + 1}" : _images[i].Caption);
                    _cmbCompare.SelectedIndex = 0;
                }
                UpdateGalleryPicture();
                return;
            }

            // no media
            _galleryMode = false;
            _navBar.Visible = false;
            _compareHint.Visible = false;
            _compareName.Text = "";
            _pic.Image = null;
            _pic.Visible = false;
            _imgStatus.Text = "No preview image for this option.";
            _imgStatus.Visible = true;
            _imgStatus.BringToFront();
        }

        private void ShowGroupNonePreview(OivsGroup g)
        {
            _previewToken++;
            _prevTitle.Text = (g.Title ?? g.Id) + ": None";
            _prevDesc.Text = string.IsNullOrEmpty(g.Description)
                ? "This optional set will not be installed."
                : g.Description + "\r\n\r\n(None selected — nothing from this set will be installed.)";
            _compares.Clear();
            _images.Clear();
            _galleryMode = false;
            _navBar.Visible = false;
            _compareHint.Visible = false;
            _compareName.Text = "";
            _pic.Image = null;
            _pic.Visible = false;
            _imgStatus.Text = "Nothing from this set will be installed.";
            _imgStatus.Visible = true;
            _imgStatus.BringToFront();
        }

        // Picture clicked: in compare mode toggle before/after; in gallery mode
        // advance to the next screenshot.
        private void OnPictureClicked()
        {
            if (_galleryMode)
            {
                if (_images.Count == 0) return;
                _imageIdx = (_imageIdx + 1) % _images.Count;
                if (_navBar.Visible) _cmbCompare.SelectedIndex = _imageIdx; // syncs + repaints
                else UpdateGalleryPicture();
            }
            else
            {
                if (_compares.Count == 0) return;
                _showAfter = !_showAfter;
                UpdateComparePicture();
            }
        }

        private void UpdateComparePicture()
        {
            if (_compareIdx < 0 || _compareIdx >= _compares.Count) return;
            var m = _compares[_compareIdx];
            _compareHint.Text = "Click image to compare Before / After";
            _compareHint.Visible = true;
            string url = _showAfter ? m.After : m.Before;
            string side = _showAfter ? "After" : "Before";
            _compareName.Text = string.IsNullOrEmpty(m.Title) ? side : $"{side} — {m.Title}";
            _previewToken++;
            LoadImage(url, _previewToken);
        }

        private void UpdateGalleryPicture()
        {
            if (_imageIdx < 0 || _imageIdx >= _images.Count) return;
            var m = _images[_imageIdx];
            _compareName.Text = !string.IsNullOrEmpty(m.Caption)
                ? m.Caption
                : (_images.Count > 1 ? $"{_imageIdx + 1} / {_images.Count}" : "");
            _compareHint.Text = "Click image for next";
            _compareHint.Visible = _images.Count > 1;
            _previewToken++;
            LoadImage(m.Image, _previewToken);
        }

        private void ShowLoadedImage(Image img)
        {
            _imgStatus.Visible = false;
            _pic.Image = img;
            _pic.Visible = true;
            _pic.BringToFront();
        }

        // Accepts either an http(s) URL or a path relative to the .oivs root
        // (a bundled image file), so previews work offline when shipped in-package.
        private async void LoadImage(string src, int token)
        {
            if (string.IsNullOrEmpty(src))
            {
                _pic.Image = null;
                _imgStatus.Text = "No image.";
                _imgStatus.Visible = true;
                return;
            }
            if (_imgCache.TryGetValue(src, out var cached))
            {
                if (token == _previewToken) ShowLoadedImage(cached);
                return;
            }
            _pic.Visible = false;
            _imgStatus.Text = "Loading preview…";
            _imgStatus.Visible = true;
            _imgStatus.BringToFront();
            try
            {
                byte[] data;
                if (IsUrl(src))
                {
                    data = await _http.GetByteArrayAsync(src);
                }
                else
                {
                    string path = ResolveLocal(src);
                    if (path == null || !File.Exists(path))
                        throw new FileNotFoundException("bundled image not found", src);
                    data = await Task.Run(() => File.ReadAllBytes(path));
                }

                Image img;
                using (var ms = new MemoryStream(data))
                    img = new Bitmap(Image.FromStream(ms));
                _imgCache[src] = img;
                if (!IsDisposed && token == _previewToken)
                    ShowLoadedImage(img);
            }
            catch
            {
                if (!IsDisposed && token == _previewToken)
                {
                    _pic.Image = null;
                    _pic.Visible = false;
                    _imgStatus.Text = IsUrl(src)
                        ? "Preview unavailable (offline?)."
                        : "Preview image not found in package.";
                    _imgStatus.Visible = true;
                    _imgStatus.BringToFront();
                }
            }
        }

        private static bool IsUrl(string s) =>
            s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // Resolves a manifest-relative media path against the extracted .oivs root.
        private string ResolveLocal(string rel)
        {
            if (string.IsNullOrEmpty(_pkg.RootPath)) return null;
            rel = rel.Replace('/', Path.DirectorySeparatorChar)
                     .Replace('\\', Path.DirectorySeparatorChar)
                     .TrimStart(Path.DirectorySeparatorChar);
            return Path.Combine(_pkg.RootPath, rel);
        }

        // ---- selection -------------------------------------------------------

        private void CommitSelection()
        {
            var sel = _pkg.DefaultSelection();
            sel.EnabledModules.Clear();
            foreach (var kv in _moduleChecks)
            {
                if (kv.Key.Checked)
                {
                    var mod = _pkg.FindModule(kv.Value);
                    if (mod != null && !mod.Required) sel.EnabledModules.Add(kv.Value);
                }
            }
            foreach (var g in _pkg.Groups)
            {
                string choice = "none";
                if (_groupRadios.TryGetValue(g.Id, out var radios))
                    foreach (var r in radios)
                        if (r.rb.Checked) { choice = r.optId; break; }
                sel.GroupChoices[g.Id] = choice;
            }
            Selection = sel;
        }

        // ---- small UI helpers ------------------------------------------------

        private Control MakeModuleRow(OivsModule m, Control owner)
        {
            var cb = new CheckBox
            {
                Text = m.Required ? m.Name + "   (required)" : m.Name,
                ForeColor = _text,
                AutoSize = true,
                Margin = new Padding(2, 4, 2, 4),
                Checked = m.Required || m.Default,
                // Required modules stay enabled so the user can click the row to view
                // its preview, but can't be unchecked (forced back on below).
                Enabled = true,
                FlatStyle = FlatStyle.Flat,
            };
            var mod = m;
            if (m.Required)
                cb.CheckedChanged += (s, e) => { if (!cb.Checked) cb.Checked = true; };
            cb.Click += (s, e) => ShowPreview(mod);
            cb.Enter += (s, e) => ShowPreview(mod);
            cb.MouseDown += (s, e) => ShowPreview(mod);
            if (!m.Required) _moduleChecks[cb] = m.Id;
            return cb;
        }

        private RadioButton MakeRadio(string text, bool check)
        {
            return new RadioButton
            {
                Text = text,
                ForeColor = _text,
                AutoSize = true,
                Checked = check,
                Margin = new Padding(2, 3, 2, 3),
                FlatStyle = FlatStyle.Flat,
            };
        }

        private Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = _accent,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 4),
            };
        }

        private static Color ParseAccent(string s, Color fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(s))
                {
                    s = s.TrimStart('$');
                    if (s.Length == 8)
                    {
                        int argb = Convert.ToInt32(s, 16);
                        var c = Color.FromArgb(argb);
                        return Color.FromArgb(255, c.R, c.G, c.B);
                    }
                }
            }
            catch { }
            return fallback;
        }
    }
}
