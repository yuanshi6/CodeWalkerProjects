using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeWalker.OivsPacker
{
    public class PackerForm : Form
    {
        private readonly Color _accent = Color.FromArgb(21, 58, 117);
        private readonly Color _bg = Color.White;
        private readonly Color _muted = Color.FromArgb(110, 110, 110);

        private OivsProject _project = OivsProject.NewDefault();
        private string _projectPath;
        private bool _dirty;

        private TreeView _tree;
        private Panel _editor;
        private ToolStrip _toolbar;
        private Label _status;
        private readonly PackerSettings _settings = PackerSettings.Load();

        public PackerForm()
        {
            Text = "OIVS Packer";
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _bg;
            Font = new Font("Segoe UI", 9f);
            MinimumSize = new Size(860, 560);
            Size = new Size(Math.Max(860, _settings.WindowWidth), Math.Max(560, _settings.WindowHeight));
            if (_settings.Maximized) WindowState = FormWindowState.Maximized;

            BuildToolbar();
            BuildStatusBar();   // dock bottom before fill/left
            BuildBody();
            RebuildTree();
            SelectPackageRoot();

            // Drag-and-drop: .oiv -> module, folder -> module, image(s) -> media,
            // .oivsproj -> open.
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        // ---- top toolbar -----------------------------------------------------

        private void BuildToolbar()
        {
            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Color.FromArgb(245, 245, 245),
                Renderer = new ToolStripProfessionalRenderer(),
                ImageScalingSize = new Size(16, 16),
            };
            ToolStripButton Btn(string text, EventHandler h, bool accent = false)
            {
                var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(6, 2, 6, 2) };
                if (accent) { b.ForeColor = Color.White; b.BackColor = _accent; }
                b.Click += h;
                return b;
            }
            _toolbar.Items.Add(Btn("New", (s, e) => NewProject()));
            _toolbar.Items.Add(Btn("Open…", (s, e) => OpenProject()));
            _toolbar.Items.Add(Btn("Save", (s, e) => SaveProject(false)));
            _toolbar.Items.Add(Btn("Save As…", (s, e) => SaveProject(true)));
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(Btn("Preview", (s, e) => PreviewInInstaller()));
            _toolbar.Items.Add(Btn("Export .oivs", (s, e) => ExportOivs(), accent: true));
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(Btn("Help", (s, e) => ShowHelp()));
            Controls.Add(_toolbar);
        }

        private void BuildStatusBar()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Color.FromArgb(245, 245, 245) };
            bar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(220, 220, 220)), 0, 0, bar.Width, 0);
            _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0), ForeColor = _muted };
            _status.Click += (s, e) => { var errs = _project.Validate(); if (errs.Count > 0) MessageBox.Show("• " + string.Join("\n• ", errs), "Problems", MessageBoxButtons.OK, MessageBoxIcon.Warning); };
            bar.Controls.Add(_status);
            Controls.Add(bar);
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            int mods = _project.Modules.Count;
            int grps = _project.Groups.Count;
            int opts = _project.Groups.Sum(g => g.Options.Count);
            string summary = $"{mods} module{(mods == 1 ? "" : "s")}, {grps} group{(grps == 1 ? "" : "s")}" + (grps > 0 ? $" ({opts} options)" : "");
            var errs = _project.Validate();
            if (errs.Count == 0)
            {
                _status.ForeColor = Color.FromArgb(0, 120, 60);
                _status.Text = $"✓  Ready to export  —  {summary}";
            }
            else
            {
                _status.ForeColor = Color.FromArgb(170, 60, 30);
                _status.Text = $"⚠  {errs.Count} problem{(errs.Count == 1 ? "" : "s")} (click for details)  —  {summary}";
            }
        }

        // ---- drag & drop -----------------------------------------------------

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

            // .oivsproj -> open it
            var proj = paths.FirstOrDefault(p => p.EndsWith(".oivsproj", StringComparison.OrdinalIgnoreCase));
            if (proj != null) { if (ConfirmDiscard()) LoadProjectFile(proj); return; }

            var images = paths.Where(IsImage).ToList();
            var oivs = paths.Where(p => p.EndsWith(".oiv", StringComparison.OrdinalIgnoreCase)).ToList();
            var folders = paths.Where(Directory.Exists).ToList();

            // images -> add as media on the currently selected module/option
            if (images.Count > 0)
            {
                var it = _tree.SelectedNode?.Tag as PackInstallable;
                if (it == null)
                {
                    MessageBox.Show("Select a module or option first, then drop image(s) to add them as previews.",
                        "Add previews", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    foreach (var img in images) it.Media.Add(PackMedia.Image(img));
                    MarkDirty(); RefreshEditor(it);
                }
            }

            // each .oiv -> a new module pointing at it
            foreach (var o in oivs)
            {
                var m = new PackModule { Id = UniqueId("module"), Name = Path.GetFileNameWithoutExtension(o), OivPath = o };
                _project.Modules.Add(m);
            }
            // each folder -> a new module that copies it into the game root
            foreach (var d in folders)
            {
                var m = new PackModule { Id = UniqueId("module"), Name = new DirectoryInfo(d).Name };
                m.Folders.Add(new PackFolder { Path = d });
                _project.Modules.Add(m);
            }
            if (oivs.Count > 0 || folders.Count > 0)
            {
                MarkDirty(); RebuildTree();
                var last = _project.Modules.LastOrDefault();
                if (last != null) _tree.SelectedNode = FindNode(_tree.Nodes, last);
            }
        }

        private static bool IsImage(string p)
        {
            string e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp" || e == ".gif";
        }

        // ---- body: tree (left) + editor (right) ------------------------------

        private void BuildBody()
        {
            _editor = new Panel { Dock = DockStyle.Fill, BackColor = _bg, AutoScroll = true, Padding = new Padding(18, 14, 18, 14) };

            var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = _bg };
            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = false,
                ShowRootLines = false,
                Indent = 18,
                ItemHeight = 24,
                BackColor = Color.FromArgb(248, 248, 248),
            };
            _tree.AfterSelect += (s, e) => ShowEditor(e.Node?.Tag);

            var treeButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, BackColor = Color.FromArgb(245, 245, 245), Padding = new Padding(4) };
            Button TB(string t, EventHandler h) { var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(2) }; b.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200); b.Click += h; return b; }
            treeButtons.Controls.Add(TB("+ Module", (s, e) => AddModule()));
            treeButtons.Controls.Add(TB("+ Group", (s, e) => AddGroup()));
            treeButtons.Controls.Add(TB("+ Option", (s, e) => AddOption()));
            treeButtons.Controls.Add(TB("Remove", (s, e) => RemoveSelected()));
            treeButtons.Controls.Add(TB("↑", (s, e) => MoveSelected(-1)));
            treeButtons.Controls.Add(TB("↓", (s, e) => MoveSelected(1)));

            left.Controls.Add(_tree);
            left.Controls.Add(treeButtons);

            var split = new Splitter { Dock = DockStyle.Left, Width = 4, BackColor = Color.FromArgb(225, 225, 225) };

            Controls.Add(_editor);
            Controls.Add(split);
            Controls.Add(left);
        }

        // ---- tree management -------------------------------------------------

        private const string PackageTag = "__package__";

        private void RebuildTree()
        {
            object keep = _tree.SelectedNode?.Tag;
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            var root = new TreeNode("📦  " + (string.IsNullOrWhiteSpace(_project.Meta.Name) ? "Package" : _project.Meta.Name)) { Tag = PackageTag };
            _tree.Nodes.Add(root);

            var mods = new TreeNode("Modules") { Tag = "__modules__" };
            root.Nodes.Add(mods);
            foreach (var m in _project.Modules)
                mods.Nodes.Add(new TreeNode(ModuleLabel(m)) { Tag = m });

            var grps = new TreeNode("Groups") { Tag = "__groups__" };
            root.Nodes.Add(grps);
            foreach (var g in _project.Groups)
            {
                var gn = new TreeNode("◧  " + (string.IsNullOrWhiteSpace(g.Title) ? g.Id : g.Title)) { Tag = g };
                foreach (var o in g.Options)
                    gn.Nodes.Add(new TreeNode("•  " + (string.IsNullOrWhiteSpace(o.Name) ? o.Id : o.Name)) { Tag = o });
                grps.Nodes.Add(gn);
            }
            root.ExpandAll();
            _tree.EndUpdate();

            // restore selection
            if (keep != null) { var n = FindNode(_tree.Nodes, keep); if (n != null) _tree.SelectedNode = n; }
            UpdateStatus();
        }

        private static string ModuleLabel(PackModule m) =>
            (m.Required ? "★  " : "○  ") + (string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name);

        private TreeNode FindNode(TreeNodeCollection nodes, object tag)
        {
            foreach (TreeNode n in nodes)
            {
                if (ReferenceEquals(n.Tag, tag)) return n;
                var c = FindNode(n.Nodes, tag);
                if (c != null) return c;
            }
            return null;
        }

        private void SelectPackageRoot() { if (_tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0]; }

        private void AddModule()
        {
            var m = new PackModule { Id = UniqueId("module"), Name = "New Module", Default = false };
            _project.Modules.Add(m);
            MarkDirty(); RebuildTree();
            _tree.SelectedNode = FindNode(_tree.Nodes, m);
        }

        private void AddGroup()
        {
            var g = new PackGroup { Id = UniqueId("group"), Title = "New Group" };
            _project.Groups.Add(g);
            MarkDirty(); RebuildTree();
            _tree.SelectedNode = FindNode(_tree.Nodes, g);
        }

        private void AddOption()
        {
            var g = SelectedGroup();
            if (g == null) { MessageBox.Show("Select a group (or one of its options) first.", "Add Option", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var o = new PackOption { Id = UniqueId("option", g.Options.Select(x => x.Id)), Name = "New Option" };
            g.Options.Add(o);
            MarkDirty(); RebuildTree();
            _tree.SelectedNode = FindNode(_tree.Nodes, o);
        }

        private void RemoveSelected()
        {
            object t = _tree.SelectedNode?.Tag;
            if (t is PackModule m)
            {
                // A package may have no required base at all (a pick-any
                // compilation), so any module can be removed. Export validation
                // still requires at least one module overall.
                _project.Modules.Remove(m);
            }
            else if (t is PackGroup g) _project.Groups.Remove(g);
            else if (t is PackOption o) { _project.Groups.FirstOrDefault(gr => gr.Options.Contains(o))?.Options.Remove(o); }
            else return;
            MarkDirty(); RebuildTree(); SelectPackageRoot();
        }

        private void MoveSelected(int dir)
        {
            object t = _tree.SelectedNode?.Tag;
            if (t is PackModule m) Reorder(_project.Modules, m, dir);
            else if (t is PackGroup g) Reorder(_project.Groups, g, dir);
            else if (t is PackOption o) { var gr = _project.Groups.FirstOrDefault(x => x.Options.Contains(o)); if (gr != null) Reorder(gr.Options, o, dir); }
            else return;
            MarkDirty(); RebuildTree();
            _tree.SelectedNode = FindNode(_tree.Nodes, t);
        }

        private static void Reorder<T>(List<T> list, T item, int dir)
        {
            int i = list.IndexOf(item), j = i + dir;
            if (i < 0 || j < 0 || j >= list.Count) return;
            list.RemoveAt(i); list.Insert(j, item);
        }

        private PackGroup SelectedGroup()
        {
            object t = _tree.SelectedNode?.Tag;
            if (t is PackGroup g) return g;
            if (t is PackOption o) return _project.Groups.FirstOrDefault(x => x.Options.Contains(o));
            return null;
        }

        private string UniqueId(string prefix, IEnumerable<string> extra = null)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in _project.Modules) used.Add(m.Id);
            foreach (var gr in _project.Groups) { used.Add(gr.Id); foreach (var op in gr.Options) used.Add(op.Id); }
            if (extra != null) foreach (var x in extra) used.Add(x);
            for (int i = 1; ; i++) { string id = prefix + i; if (!used.Contains(id)) return id; }
        }

        // ---- editor (right) --------------------------------------------------

        private void ShowEditor(object tag)
        {
            _editor.SuspendLayout();
            _editor.Controls.Clear();
            int y = 0;
            if (tag is string s && s == PackageTag) BuildMetaEditor(ref y);
            else if (tag is PackModule m) BuildInstallableEditor(m, ref y, isModule: true);
            else if (tag is PackOption o) BuildInstallableEditor(o, ref y, isModule: false);
            else if (tag is PackGroup g) BuildGroupEditor(g, ref y);
            else { Header("Select an item on the left, or use + Module / + Group.", ref y, big: false); }
            _editor.ResumeLayout();
        }

        private void BuildMetaEditor(ref int y)
        {
            var meta = _project.Meta;
            Header("Package", ref y);
            TextField("Name", () => meta.Name, v => { meta.Name = v; RebuildTree(); }, ref y);
            Row2(
                p => IntField(p, "Version major", () => meta.VersionMajor, v => meta.VersionMajor = v),
                p => IntField(p, "Version minor", () => meta.VersionMinor, v => meta.VersionMinor = v), ref y);
            ComboField("Game version", new[] { "enhanced", "legacy", "(any)" },
                () => string.IsNullOrEmpty(meta.GameVersion) ? "(any)" : meta.GameVersion,
                v => meta.GameVersion = (v == "(any)" ? "" : v), ref y);
            TextField("Author display name", () => meta.AuthorName, v => meta.AuthorName = v, ref y);
            TextField("Action link (Patreon/store)", () => meta.ActionLink, v => meta.ActionLink = v, ref y);
            TextField("Website / Discord", () => meta.Web, v => meta.Web = v, ref y);
            TextField("YouTube", () => meta.Youtube, v => meta.Youtube = v, ref y);
            MultilineField("Description", () => meta.Description, v => meta.Description = v, ref y);
            MultilineField("Licence", () => meta.License, v => meta.License = v, ref y, height: 50);
            ColorField("Header color (ARGB, e.g. $FF153a75)", () => meta.HeaderBackground, v => meta.HeaderBackground = v, ref y);
            CheckField("Use black header text", () => meta.UseBlackTextColor, v => meta.UseBlackTextColor = v, ref y);
            FilePicker("Header icon (.png, optional)", () => meta.IconPath, v => meta.IconPath = v, "PNG image|*.png", ref y);
        }

        private void BuildInstallableEditor(PackInstallable it, ref int y, bool isModule)
        {
            Header(isModule ? "Module" : "Option", ref y);
            TextField("Id (letters, digits, _ -)", () => it.Id, v => { it.Id = v; RebuildTree(); }, ref y);
            TextField("Display name", () => it.Name, v => { it.Name = v; RebuildTree(); }, ref y);
            if (isModule && it is PackModule m)
            {
                CheckField("Required (always installed)", () => m.Required, v => { m.Required = v; RebuildTree(); }, ref y);
                CheckField("Checked by default (optional modules)", () => m.Default, v => m.Default = v, ref y);
            }
            MultilineField("Description", () => it.Description, v => it.Description = v, ref y);
            FilePicker("Install: .oiv package (optional)", () => it.OivPath, v => it.OivPath = v, "OIV package|*.oiv", ref y);
            FoldersEditor(it, ref y);
            MediaEditor(it, ref y);
        }

        private void BuildGroupEditor(PackGroup g, ref int y)
        {
            Header("Group (single choice)", ref y);
            TextField("Id", () => g.Id, v => { g.Id = v; RebuildTree(); }, ref y);
            TextField("Title", () => g.Title, v => { g.Title = v; RebuildTree(); }, ref y);
            MultilineField("Description", () => g.Description, v => g.Description = v, ref y, height: 50);
            CheckField("Allow \"None\" (let user pick nothing)", () => g.AllowNone, v => g.AllowNone = v, ref y);
            var choices = new List<string> { "none" };
            choices.AddRange(g.Options.Select(o => o.Id));
            ComboField("Default selection", choices.ToArray(), () => g.Default, v => g.Default = v, ref y);
            Note("Add options with the + Option button on the left.", ref y);
        }

        // ---- folders + media sub-editors ------------------------------------

        private void FoldersEditor(PackInstallable it, ref int y)
        {
            SubHeader("Loose folders (copied into the game root)", ref y);
            foreach (var f in it.Folders.ToList())
            {
                var fLocal = f;
                int rowY = y;
                var lblPath = new Label { Text = ShortPath(f.Path), AutoSize = false, Width = 360, Height = 22, Location = new Point(0, rowY), ForeColor = _muted, TextAlign = ContentAlignment.MiddleLeft };
                var btnPick = FlatBtn("Folder…", 370, rowY, (s, e) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog() == DialogResult.OK) { fLocal.Path = d.SelectedPath; MarkDirty(); RefreshEditor(it); } });
                var btnDel = FlatBtn("✕", 452, rowY, (s, e) => { it.Folders.Remove(fLocal); MarkDirty(); RefreshEditor(it); });
                _editor.Controls.Add(lblPath); _editor.Controls.Add(btnPick); _editor.Controls.Add(btnDel);
                y += 28;
            }
            var add = FlatBtn("+ Add folder", 0, y, (s, e) => { it.Folders.Add(new PackFolder()); MarkDirty(); RefreshEditor(it); });
            _editor.Controls.Add(add);
            y += 36;
        }

        private void MediaEditor(PackInstallable it, ref int y)
        {
            SubHeader("Previews (shown in the installer)", ref y);
            var media = it.Media;
            for (int idx = 0; idx < media.Count; idx++)
            {
                var md = media[idx];
                int i = idx, rowY = y;
                string thumbPath = md.IsCompare ? (string.IsNullOrEmpty(md.AfterPath) ? md.BeforePath : md.AfterPath) : md.ImagePath;
                _editor.Controls.Add(Thumb(thumbPath, 0, rowY));
                string desc = md.IsCompare
                    ? $"Compare{(string.IsNullOrEmpty(md.Title) ? "" : " ‘" + md.Title + "’")}: {ShortName(md.BeforePath)} → {ShortName(md.AfterPath)}"
                    : $"Image: {ShortName(md.ImagePath)}{(string.IsNullOrEmpty(md.Title) ? "" : " (" + md.Title + ")")}";
                _editor.Controls.Add(new Label { Text = desc, AutoSize = false, Width = 236, Height = 22, Location = new Point(56, rowY + 6), ForeColor = _muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
                _editor.Controls.Add(IconBtn("▲", 300, rowY + 4, (s, e) => { if (i > 0) { (media[i - 1], media[i]) = (media[i], media[i - 1]); MarkDirty(); RefreshEditor(it); } }));
                _editor.Controls.Add(IconBtn("▼", 330, rowY + 4, (s, e) => { if (i < media.Count - 1) { (media[i + 1], media[i]) = (media[i], media[i + 1]); MarkDirty(); RefreshEditor(it); } }));
                _editor.Controls.Add(FlatBtn("Edit…", 364, rowY + 4, (s, e) => { EditMedia(md); MarkDirty(); RefreshEditor(it); }));
                _editor.Controls.Add(FlatBtn("✕", 440, rowY + 4, (s, e) => { media.Remove(md); MarkDirty(); RefreshEditor(it); }));
                y += 40;
            }
            var addImg = FlatBtn("+ Image", 0, y, (s, e) => { var md = PackMedia.Image(""); if (PickFile(out var p, "Image|*.png;*.jpg;*.jpeg;*.bmp;*.gif")) { md.ImagePath = p; it.Media.Add(md); MarkDirty(); RefreshEditor(it); } });
            var addCmp = FlatBtn("+ Before/After", 90, y, (s, e) => { var md = PackMedia.Compare("", ""); EditMedia(md); if (!string.IsNullOrEmpty(md.BeforePath) || !string.IsNullOrEmpty(md.AfterPath)) { it.Media.Add(md); MarkDirty(); RefreshEditor(it); } });
            _editor.Controls.Add(addImg); _editor.Controls.Add(addCmp);
            y += 44;
        }

        private PictureBox Thumb(string path, int x, int y)
        {
            var pb = new PictureBox { Location = new Point(x, y), Size = new Size(48, 32), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(240, 240, 240) };
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            { try { using var img = Image.FromFile(path); pb.Image = new Bitmap(img); } catch { } }
            return pb;
        }

        private Button IconBtn(string text, int x, int y, EventHandler h)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(28, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            b.Click += h; return b;
        }

        private void EditMedia(PackMedia md)
        {
            using var dlg = new Form { Text = md.IsCompare ? "Before / After" : "Image", Width = 520, Height = md.IsCompare ? 250 : 190, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = _bg };
            int yy = 16;
            TextBox Pick(string label, Func<string> get, Action<string> set)
            {
                var l = new Label { Text = label, Location = new Point(14, yy), AutoSize = true }; dlg.Controls.Add(l);
                var tb = new TextBox { Location = new Point(14, yy + 20), Width = 380, Text = get() ?? "" }; dlg.Controls.Add(tb);
                var b = new Button { Text = "…", Location = new Point(400, yy + 19), Width = 36, FlatStyle = FlatStyle.Flat }; dlg.Controls.Add(b);
                b.Click += (s, e) => { if (PickFile(out var p, "Image|*.png;*.jpg;*.jpeg")) { tb.Text = p; } };
                tb.TextChanged += (s, e) => set(tb.Text);
                yy += 52;
                return tb;
            }
            if (md.IsCompare)
            {
                var t = new Label { Text = "Title (optional, e.g. Day)", Location = new Point(14, yy), AutoSize = true }; dlg.Controls.Add(t);
                var tt = new TextBox { Location = new Point(220, yy - 3), Width = 216, Text = md.Title ?? "" }; dlg.Controls.Add(tt);
                tt.TextChanged += (s, e) => md.Title = tt.Text; yy += 34;
                Pick("Before image", () => md.BeforePath, v => md.BeforePath = v);
                Pick("After image", () => md.AfterPath, v => md.AfterPath = v);
            }
            else
            {
                Pick("Image", () => md.ImagePath, v => md.ImagePath = v);
                var t = new Label { Text = "Caption (optional)", Location = new Point(14, yy), AutoSize = true }; dlg.Controls.Add(t);
                var tt = new TextBox { Location = new Point(150, yy - 3), Width = 286, Text = md.Title ?? "" }; dlg.Controls.Add(tt);
                tt.TextChanged += (s, e) => md.Title = tt.Text; yy += 34;
            }
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(360, yy), Width = 76, FlatStyle = FlatStyle.Flat, BackColor = _accent, ForeColor = Color.White };
            dlg.Controls.Add(ok); dlg.AcceptButton = ok;
            dlg.ShowDialog(this);
        }

        private void RefreshEditor(object obj) => ShowEditor(obj);

        // ---- field helpers ---------------------------------------------------

        private void Header(string text, ref int y, bool big = true)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(0, y), ForeColor = big ? _accent : _muted, Font = new Font("Segoe UI", big ? 14f : 9f, big ? FontStyle.Bold : FontStyle.Regular) };
            _editor.Controls.Add(l); y += big ? 38 : 24;
        }
        private void SubHeader(string text, ref int y)
        {
            y += 6;
            var l = new Label { Text = text.ToUpperInvariant(), AutoSize = true, Location = new Point(0, y), ForeColor = _accent, Font = new Font("Segoe UI", 8.25f, FontStyle.Bold) };
            _editor.Controls.Add(l); y += 24;
        }
        private void Note(string text, ref int y)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(0, y), ForeColor = _muted };
            _editor.Controls.Add(l); y += 24;
        }
        private void TextField(string label, Func<string> get, Action<string> set, ref int y, int width = 460)
        {
            _editor.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, y), ForeColor = _muted });
            var tb = new TextBox { Location = new Point(0, y + 20), Width = width, Text = get() ?? "" };
            tb.TextChanged += (s, e) => { set(tb.Text); MarkDirty(); };
            _editor.Controls.Add(tb); y += 50;
        }
        private void MultilineField(string label, Func<string> get, Action<string> set, ref int y, int height = 70)
        {
            _editor.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, y), ForeColor = _muted });
            var tb = new TextBox { Location = new Point(0, y + 20), Width = 460, Height = height, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = get() ?? "" };
            tb.TextChanged += (s, e) => { set(tb.Text); MarkDirty(); };
            _editor.Controls.Add(tb); y += 24 + height + 8;
        }
        private void IntField(Panel p, string label, Func<int> get, Action<int> set)
        {
            p.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, 0), ForeColor = _muted });
            var nud = new NumericUpDown { Location = new Point(0, 20), Width = 100, Minimum = 0, Maximum = 999, Value = Math.Max(0, get()) };
            nud.ValueChanged += (s, e) => { set((int)nud.Value); MarkDirty(); };
            p.Controls.Add(nud);
        }
        private void CheckField(string label, Func<bool> get, Action<bool> set, ref int y)
        {
            var cb = new CheckBox { Text = label, AutoSize = true, Location = new Point(0, y), Checked = get() };
            cb.CheckedChanged += (s, e) => { set(cb.Checked); MarkDirty(); };
            _editor.Controls.Add(cb); y += 28;
        }
        private void ComboField(string label, string[] items, Func<string> get, Action<string> set, ref int y)
        {
            _editor.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, y), ForeColor = _muted });
            var cb = new ComboBox { Location = new Point(0, y + 20), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(items);
            cb.SelectedItem = items.FirstOrDefault(i => string.Equals(i, get(), StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
            cb.SelectedIndexChanged += (s, e) => { set(cb.SelectedItem?.ToString()); MarkDirty(); };
            _editor.Controls.Add(cb); y += 50;
        }
        private void ColorField(string label, Func<string> get, Action<string> set, ref int y)
        {
            _editor.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, y), ForeColor = _muted });
            var tb = new TextBox { Location = new Point(0, y + 20), Width = 200, Text = get() ?? "" };
            var swatch = new Panel { Location = new Point(210, y + 19), Size = new Size(28, 24), BorderStyle = BorderStyle.FixedSingle };
            void Upd() { try { var c = tb.Text.TrimStart('$'); if (c.Length == 8) swatch.BackColor = Color.FromArgb(255, Convert.ToInt32(c.Substring(2, 2), 16), Convert.ToInt32(c.Substring(4, 2), 16), Convert.ToInt32(c.Substring(6, 2), 16)); } catch { } }
            tb.TextChanged += (s, e) => { set(tb.Text); Upd(); MarkDirty(); }; Upd();
            var pick = FlatBtn("Pick…", 246, y + 19, (s, e) => { using var d = new ColorDialog(); if (d.ShowDialog() == DialogResult.OK) tb.Text = $"$FF{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}"; });
            _editor.Controls.Add(tb); _editor.Controls.Add(swatch); _editor.Controls.Add(pick); y += 50;
        }
        private void FilePicker(string label, Func<string> get, Action<string> set, string filter, ref int y)
        {
            _editor.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, y), ForeColor = _muted });
            var tb = new TextBox { Location = new Point(0, y + 20), Width = 380, Text = get() ?? "" };
            tb.TextChanged += (s, e) => { set(tb.Text); MarkDirty(); };
            var b = FlatBtn("Browse…", 388, y + 19, (s, e) => { if (PickFile(out var p, filter)) tb.Text = p; });
            _editor.Controls.Add(tb); _editor.Controls.Add(b); y += 50;
        }
        private void Row2(Action<Panel> a, Action<Panel> b, ref int y)
        {
            var p1 = new Panel { Location = new Point(0, y), Size = new Size(150, 48) };
            var p2 = new Panel { Location = new Point(170, y), Size = new Size(150, 48) };
            a(p1); b(p2);
            _editor.Controls.Add(p1); _editor.Controls.Add(p2); y += 52;
        }
        private Button FlatBtn(string text, int x, int yy, EventHandler h)
        {
            var b = new Button { Text = text, Location = new Point(x, yy), AutoSize = true, MinimumSize = new Size(76, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            b.Click += h; return b;
        }
        private bool PickFile(out string path, string filter)
        {
            using var d = new OpenFileDialog { Filter = filter + "|All files|*.*" };
            if (d.ShowDialog() == DialogResult.OK) { path = d.FileName; return true; }
            path = null; return false;
        }
        private static string ShortPath(string p) => string.IsNullOrEmpty(p) ? "(not set)" : (p.Length > 52 ? "…" + p.Substring(p.Length - 50) : p);
        private static string ShortName(string p) => string.IsNullOrEmpty(p) ? "(none)" : Path.GetFileName(p);

        // ---- project file ops + export ---------------------------------------

        private void MarkDirty() { _dirty = true; UpdateTitle(); UpdateStatus(); }
        private void UpdateTitle() => Text = "OIVS Packer — " + (_projectPath != null ? Path.GetFileName(_projectPath) : "untitled") + (_dirty ? " *" : "");

        private void NewProject()
        {
            if (!ConfirmDiscard()) return;
            _project = OivsProject.NewDefault(); _projectPath = null; _dirty = false;
            RebuildTree(); SelectPackageRoot(); UpdateTitle(); UpdateStatus();
        }
        private void OpenProject()
        {
            if (!ConfirmDiscard()) return;
            using var d = new OpenFileDialog { Filter = "OIVS project|*.oivsproj|All files|*.*", InitialDirectory = DialogDir() };
            if (d.ShowDialog() != DialogResult.OK) return;
            LoadProjectFile(d.FileName);
        }
        private void LoadProjectFile(string path)
        {
            try
            {
                _project = OivsProject.Load(path); _projectPath = path; _dirty = false;
                _settings.LastDir = Path.GetDirectoryName(path);
                RebuildTree(); SelectPackageRoot(); UpdateTitle(); UpdateStatus();
            }
            catch (Exception ex) { MessageBox.Show("Couldn't open project:\n\n" + ex.Message, "Open", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private bool SaveProject(bool saveAs)
        {
            if (saveAs || _projectPath == null)
            {
                using var d = new SaveFileDialog { Filter = "OIVS project|*.oivsproj", FileName = SafeName() + ".oivsproj", InitialDirectory = DialogDir() };
                if (d.ShowDialog() != DialogResult.OK) return false;
                _projectPath = d.FileName;
            }
            try { _project.Save(_projectPath); _dirty = false; _settings.LastDir = Path.GetDirectoryName(_projectPath); UpdateTitle(); return true; }
            catch (Exception ex) { MessageBox.Show("Couldn't save:\n\n" + ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }
        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            var r = MessageBox.Show("Discard unsaved changes?", "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return r == DialogResult.Yes;
        }

        private string SafeName() => string.Join("_", (_project.Meta.Name ?? "package").Split(Path.GetInvalidFileNameChars()));
        private string DialogDir() => Directory.Exists(_settings.LastDir) ? _settings.LastDir : "";

        private void ExportOivs()
        {
            if (!ValidateOrReport("exporting")) return;
            string outPath;
            using (var d = new SaveFileDialog { Filter = "OIVS package|*.oivs", FileName = SafeName() + ".oivs", InitialDirectory = DialogDir() })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                outPath = d.FileName;
            }
            _settings.LastDir = Path.GetDirectoryName(outPath);
            BuildWithProgress(outPath, "Exporting…", onSuccess: null);
        }

        private void PreviewInInstaller()
        {
            if (!ValidateOrReport("previewing")) return;
            string exe = ResolveInstallerPath();
            if (exe == null) return;
            string tmp = Path.Combine(Path.GetTempPath(), "oivs_preview_" + Guid.NewGuid().ToString("N") + ".oivs");
            BuildWithProgress(tmp, "Building preview…", onSuccess: () =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = exe, Arguments = $"--preview \"{tmp}\"", UseShellExecute = true });
                }
                catch (Exception ex)
                { MessageBox.Show("Couldn't launch the installer:\n\n" + ex.Message, "Preview", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
        }

        private bool ValidateOrReport(string verb)
        {
            var errs = _project.Validate();
            if (errs.Count == 0) return true;
            MessageBox.Show($"Fix these before {verb}:\n\n• " + string.Join("\n• ", errs.Take(20)), "Not ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Runs the build on a worker with a small progress/log window. onSuccess (if
        // given) runs on the UI thread once the build completed without error.
        private void BuildWithProgress(string outPath, string title, Action onSuccess)
        {
            var dlg = new Form { Text = title, Width = 560, Height = 360, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = _bg };
            var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.None, Margin = new Padding(8) };
            var close = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat, Enabled = false };
            close.Click += (s, e) => dlg.Close();
            dlg.Controls.Add(log); dlg.Controls.Add(close);
            void Log(string m) { if (dlg.IsDisposed) return; dlg.BeginInvoke((MethodInvoker)(() => log.AppendText(m + "\r\n"))); }

            Task.Run(() =>
            {
                bool ok = false;
                try { new OivsBuilder(Log) { MaxImageWidth = _settings.MaxImageWidth }.Build(_project, outPath); Log("\r\n✔ Done."); ok = true; }
                catch (Exception ex) { Log("\r\nERROR: " + ex.Message); }
                finally
                {
                    if (!dlg.IsDisposed) dlg.BeginInvoke((MethodInvoker)(() =>
                    {
                        close.Enabled = true;
                        if (ok && onSuccess != null) { onSuccess(); dlg.Close(); }
                    }));
                }
            });
            dlg.ShowDialog(this);
        }

        private string ResolveInstallerPath()
        {
            if (!string.IsNullOrEmpty(_settings.InstallerPath) && File.Exists(_settings.InstallerPath))
                return _settings.InstallerPath;
            // try next to the packer exe
            string near = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CodeWalker.OIVInstaller.exe");
            if (File.Exists(near)) { _settings.InstallerPath = near; return near; }
            // ask once and remember
            MessageBox.Show("To preview, point the packer at the installer (CodeWalker.OIVInstaller.exe). This is remembered.",
                "Locate installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using var d = new OpenFileDialog { Title = "Locate CodeWalker.OIVInstaller.exe", Filter = "Installer|CodeWalker.OIVInstaller.exe|Executable|*.exe" };
            if (d.ShowDialog() != DialogResult.OK) return null;
            _settings.InstallerPath = d.FileName;
            return d.FileName;
        }

        private void ShowHelp()
        {
            MessageBox.Show(
                "OIVS Packer\n\n" +
                "1. Fill in Package info (name, author, game version, icon).\n" +
                "2. Add Modules. Tick 'Required' on a module to make it a base mod that's always installed, or leave them all optional for a pick-any compilation. Each module installs an .oiv and/or copies a loose folder into the game root.\n" +
                "3. Add Groups for single-choice options (e.g. a color the user picks one of).\n" +
                "4. Attach previews (images or before/after pairs) — they show in the installer.\n" +
                "5. Click Preview to open your package in the real installer wizard, and Export .oivs to share. Save your project (.oivsproj) to keep working on it.\n\n" +
                "TIP: drag & drop onto the window —\n" +
                "  • an .oiv file  → adds a module that installs it\n" +
                "  • a folder      → adds a module that copies it into the game root\n" +
                "  • image(s)      → adds them as previews to the selected module/option\n" +
                "  • an .oivsproj  → opens that project\n\n" +
                "Preview images are downscaled and bundled, so the result works offline.",
                "How to use", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            _settings.Maximized = WindowState == FormWindowState.Maximized;
            if (WindowState == FormWindowState.Normal) { _settings.WindowWidth = Width; _settings.WindowHeight = Height; }
            _settings.Save();
            base.OnFormClosing(e);
        }
    }
}
