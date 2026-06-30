using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeWalker.OivsPacker
{
    // ============================================================================
    // Authoring model for a Super OIV (.oivs) package. This is what the GUI edits
    // and what OivsBuilder turns into a .oivs. It also round-trips to/from a JSON
    // project file (.oivsproj) so creators can save and reopen their work.
    //
    // File paths (OivPath, FolderPath, image paths, IconPath) are stored as
    // ABSOLUTE paths to files on the author's machine; nothing is copied until
    // Export.
    // ============================================================================

    public class PackMeta
    {
        public string Name { get; set; } = "";
        public int VersionMajor { get; set; } = 1;
        public int VersionMinor { get; set; } = 0;
        public string AuthorName { get; set; } = "";
        public string ActionLink { get; set; } = "";   // Patreon / store link
        public string Web { get; set; } = "";           // Discord / website
        public string Youtube { get; set; } = "";
        public string Description { get; set; } = "";
        public string License { get; set; } = "";
        public string GameVersion { get; set; } = "enhanced"; // "enhanced" | "legacy" | ""(any)
        public string HeaderBackground { get; set; } = "$FF153a75";
        public bool UseBlackTextColor { get; set; } = false;
        public string IconPath { get; set; } = "";      // optional .png on disk
    }

    /// <summary>A preview: either a before/after comparison or a single showcase image.</summary>
    public class PackMedia
    {
        public bool IsCompare { get; set; }
        public string Title { get; set; } = "";       // compare title or image caption
        public string BeforePath { get; set; } = "";  // compare: file on disk
        public string AfterPath { get; set; } = "";   // compare: file on disk
        public string ImagePath { get; set; } = "";   // single image: file on disk

        public static PackMedia Image(string path, string caption = "") =>
            new PackMedia { IsCompare = false, ImagePath = path, Title = caption };
        public static PackMedia Compare(string before, string after, string title = "") =>
            new PackMedia { IsCompare = true, BeforePath = before, AfterPath = after, Title = title };
    }

    /// <summary>A loose folder copied into the game root on install.</summary>
    public class PackFolder
    {
        public string Sub { get; set; } = "";   // namespace subfolder ("" = the module root)
        public string Path { get; set; } = "";  // source directory on disk
    }

    /// <summary>Shared base for a module or a group option.</summary>
    public abstract class PackInstallable
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string OivPath { get; set; } = "";          // optional .oiv on disk
        public List<PackFolder> Folders { get; set; } = new List<PackFolder>();
        public List<PackMedia> Media { get; set; } = new List<PackMedia>();
    }

    public class PackModule : PackInstallable
    {
        public bool Required { get; set; }
        public bool Default { get; set; }
    }

    public class PackOption : PackInstallable { }

    public class PackGroup
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool AllowNone { get; set; } = true;
        public string Default { get; set; } = "none";  // "none" or an option id
        public List<PackOption> Options { get; set; } = new List<PackOption>();
    }

    public class OivsProject
    {
        public PackMeta Meta { get; set; } = new PackMeta();
        public List<PackModule> Modules { get; set; } = new List<PackModule>();
        public List<PackGroup> Groups { get; set; } = new List<PackGroup>();

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public void Save(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));

        public static OivsProject Load(string path) =>
            JsonSerializer.Deserialize<OivsProject>(File.ReadAllText(path), JsonOpts) ?? new OivsProject();

        /// <summary>A new project pre-seeded with a required base module.</summary>
        public static OivsProject NewDefault()
        {
            var p = new OivsProject();
            p.Meta.Name = "My Mod";
            p.Modules.Add(new PackModule
            {
                Id = "base", Name = "Base Mod", Required = true,
                Description = "The main mod. Always installed.",
            });
            return p;
        }

        // ---- validation ------------------------------------------------------

        /// <summary>Returns a list of human-readable problems; empty = ready to export.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Meta.Name)) errors.Add("Package name is required.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Note: a required (base) module is NOT mandatory — a package may be a
            // pick-any compilation where every module is optional.
            foreach (var m in Modules)
            {
                CheckId(m.Id, $"module '{m.Name}'", ids, errors);
                CheckInstallable(m, $"module '{m.Name}'", errors);
            }
            if (Modules.Count == 0) errors.Add("Add at least one module.");

            foreach (var g in Groups)
            {
                CheckId(g.Id, $"group '{g.Title}'", ids, errors);
                if (g.Options.Count == 0) errors.Add($"Group '{g.Title}' has no options.");
                var optIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var o in g.Options)
                {
                    if (string.IsNullOrWhiteSpace(o.Id))
                        errors.Add($"An option in group '{g.Title}' has no id.");
                    else if (!optIds.Add(o.Id))
                        errors.Add($"Duplicate option id '{o.Id}' in group '{g.Title}'.");
                    CheckInstallable(o, $"option '{o.Name}' in group '{g.Title}'", errors);
                }
            }
            return errors;
        }

        private static void CheckId(string id, string what, HashSet<string> seen, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id)) { errors.Add($"{what} has no id."); return; }
            if (!seen.Add(id)) errors.Add($"Duplicate id '{id}' ({what}).");
            foreach (char c in id)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                { errors.Add($"Id '{id}' ({what}) may only use letters, digits, '_' and '-'."); break; }
        }

        private static void CheckInstallable(PackInstallable it, string what, List<string> errors)
        {
            bool hasOiv = !string.IsNullOrWhiteSpace(it.OivPath);
            bool hasFolder = it.Folders.Exists(f => !string.IsNullOrWhiteSpace(f.Path));
            if (!hasOiv && !hasFolder)
                errors.Add($"{what} installs nothing — add an .oiv or a folder.");
            if (hasOiv && !File.Exists(it.OivPath))
                errors.Add($"{what}: .oiv file not found: {it.OivPath}");
            foreach (var f in it.Folders)
                if (!string.IsNullOrWhiteSpace(f.Path) && !Directory.Exists(f.Path))
                    errors.Add($"{what}: folder not found: {f.Path}");
            foreach (var m in it.Media)
            {
                if (m.IsCompare)
                {
                    if (!string.IsNullOrWhiteSpace(m.BeforePath) && !File.Exists(m.BeforePath))
                        errors.Add($"{what}: 'before' image not found: {m.BeforePath}");
                    if (!string.IsNullOrWhiteSpace(m.AfterPath) && !File.Exists(m.AfterPath))
                        errors.Add($"{what}: 'after' image not found: {m.AfterPath}");
                }
                else if (!string.IsNullOrWhiteSpace(m.ImagePath) && !File.Exists(m.ImagePath))
                    errors.Add($"{what}: image not found: {m.ImagePath}");
            }
        }
    }
}
