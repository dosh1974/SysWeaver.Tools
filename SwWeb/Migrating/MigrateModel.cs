using System;
using System.Collections.Generic;

namespace SwWeb.Migrating
{
    /// <summary>
    /// What "migrate" rewrites.
    /// </summary>
    public enum MigrateLevel
    {
        /// <summary>
        /// Only the HTML include tags (pages that don't use legacy globals any more).
        /// </summary>
        Includes,
        /// <summary>
        /// Include tags plus the token level legacy global renames in JavaScript, css hooks and design token alias renames (the default).
        /// </summary>
        Api,
        /// <summary>
        /// <see cref="Api"/> plus the Design v2 porting report: every finding of the design rules of "check --design" (page header,
        /// shadows, gradients, hard-coded colours, legacy theme variables, old class hooks, inline styles, accessible custom controls) as a
        /// manual follow-up warning (the token alias uses are renamed, not reported).
        /// </summary>
        Design,
    }

    /// <summary>
    /// The kind of a migrate report item.
    /// </summary>
    public enum MigrateItemKind
    {
        /// <summary>
        /// A change that was (or would be, with --dry-run) written.
        /// </summary>
        Change,
        /// <summary>
        /// Information (no action needed).
        /// </summary>
        Info,
        /// <summary>
        /// Needs manual follow-up.
        /// </summary>
        Warning,
    }

    /// <summary>
    /// A text replacement (offsets into the decoded text).
    /// </summary>
    public sealed class TextEdit
    {
        /// <summary>
        /// Start offset.
        /// </summary>
        public int Start;
        /// <summary>
        /// End offset (exclusive).
        /// </summary>
        public int End;
        /// <summary>
        /// The new text.
        /// </summary>
        public String Text;
        /// <summary>
        /// The offset used for the report line (default: Start).
        /// </summary>
        public int ReportAt = -1;
        /// <summary>
        /// Report text for the old part (default: the replaced text).
        /// </summary>
        public String OldDisplay;
        /// <summary>
        /// Report text for the new part (default: <see cref="Text"/>).
        /// </summary>
        public String NewDisplay;
        /// <summary>
        /// An optional note shown in the report.
        /// </summary>
        public String Note;
        /// <summary>
        /// True for helper edits that are not listed in the report (part of another change).
        /// </summary>
        public bool Silent;
    }

    /// <summary>
    /// An item of the migrate report.
    /// </summary>
    public sealed class MigrateItem
    {
        /// <summary>
        /// The relative path (forward slashes).
        /// </summary>
        public String Path;
        /// <summary>
        /// 1-based line (0 = file level).
        /// </summary>
        public int Line;
        /// <summary>
        /// 1-based column (0 = file level).
        /// </summary>
        public int Col;
        /// <summary>
        /// The kind.
        /// </summary>
        public MigrateItemKind Kind;
        /// <summary>
        /// The message ("old → new" for changes).
        /// </summary>
        public String Message;

        /// <summary>
        /// Format as "path(line,col): kind: message".
        /// </summary>
        public String Format() =>
            Path + (Line > 0 ? ("(" + Line + "," + Col + ")") : "") + ": " + Kind.ToString().ToLowerInvariant() + ": " + Message;

        /// <summary>
        /// A debug string.
        /// </summary>
        public override string ToString() => Format();
    }

    /// <summary>
    /// A finding of an analysis, located by offset (converted to line / column by the caller).
    /// </summary>
    public sealed class OffsetItem
    {
        /// <summary>
        /// The offset in the decoded text.
        /// </summary>
        public int Offset;
        /// <summary>
        /// The kind (Info or Warning).
        /// </summary>
        public MigrateItemKind Kind;
        /// <summary>
        /// The message.
        /// </summary>
        public String Message;
    }

    /// <summary>
    /// Options for <see cref="Migrator"/>.
    /// </summary>
    public sealed class MigrateOptions
    {
        /// <summary>
        /// Write nothing.
        /// </summary>
        public bool DryRun;
        /// <summary>
        /// Copy the originals (mirrored relative paths) to this folder before writing (null = no backup).
        /// </summary>
        public String BackupDir;
        /// <summary>
        /// Only these paths (PathMatcher patterns, empty = all).
        /// </summary>
        public List<String> Include = new List<String>();
        /// <summary>
        /// Skip these paths too (PathMatcher patterns).
        /// </summary>
        public List<String> Exclude = new List<String>();
        /// <summary>
        /// The level.
        /// </summary>
        public MigrateLevel Level = MigrateLevel.Api;
        /// <summary>
        /// Keep pages that still use legacy globals (after the renames) on the legacy bundles common.js / application.js / application.css
        /// (the old safety gate); default: migrate them to the new format plus the compatibility bundle sw/compat.js / sw/compat.css.
        /// </summary>
        public bool KeepLegacy;
        /// <summary>
        /// Rewrite the includes of pages that still use legacy globals without adding sw/compat.js (the page breaks until they are rewritten).
        /// </summary>
        public bool ForceIncludes;
        /// <summary>
        /// Add a JSDoc file header (contract rule jsdoc-header) to .js files that have none (--no-jsdoc-header disables it).
        /// </summary>
        public bool JsDocHeaders = true;
        /// <summary>
        /// Fix newline escapes in translated literals (contract rule tr-newline); "upgrade" leaves them alone (not a change of the front end).
        /// </summary>
        public bool FixTranslations = true;
        /// <summary>
        /// "upgrade": a page that didn't link any SysWeaver style sheet (no common/theme.css, app/application.css) draws its own UI, so it
        /// gets the new scripts only: no common/theme.css, sw/ui.css or sw/compat.css is added (they would restyle it).
        /// </summary>
        public bool KeepStyles;
        /// <summary>
        /// Alias tokens (map "Tokens" Safe entries) that a file of the run defines (an override such as <c>--sw-touch: 44px</c>): their
        /// uses are not renamed. Set by <see cref="Migrator.MigrateFolder"/>.
        /// </summary>
        public HashSet<String> DefinedTokens = new HashSet<String>(StringComparer.Ordinal);
        /// <summary>
        /// The mapping table.
        /// </summary>
        public MigrationMap Map;
    }
}
