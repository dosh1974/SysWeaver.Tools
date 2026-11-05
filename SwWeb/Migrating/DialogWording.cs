using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Migrating
{
    /// <summary>
    /// The dialog wording rules of the framework (ui/README.md "Dialog rules") for confirmation calls: a destructive action (Delete, Remove,
    /// Kill ...) is a danger dialog (<c>{ Danger: true }</c>: danger button, Cancel focused, Enter never confirms), buttons are verbs
    /// ("Delete", not "Yes, Delete!") and the way out is "Cancel" (not "No, Keep it!"). Used by "migrate" (old <c>Confirm(...)</c> calls) and
    /// by the check rule design-confirm (<c>Sw.Dialog.Confirm(...)</c> calls).
    /// </summary>
    public static class DialogWording
    {
        static readonly Regex DestructiveText = new Regex(@"^\s*(?:yes\s*[,!.]?\s*)?(?:delete|remove|kill|terminate|destroy|erase|purge|wipe|drop|revoke|uninstall|discard|clear|reset|end)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex YesLabel = new Regex(@"^\s*yes\b\s*[,!.]?\s*(?<v>.*?)[\s!.]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        static readonly Regex NoLabel = new Regex(@"^\s*no\b(?:\s*[,!.].*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        static readonly Regex AreYouSure = new Regex(@"are\s+you\s+sure\s*\?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The names of the translation functions whose first argument is the source text.
        /// </summary>
        static readonly HashSet<String> TrFunctions = new HashSet<String>(StringComparer.Ordinal) { "_T", "_TF", "_TH", "_TFH" };

        /// <summary>
        /// True if a button or title text names a destructive action ("Delete", "Yes, Kill!", "Remove the key?").
        /// </summary>
        /// <param name="s">The text (null = unknown)</param>
        /// <returns>True if destructive</returns>
        public static bool IsDestructive(String s) => (s != null) && DestructiveText.IsMatch(s);

        /// <summary>
        /// True if a text is a "Yes ..." / "No ..." button label.
        /// </summary>
        /// <param name="s">The text</param>
        /// <returns>True for "Yes, Delete!", "No, Keep it!", "Yes", "No!"</returns>
        public static bool IsYesNo(String s) => (s != null) && (YesLabel.IsMatch(s) || NoLabel.IsMatch(s));

        /// <summary>
        /// True if the text asks "Are you sure?" (the title asks the question in the framework's dialogs).
        /// </summary>
        /// <param name="s">The text</param>
        /// <returns>True if found</returns>
        public static bool HasAreYouSure(String s) => (s != null) && AreYouSure.IsMatch(s);

        /// <summary>
        /// The verb label of an OK button text: "Yes, Delete!" becomes "Delete", "Delete!" becomes "Delete"; null if the text has no verb
        /// ("Yes!") or doesn't need a change.
        /// </summary>
        /// <param name="s">The OK button text</param>
        /// <returns>The new label or null</returns>
        public static String OkLabel(String s)
        {
            if (s == null)
                return null;
            var m = YesLabel.Match(s);
            String r;
            if (m.Success)
                r = m.Groups["v"].Value.Trim();
            else
                r = s.TrimEnd().TrimEnd('!').TrimEnd();
            if (r.Length == 0)
                return null;
            r = Char.ToUpperInvariant(r[0]) + r.Substring(1);
            return r == s ? null : r;
        }

        /// <summary>
        /// The label of a Cancel button text: "No, Keep it!" becomes "Cancel", null if no change is needed.
        /// </summary>
        /// <param name="s">The cancel button text</param>
        /// <returns>"Cancel" or null</returns>
        public static String CancelLabel(String s) => (s != null) && NoLabel.IsMatch(s) ? "Cancel" : null;

        /// <summary>
        /// A literal text argument of a call: a plain string ("Delete") or the source text of a translation call (_T("Delete"),
        /// _TF("Delete", "description")).
        /// </summary>
        public readonly struct Literal
        {
            /// <summary>The string token (with its quotes).</summary>
            public readonly JsToken Token;
            /// <summary>The text without the quotes (escape sequences kept as written).</summary>
            public readonly String Value;
            /// <summary>True if the text has no escape sequence (so the token content can be replaced as is).</summary>
            public readonly bool Plain;

            /// <summary>
            /// Create a literal.
            /// </summary>
            public Literal(JsToken token, String value)
            {
                Token = token;
                Value = value;
                Plain = value.IndexOf('\\') < 0;
            }

            /// <summary>
            /// The edit that replaces the text inside the quotes.
            /// </summary>
            /// <param name="text">The new text (no quotes or backslashes)</param>
            /// <param name="note">The report note</param>
            /// <returns>The edit</returns>
            public TextEdit Replace(String text, String note) => new TextEdit
            {
                Start = Token.Start + 1,
                End = Token.Start + Token.Length - 1,
                Text = text,
                OldDisplay = "\"" + Value + "\"",
                NewDisplay = "\"" + text + "\"",
                Note = note,
            };
        }

        /// <summary>
        /// Get the literal text of a call argument (significant token range), if it is one.
        /// </summary>
        /// <param name="text">The source text</param>
        /// <param name="sig">The significant tokens</param>
        /// <param name="first">The first token of the argument</param>
        /// <param name="last">The last token of the argument</param>
        /// <param name="lit">Receives the literal</param>
        /// <returns>True if the argument is a literal text</returns>
        public static bool TryLiteral(String text, IReadOnlyList<JsToken> sig, int first, int last, out Literal lit)
        {
            lit = default;
            if ((first < 0) || (last >= sig.Count) || (first > last))
                return false;
            int at = -1;
            if (first == last)
                at = first;
            else if ((sig[first].Kind == JsTokenKind.Identifier) && TrFunctions.Contains(sig[first].GetText(text)) && (first + 2 <= last)
                && (sig[first + 1].Kind == JsTokenKind.Punctuator) && sig[first + 1].Is(text, "(")
                && (sig[last].Kind == JsTokenKind.Punctuator) && sig[last].Is(text, ")"))
                at = first + 2;
            if ((at < 0) || (sig[at].Kind != JsTokenKind.String))
                return false;
            //  _T("x") / _TF("x", "description"): the string must be the first argument
            if ((at != first) && !(sig[at + 1].Kind == JsTokenKind.Punctuator && (sig[at + 1].Is(text, ",") || sig[at + 1].Is(text, ")"))))
                return false;
            var s = sig[at].GetText(text);
            if (s.Length < 2)
                return false;
            lit = new Literal(sig[at], s.Substring(1, s.Length - 2));
            return true;
        }
    }
}
