using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SwWeb.Checking;

namespace SwWeb.Migrating
{
    /// <summary>
    /// How a legacy name is migrated.
    /// </summary>
    public enum MapKind
    {
        /// <summary>
        /// Replace the identifier with <see cref="MapEntry.New"/> (optionally only calls with at most <see cref="MapEntry.MaxArgs"/> arguments).
        /// </summary>
        Rename,
        /// <summary>
        /// <c>old()</c> (a call without arguments) becomes the property <see cref="MapEntry.New"/>; other uses are reported.
        /// </summary>
        Property,
        /// <summary>
        /// Never rewritten; every use is reported with <see cref="MapEntry.Suggest"/>.
        /// </summary>
        Report,
        /// <summary>
        /// Not a legacy name: no rewrite, no report.
        /// </summary>
        Ignore,
    }

    /// <summary>
    /// A mapping of one legacy global (or member of a legacy object).
    /// </summary>
    public sealed class MapEntry
    {
        /// <summary>
        /// The old name (global name, or member name inside <see cref="Members"/>).
        /// </summary>
        public String Name;
        /// <summary>
        /// The section that defined the entry.
        /// </summary>
        public String Section;
        /// <summary>
        /// The kind.
        /// </summary>
        public MapKind Kind = MapKind.Rename;
        /// <summary>
        /// The replacement expression (ex: "Sw.Util.Delay"), null if none.
        /// </summary>
        public String New;
        /// <summary>
        /// Rename only calls with at most this many arguments (-1 = any use).
        /// </summary>
        public int MaxArgs = -1;
        /// <summary>
        /// With <see cref="Options"/>: the number of arguments kept positional, the following ones become an options object.
        /// </summary>
        public int Positional;
        /// <summary>
        /// Option names for the arguments after <see cref="Positional"/> (null = no conversion): f(a, b) -> New(a, { Key: b }).
        /// </summary>
        public String[] Options;
        /// <summary>
        /// The new argument list of a call, null = the same arguments: "$0, [$1, $2], $4, $3" makes waitEvent2(el, a, b, fn, ms) the call
        /// New(el, [a, b], ms, fn). "$n" is the n:th old argument ("undefined" when it wasn't passed, trailing ones are left out), each at most
        /// once; old arguments that aren't used must be simple values (dropped). Arguments that change their evaluation order must be free
        /// of side effects (names, literals, member reads, functions); other calls are reported.
        /// </summary>
        public String Args;
        /// <summary>
        /// A suggested new call for reported uses.
        /// </summary>
        public String Suggest;
        /// <summary>
        /// A note about semantic differences.
        /// </summary>
        public String Note;
        /// <summary>
        /// True if the name is a shell feature (pages using it load sw/shell.js).
        /// </summary>
        public bool Shell;
        /// <summary>
        /// The library file that still provides the old name as a legacy alias (section "Library", ex: "explore/table.js"), null for the
        /// names of the old common.js / application.js. Uses that can't be renamed are reported, but they keep working without sw/compat.js.
        /// </summary>
        public String Library;
        /// <summary>
        /// Member mappings for legacy objects (null if not an object entry).
        /// </summary>
        public Dictionary<String, MapEntry> Members;

        /// <summary>
        /// The text used in reports for the suggested replacement.
        /// </summary>
        public String SuggestText => Suggest ?? New ?? "(no counterpart)";
    }

    /// <summary>
    /// A CSS selector hook mapping (".OldClass" or "OldElement" to new selector text).
    /// </summary>
    public sealed class CssEntry
    {
        /// <summary>
        /// The old selector: ".Class", "#id" or an element name.
        /// </summary>
        public String Old;
        /// <summary>
        /// The new selector text.
        /// </summary>
        public String New;
        /// <summary>
        /// True if the rename is safe to apply automatically (else it is only reported).
        /// </summary>
        public bool Safe;
        /// <summary>
        /// True if the core still draws the old element on pages without sw/ui.css (pages with their own UI style it): "upgrade" reports it as info.
        /// </summary>
        public bool OwnUi;
        /// <summary>
        /// The section that defined the entry.
        /// </summary>
        public String Section;
        /// <summary>
        /// An optional note.
        /// </summary>
        public String Note;
    }

