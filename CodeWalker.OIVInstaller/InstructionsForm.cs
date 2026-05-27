using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace CodeWalker.OIVInstaller
{
    /// <summary>
    /// Shows a tree view of the parsed assembly.xml operations so a user can preview
    /// exactly what files / archives an OIV will touch before installing. Read-only.
    /// </summary>
    public partial class InstructionsForm : Form
    {
        private readonly OivPackage _package;

        public InstructionsForm(OivPackage package)
        {
            InitializeComponent();
            _package = package;
            if (_package?.Metadata != null)
            {
                lblPackage.Text = $"Install steps for: {_package.Metadata.Name}";
                this.Text = $"Install Steps - {_package.Metadata.Name}";
            }
            PopulateTree();
        }

        private void PopulateTree()
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            int total = 0;
            if (_package?.Operations != null)
            {
                foreach (var op in _package.Operations)
                {
                    var node = tree.Nodes.Add(NodeText(op));
                    node.Tag = op;
                    total += AddChildren(node, op);
                    total += 1;
                }
            }
            // Expand the first level so users immediately see the archive structure.
            foreach (TreeNode n in tree.Nodes) n.Expand();
            lblSummary.Text = total == 0
                ? "No operations declared in this package."
                : $"{total} operation{(total == 1 ? "" : "s")} across {tree.Nodes.Count} top-level entr{(tree.Nodes.Count == 1 ? "y" : "ies")}.";
            tree.EndUpdate();

            if (tree.Nodes.Count > 0)
            {
                tree.SelectedNode = tree.Nodes[0];
            }
        }

        // Recursively adds child nodes for nested archives and the per-edit children of
        // text / xml / pso operations. Returns the count of *descendant* operations
        // added (so the summary line at the top can report a true total).
        private int AddChildren(TreeNode parent, OivOperation op)
        {
            int count = 0;
            switch (op)
            {
                case OivArchiveOperation archive:
                    foreach (var child in archive.Children)
                    {
                        var n = parent.Nodes.Add(NodeText(child));
                        n.Tag = child;
                        count += AddChildren(n, child);
                        count += 1;
                    }
                    break;
                case OivTextOperation text:
                    foreach (var ins in text.Inserts)
                    {
                        var n = parent.Nodes.Add(NodeText(ins));
                        n.Tag = ins;
                        count += 1;
                    }
                    foreach (var rep in text.Replacements)
                    {
                        var n = parent.Nodes.Add(NodeText(rep));
                        n.Tag = rep;
                        count += 1;
                    }
                    foreach (var add in text.Adds)
                    {
                        var n = parent.Nodes.Add(NodeText(add));
                        n.Tag = add;
                        count += 1;
                    }
                    foreach (var del in text.Deletions)
                    {
                        var n = parent.Nodes.Add(NodeText(del));
                        n.Tag = del;
                        count += 1;
                    }
                    break;
                case OivXmlOperation xml:
                    foreach (var add in xml.Adds)
                    {
                        var n = parent.Nodes.Add(NodeText(add));
                        n.Tag = add;
                        count += 1;
                    }
                    foreach (var rep in xml.Replacements)
                    {
                        var n = parent.Nodes.Add(NodeText(rep));
                        n.Tag = rep;
                        count += 1;
                    }
                    foreach (var rem in xml.Removals)
                    {
                        var n = parent.Nodes.Add(NodeText(rem));
                        n.Tag = rem;
                        count += 1;
                    }
                    break;
            }
            return count;
        }

        // Brief one-line summary for each operation type. Detailed multi-line bodies
        // (full content for Insert / Replace / XmlAdd, etc.) are rendered in the
        // details pane on the right when the node is selected.
        private static string NodeText(OivOperation op)
        {
            switch (op)
            {
                case OivArchiveOperation a:
                    return $"[Archive] {a.ArchivePath}  ({a.Children.Count} op{(a.Children.Count == 1 ? "" : "s")})";
                case OivAddOperation add:
                    return $"[Add] {add.Destination}";
                case OivDeleteOperation del:
                    return $"[Delete] {del.Target}";
                case OivTextOperation text:
                    int textEdits = text.Inserts.Count + text.Replacements.Count + text.Adds.Count + text.Deletions.Count;
                    return $"[Text] {text.FilePath}  ({textEdits} edit{(textEdits == 1 ? "" : "s")})";
                case OivPsoOperation pso:
                    int psoEdits = pso.Adds.Count + pso.Replacements.Count + pso.Removals.Count;
                    return $"[Pso] {pso.FilePath}  ({psoEdits} edit{(psoEdits == 1 ? "" : "s")})";
                case OivXmlOperation xml:
                    int xmlEdits = xml.Adds.Count + xml.Replacements.Count + xml.Removals.Count;
                    return $"[Xml] {xml.FilePath}  ({xmlEdits} edit{(xmlEdits == 1 ? "" : "s")})";
                case OivDefragmentationOperation defrag:
                    return $"[Defragment] {defrag.Variable}";
                case OivInsertOperation ins:
                    return $"Insert {ins.Where} → {Truncate(ins.LinePattern, 60)}";
                case OivReplaceOperation rep:
                    return $"Replace ({rep.Condition}) → {Truncate(rep.LinePattern, 60)}";
                case OivTextAddOperation tadd:
                    return $"Append line: {Truncate(tadd.Content, 60)}";
                case OivTextDeleteOperation tdel:
                    return $"Delete line ({tdel.Condition}): {Truncate(tdel.Content, 60)}";
                case OivXmlAddOperation xadd:
                    return $"XmlAdd ({xadd.Append}) @ {Truncate(xadd.XPath, 70)}";
                case OivXmlReplaceOperation xrep:
                    return $"XmlReplace @ {Truncate(xrep.XPath, 70)}";
                case OivXmlRemoveOperation xrem:
                    return $"XmlRemove @ {Truncate(xrem.XPath, 70)}";
                default:
                    return op?.OperationType ?? "<unknown>";
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        private void tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is OivOperation op)
            {
                txtDetails.Text = BuildDetails(op);
            }
            else
            {
                txtDetails.Text = "";
            }
        }

        // Verbose multi-line dump of every relevant field on the selected operation.
        // Kept plain-text so it copy-pastes cleanly out of the read-only RichTextBox.
        private static string BuildDetails(OivOperation op)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Operation: {op.OperationType}");
            sb.AppendLine(new string('-', 60));

            switch (op)
            {
                case OivArchiveOperation a:
                    sb.AppendLine($"Archive path : {a.ArchivePath}");
                    sb.AppendLine($"Archive type : {a.Type}");
                    sb.AppendLine($"Create if missing: {a.CreateIfNotExist}");
                    sb.AppendLine($"Nested operations: {a.Children.Count}");
                    break;
                case OivAddOperation add:
                    sb.AppendLine($"Source (in OIV) : {add.Source}");
                    sb.AppendLine($"Destination     : {add.Destination}");
                    break;
                case OivDeleteOperation del:
                    sb.AppendLine($"Target: {del.Target}");
                    break;
                case OivTextOperation text:
                    sb.AppendLine($"File path : {text.FilePath}");
                    sb.AppendLine($"Create if missing: {text.CreateIfNotExist}");
                    sb.AppendLine($"Inserts   : {text.Inserts.Count}");
                    sb.AppendLine($"Replaces  : {text.Replacements.Count}");
                    sb.AppendLine($"Adds      : {text.Adds.Count}");
                    sb.AppendLine($"Deletes   : {text.Deletions.Count}");
                    break;
                case OivPsoOperation pso:
                    sb.AppendLine($"File path : {pso.FilePath}");
                    sb.AppendLine($"Adds      : {pso.Adds.Count}");
                    sb.AppendLine($"Replaces  : {pso.Replacements.Count}");
                    sb.AppendLine($"Removes   : {pso.Removals.Count}");
                    break;
                case OivXmlOperation xml:
                    sb.AppendLine($"File path : {xml.FilePath}");
                    sb.AppendLine($"Adds      : {xml.Adds.Count}");
                    sb.AppendLine($"Replaces  : {xml.Replacements.Count}");
                    sb.AppendLine($"Removes   : {xml.Removals.Count}");
                    break;
                case OivDefragmentationOperation defrag:
                    sb.AppendLine($"Archive: {defrag.Variable}");
                    break;
                case OivInsertOperation ins:
                    sb.AppendLine($"Where    : {ins.Where}");
                    sb.AppendLine($"Condition: {ins.Condition}");
                    sb.AppendLine($"Match    : {ins.LinePattern}");
                    sb.AppendLine();
                    sb.AppendLine("Content:");
                    sb.AppendLine(ins.Content ?? "");
                    break;
                case OivReplaceOperation rep:
                    sb.AppendLine($"Condition: {rep.Condition}");
                    sb.AppendLine($"Match    : {rep.LinePattern}");
                    sb.AppendLine();
                    sb.AppendLine("Replacement:");
                    sb.AppendLine(rep.Content ?? "");
                    break;
                case OivTextAddOperation tadd:
                    sb.AppendLine("Append:");
                    sb.AppendLine(tadd.Content ?? "");
                    break;
                case OivTextDeleteOperation tdel:
                    sb.AppendLine($"Condition: {tdel.Condition}");
                    sb.AppendLine($"Match    : {tdel.Content}");
                    break;
                case OivXmlAddOperation xadd:
                    sb.AppendLine($"XPath  : {xadd.XPath}");
                    sb.AppendLine($"Append : {xadd.Append}");
                    sb.AppendLine();
                    sb.AppendLine("Content:");
                    sb.AppendLine(xadd.Content ?? "");
                    break;
                case OivXmlReplaceOperation xrep:
                    sb.AppendLine($"XPath: {xrep.XPath}");
                    sb.AppendLine();
                    sb.AppendLine("New content:");
                    sb.AppendLine(xrep.Content ?? "");
                    break;
                case OivXmlRemoveOperation xrem:
                    sb.AppendLine($"XPath: {xrem.XPath}");
                    break;
                default:
                    sb.AppendLine(op.ToString());
                    break;
            }
            return sb.ToString();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