    /// <summary>
    /// A CSS custom property (design token) mapping: uses <c>var(--old)</c> in css, style attributes and JavaScript strings become
    /// <c>var(--new)</c> when <see cref="Safe"/> (the old name is an alias of the new one, so the value is identical); other entries are
    /// reported by the Design v2 rules. Definitions (<c>--old: value</c>) are never rewritten (reported: an override of an alias doesn't
    /// restyle the components any more).
    /// </summary>
    public sealed class TokenEntry
    {
        /// <summary>
        /// The old custom property name, ex: "--sw-bg-1".
        /// </summary>
        public String Old;
        /// <summary>
        /// The new custom property name (Safe entries) or the suggested replacement text, ex: "--sw-surface".
        /// </summary>
        public String New;
        /// <summary>
        /// True if <c>var(Old)</c> is always the same value as <c>var(New)</c> (an alias), so uses are renamed automatically.
        /// </summary>
        public bool Safe;
        /// <summary>
        /// True for a legacy theme variable with one context-free replacement (<c>var(--ThemeAcc2)</c> = <c>var(--sw-accent2)</c>,
        /// <c>var(--ThemeFadeDuration)</c> = <c>var(--sw-dur-2)</c>): "migrate --level design" renames its uses (the role token follows the
        /// same theme; its value is the Design v2 value). Role dependent variables (<c>--ThemeMain</c>, <c>--ThemeBackground</c>, the derived
        /// <c>*25</c> / <c>*50</c> mixes) are not "Apply": they stay a manual step (design-legacy-token).
        /// </summary>
        public bool Apply;
        /// <summary>
        /// With <see cref="Apply"/>: the replacement in a text colour property (color, fill, caret-color ...), ex: <c>--sw-text-accent</c>
        /// for <c>--ThemeAcc1</c> (the accent tuned for text contrast); null = <see cref="New"/> everywhere.
        /// </summary>
        public String TextNew;
        /// <summary>
        /// The section that defined the entry.
        /// </summary>
        public String Section;
        /// <summary>
        /// An optional note.
        /// </summary>
        public String Note;

        /// <summary>
        /// The replacement token of an <see cref="Apply"/> entry in a css property (<see cref="TextNew"/> in text colour properties).
        /// </summary>
        /// <param name="prop">The css property (lower case), null if unknown</param>
        /// <returns>The new custom property name</returns>
        public String NewFor(String prop) => ((TextNew != null) && IsTextColorProperty(prop)) ? TextNew : New;

        /// <summary>
        /// True if migrate renames a use in a css property: an <see cref="Apply"/> entry everywhere, an entry with only <see cref="TextNew"/>
        /// (a role dependent variable such as --ThemeMain, whose use as a text colour has one answer: --sw-text) in the text colour properties.
        /// </summary>
        /// <param name="prop">The css property (lower case), null if unknown</param>
        public bool AppliesTo(String prop) => Apply || ((TextNew != null) && IsTextColorProperty(prop));

        /// <summary>
        /// True for the css properties that colour text (color, fill, caret-color ...).
        /// </summary>
        public static bool IsTextColorProperty(String prop) => (prop == "color") || (prop == "fill") || (prop == "caret-color") || (prop == "-webkit-text-fill-color") || (prop == "text-decoration-color");
    }

    /// <summary>
    /// The migration mapping table (sections per library: core, ui, shell, later table, edit, chat ...).
    /// The default is embedded (migrate_map.json); a --map file extends / overrides it.
    /// </summary>
    public sealed class MigrationMap
    {
        /// <summary>
        /// Globals that are never touched (permanent API: translation functions, theme functions, explore Build, the shell appMain).
        /// </summary>
        public static readonly IReadOnlyList<String> NeverTouch = new[]
        {
            "_T", "_TF", "_TH", "_TFH", "setTheme", "resetTheme", "toggleTheme", "appendTheme", "getTheme", "applyTheme", "Build", "appMain",
        };

        static readonly HashSet<String> NeverTouchSet = new HashSet<String>(NeverTouch, StringComparer.Ordinal);

        /// <summary>
        /// Test if a name is never touched.
        /// </summary>
        public static bool IsNeverTouch(String name) => NeverTouchSet.Contains(name);

        /// <summary>
        /// Global entries by old name.
        /// </summary>
        public readonly Dictionary<String, MapEntry> Globals = new Dictionary<String, MapEntry>(StringComparer.Ordinal);

        /// <summary>
        /// CSS entries by old selector.
        /// </summary>
        public readonly Dictionary<String, CssEntry> Css = new Dictionary<String, CssEntry>(StringComparer.Ordinal);

        /// <summary>
        /// Design token entries by old custom property name (case sensitive, like css custom properties).
        /// </summary>
        public readonly Dictionary<String, TokenEntry> Tokens = new Dictionary<String, TokenEntry>(StringComparer.Ordinal);

        /// <summary>
        /// The <see cref="MapEntry.Options"/> name that drops an argument (the new API ignores it; only simple arguments are dropped).
        /// </summary>
        public const String DropOption = "-";

        /// <summary>
        /// Section names and descriptions in load order.
        /// </summary>
        public readonly List<(String Name, String Description)> Sections = new List<(String, String)>();

        /// <summary>
        /// Legacy names without a mapping (from legacy_globals.txt): every use is reported.
        /// </summary>
        public readonly HashSet<String> UnmappedLegacy = new HashSet<String>(StringComparer.Ordinal);

        /// <summary>
        /// Warnings found while loading (ignored entries).
        /// </summary>
        public readonly List<String> LoadWarnings = new List<String>();

        /// <summary>
        /// The resource / file name of the default map.
        /// </summary>
        public const String DefaultMapFileName = "migrate_map.json";

        /// <summary>
        /// Get the embedded default map text.
        /// </summary>
        public static String GetDefaultText()
        {
            using var s = typeof(MigrationMap).Assembly.GetManifestResourceStream("SwWeb." + DefaultMapFileName);
            if (s == null)
                throw new Exception("The embedded " + DefaultMapFileName + " is missing");
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        /// <summary>
        /// Load the default map, optionally extended by a map file, plus the legacy globals list (names without an entry are reported).
        /// </summary>
        /// <param name="mapFile">An optional --map file</param>
        /// <param name="legacyGlobals">The legacy global names (null = the default legacy_globals.txt)</param>
        /// <returns>The map</returns>
        public static MigrationMap Load(String mapFile = null, IEnumerable<String> legacyGlobals = null)
        {
            var m = new MigrationMap();
            m.Merge(GetDefaultText(), "(default)");
            if (mapFile != null)
                m.Merge(File.ReadAllText(mapFile), mapFile);
            foreach (var n in legacyGlobals ?? CheckOptions.LoadLegacyGlobals(null))
                if (!m.Globals.ContainsKey(n) && !IsNeverTouch(n))
                    m.UnmappedLegacy.Add(n);
            return m;
        }

        /// <summary>
        /// Create a map from json text only (no default, no legacy list), used by tests.
        /// </summary>
        public static MigrationMap FromJson(String json)
        {
            var m = new MigrationMap();
            m.Merge(json, "(json)");
            return m;
        }

        static readonly JsonDocumentOptions JsonOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>
        /// Merge a map (json text): sections are added, entries with the same name replace existing ones.
        /// A root member "Replace": true clears the current entries first.
        /// </summary>
        /// <param name="json">The json</param>
        /// <param name="source">The source name (messages)</param>
        public void Merge(String json, String source)
        {
            using var doc = JsonDocument.Parse(json, JsonOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new Exception(source + ": the map must be a json object");
            if (root.TryGetProperty("Replace", out var rep) && (rep.ValueKind == JsonValueKind.True))
            {
                Globals.Clear();
                Css.Clear();
                Tokens.Clear();
                Sections.Clear();
            }
            if (!root.TryGetProperty("Sections", out var secs))
                return;
            if (secs.ValueKind != JsonValueKind.Object)
                throw new Exception(source + ": \"Sections\" must be an object (section name: section)");
            foreach (var sp in secs.EnumerateObject())
            {
                var sec = sp.Value;
                if (sec.ValueKind != JsonValueKind.Object)
                    throw new Exception(source + ": section \"" + sp.Name + "\" must be an object");
                var desc = sec.TryGetProperty("Description", out var d) && (d.ValueKind == JsonValueKind.String) ? d.GetString() : null;
                var lib = GetString(sec, "Library");
                var si = Sections.FindIndex(x => x.Name == sp.Name);
                if (si < 0)
                    Sections.Add((sp.Name, desc));
                else if (desc != null)
                    Sections[si] = (sp.Name, desc);
                if (sec.TryGetProperty("Globals", out var gl))
                {
                    if (gl.ValueKind != JsonValueKind.Object)
                        throw new Exception(source + ": \"" + sp.Name + ".Globals\" must be an object");
                    foreach (var gp in gl.EnumerateObject())
                    {
                        if (IsNeverTouch(gp.Name))
                        {
                            // Only the shell flag of appMain is used, never a rewrite
                            var e0 = ParseEntry(gp.Name, gp.Value, sp.Name, source + ": " + sp.Name + "." + gp.Name, true, lib);
                            if (e0.Kind != MapKind.Ignore)
                                LoadWarnings.Add(source + ": \"" + gp.Name + "\" is a permanent global and is never rewritten (entry treated as Ignore)");
                            e0.Kind = MapKind.Ignore;
                            e0.New = null;
                            e0.Members = null;
                            Globals[gp.Name] = e0;
                            continue;
                        }
                        Globals[gp.Name] = ParseEntry(gp.Name, gp.Value, sp.Name, source + ": " + sp.Name + "." + gp.Name, true, lib);
                    }
                }
                if (sec.TryGetProperty("Css", out var css))
                {
                    if (css.ValueKind != JsonValueKind.Object)
                        throw new Exception(source + ": \"" + sp.Name + ".Css\" must be an object");
                    foreach (var cp in css.EnumerateObject())
                    {
                        var ce = new CssEntry { Old = cp.Name, Section = sp.Name };
                        if (cp.Value.ValueKind == JsonValueKind.String)
                            ce.New = cp.Value.GetString();
                        else if (cp.Value.ValueKind == JsonValueKind.Object)
                        {
                            ce.New = GetString(cp.Value, "New");
                            ce.Note = GetString(cp.Value, "Note");
                            ce.Safe = cp.Value.TryGetProperty("Safe", out var sf) && (sf.ValueKind == JsonValueKind.True);
                            ce.OwnUi = cp.Value.TryGetProperty("OwnUi", out var ou) && (ou.ValueKind == JsonValueKind.True);
                        }
                        else
                            throw new Exception(source + ": css entry \"" + cp.Name + "\" must be a string or an object");
                        if (String.IsNullOrWhiteSpace(cp.Name) || String.IsNullOrWhiteSpace(ce.New))
                            throw new Exception(source + ": css entry \"" + cp.Name + "\" needs a name and \"New\"");
                        Css[cp.Name] = ce;
                    }
                }
                if (sec.TryGetProperty("Tokens", out var tok))
                {
                    if (tok.ValueKind != JsonValueKind.Object)
                        throw new Exception(source + ": \"" + sp.Name + ".Tokens\" must be an object");
                    foreach (var tp in tok.EnumerateObject())
                    {
                        var te = new TokenEntry { Old = tp.Name, Section = sp.Name };
                        if (tp.Value.ValueKind == JsonValueKind.String)
                            te.New = tp.Value.GetString();
                        else if (tp.Value.ValueKind == JsonValueKind.Object)
                        {
                            te.New = GetString(tp.Value, "New");
                            te.Note = GetString(tp.Value, "Note");
                            te.Safe = tp.Value.TryGetProperty("Safe", out var sf) && (sf.ValueKind == JsonValueKind.True);
                            te.Apply = tp.Value.TryGetProperty("Apply", out var ap) && (ap.ValueKind == JsonValueKind.True);
                            te.TextNew = GetString(tp.Value, "TextNew");
                        }
                        else
                            throw new Exception(source + ": token entry \"" + tp.Name + "\" must be a string or an object");
                        if (!IsCustomProperty(tp.Name) || String.IsNullOrWhiteSpace(te.New))
                            throw new Exception(source + ": token entry \"" + tp.Name + "\" needs a custom property name (--name) and \"New\"");
                        if (te.Safe && !IsCustomProperty(te.New))
                            throw new Exception(source + ": the safe token entry \"" + tp.Name + "\" needs a custom property name as \"New\" (ex: \"--sw-surface\")");
                        if ((te.Apply || (te.TextNew != null)) && (!IsCustomProperty(te.New) || ((te.TextNew != null) && !IsCustomProperty(te.TextNew))))
                            throw new Exception(source + ": the token entry \"" + tp.Name + "\" with \"Apply\" / \"TextNew\" needs custom property names (ex: \"--sw-accent2\")");
                        Tokens[tp.Name] = te;
                    }
                }
            }
        }

        /// <summary>
        /// True for a css custom property name ("--" followed by letters, digits, '-' or '_').
        /// </summary>
        public static bool IsCustomProperty(String s) =>
            (s != null) && (s.Length > 2) && s.StartsWith("--", StringComparison.Ordinal) && s.Skip(2).All(c => Char.IsLetterOrDigit(c) || (c == '-') || (c == '_'));

        static String GetString(JsonElement e, String name) =>
            e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.String) ? v.GetString() : null;

        static MapEntry ParseEntry(String name, JsonElement v, String section, String where, bool allowMembers, String library)
        {
            var e = new MapEntry { Name = name, Section = section, Library = library };
            if (v.ValueKind == JsonValueKind.String)
            {
                e.New = v.GetString();
                Validate(e, where);
                return e;
            }
            if (v.ValueKind != JsonValueKind.Object)
                throw new Exception(where + ": an entry must be a string or an object");
            e.New = GetString(v, "New");
            e.Suggest = GetString(v, "Suggest");
            e.Note = GetString(v, "Note");
            e.Shell = v.TryGetProperty("Shell", out var sh) && (sh.ValueKind == JsonValueKind.True);
            if (v.TryGetProperty("MaxArgs", out var ma))
            {
                if (ma.ValueKind != JsonValueKind.Number)
                    throw new Exception(where + ": \"MaxArgs\" must be a number");
                e.MaxArgs = ma.GetInt32();
            }
            if (v.TryGetProperty("Positional", out var pos))
            {
                if (pos.ValueKind != JsonValueKind.Number)
                    throw new Exception(where + ": \"Positional\" must be a number");
                e.Positional = pos.GetInt32();
            }
            if (v.TryGetProperty("Options", out var opts))
            {
                //  "-" drops the argument (an argument the new API ignores, ex: rowsPerPage of Table.addTable)
                if ((opts.ValueKind != JsonValueKind.Array) || opts.EnumerateArray().Any(x => (x.ValueKind != JsonValueKind.String) || !(IsIdentifier(x.GetString()) || (x.GetString() == DropOption))))
                    throw new Exception(where + ": \"Options\" must be an array of identifiers (or \"-\" to drop an argument)");
                e.Options = opts.EnumerateArray().Select(x => x.GetString()).ToArray();
            }
            e.Args = GetString(v, "Args");
            if (e.Args != null)
            {
                if (e.Options != null || e.MaxArgs >= 0)
                    throw new Exception(where + ": \"Args\" can't be combined with \"Options\" or \"MaxArgs\"");
                var refs = ArgRefs(e.Args);
                if ((refs == null) || (refs.Distinct().Count() != refs.Count) || e.Args.Contains("_T") || e.Args.Contains("${") || e.Args.Contains('"') || e.Args.Contains('\'') || e.Args.Contains('`'))
                    throw new Exception(where + ": \"Args\" must be an argument list of \"$0\" .. \"$9\" references (each at most once) and plain punctuation, without quotes, \"${\" or translation function names");
            }
            var kind = GetString(v, "Kind");
            if (kind != null)
            {
                if (!Enum.TryParse<MapKind>(kind, true, out var k))
                    throw new Exception(where + ": unknown \"Kind\" \"" + kind + "\" (Rename, Property, Report, Ignore)");
                e.Kind = k;
            }
            else if ((e.New == null) && !v.TryGetProperty("Members", out _))
                e.Kind = MapKind.Report;
            if (v.TryGetProperty("Members", out var mem))
            {
                if (!allowMembers)
                    throw new Exception(where + ": nested \"Members\" are not supported");
                if (mem.ValueKind != JsonValueKind.Object)
                    throw new Exception(where + ": \"Members\" must be an object");
                e.Members = new Dictionary<String, MapEntry>(StringComparer.Ordinal);
                foreach (var mp in mem.EnumerateObject())
                    e.Members[mp.Name] = ParseEntry(mp.Name, mp.Value, section, where + "." + mp.Name, false, library);
            }
            Validate(e, where);
            return e;
        }

        /// <summary>
        /// The "$n" references of an <see cref="MapEntry.Args"/> template in order, null if it has a "$" without a digit.
        /// </summary>
        /// <param name="template">The template</param>
        /// <returns>The referenced argument indices</returns>
        public static List<int> ArgRefs(String template)
        {
            var res = new List<int>();
            for (int i = 0; i < template.Length; ++i)
            {
                if (template[i] != '$')
                    continue;
                if ((i + 1 >= template.Length) || !Char.IsAsciiDigit(template[i + 1]))
                    return null;
                res.Add(template[i + 1] - '0');
                ++i;
            }
            return res;
        }

        static bool IsIdentifier(String s) =>
            !String.IsNullOrEmpty(s) && Lexing.JsLexer.IsIdentifierStart(s[0]) && s.All(Lexing.JsLexer.IsIdentifierPart) && (s.IndexOf('\\') < 0);

        static void Validate(MapEntry e, String where)
        {
            if (((e.Kind == MapKind.Rename) || (e.Kind == MapKind.Property)) && (e.Members == null) && String.IsNullOrWhiteSpace(e.New))
                throw new Exception(where + ": a " + e.Kind + " entry needs \"New\"");
            if ((e.New != null) && (e.New.Contains("_T") || e.New.Contains("${") || e.New.Contains('"') || e.New.Contains('\'') || e.New.Contains('`')))
                throw new Exception(where + ": \"New\" must be a plain expression without quotes, \"${\" or translation function names");
        }

        /// <summary>
        /// Test if a name is a legacy global (mapped, not ignored, or unmapped legacy).
        /// </summary>
        public bool IsLegacy(String name)
        {
            if (IsNeverTouch(name))
                return false;
            if (Globals.TryGetValue(name, out var e))
                return e.Kind != MapKind.Ignore;
            return UnmappedLegacy.Contains(name);
        }

        /// <summary>
        /// Test if a name is a shell feature.
        /// </summary>
        public bool IsShell(String name) => Globals.TryGetValue(name, out var e) && e.Shell;

        /// <summary>
        /// Get the library that still provides a legacy name as an alias (null for old common.js / application.js names).
        /// </summary>
        /// <param name="name">The name</param>
        /// <returns>The library file, or null</returns>
        public String LibraryOf(String name) => Globals.TryGetValue(name, out var e) ? e.Library : null;
    }
}
