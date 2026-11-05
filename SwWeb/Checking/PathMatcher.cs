using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SwWeb.Checking
{
    /// <summary>
    /// Matches relative paths (forward slashes) against a list of simple patterns (case insensitive):
    /// <list type="bullet">
    /// <item>"name/" (ends with a slash): matches any folder with that name (glob), ex: "external/", ".*/".</item>
    /// <item>"name" (no slash): matches the file name (glob), ex: "*.min.js", "marked.js".</item>
    /// <item>"a/b/*.js" (contains a slash): matches the whole relative path (glob, "**" = any number of folders).</item>
    /// </list>
    /// Glob: "*" = any chars except '/', "?" = one char except '/', "**" = anything.
    /// </summary>
    public sealed class PathMatcher
    {
        /// <summary>
        /// Create a matcher.
        /// </summary>
        /// <param name="patterns">The patterns</param>
        public PathMatcher(IEnumerable<String> patterns)
        {
            foreach (var p0 in patterns)
            {
                var p = p0?.Trim().Replace('\\', '/');
                if (String.IsNullOrEmpty(p))
                    continue;
                Patterns.Add(p);
                if (p.EndsWith('/'))
                    Folders.Add(GlobToRegex(p.TrimEnd('/')));
                else if (p.Contains('/'))
                    Paths.Add(GlobToRegex(p.TrimStart('/')));
                else
                    Names.Add(GlobToRegex(p));
            }
        }

        /// <summary>
        /// The patterns.
        /// </summary>
        public readonly List<String> Patterns = new List<String>();

        readonly List<Regex> Folders = new List<Regex>();
        readonly List<Regex> Names = new List<Regex>();
        readonly List<Regex> Paths = new List<Regex>();

        /// <summary>
        /// Convert a glob to an anchored, case insensitive regex.
        /// </summary>
        /// <param name="glob">The glob</param>
        /// <returns>The regex</returns>
        public static Regex GlobToRegex(String glob)
        {
            var sb = new StringBuilder("^");
            for (int i = 0; i < glob.Length; ++i)
            {
                var c = glob[i];
                if (c == '*')
                {
                    if ((i + 1 < glob.Length) && (glob[i + 1] == '*'))
                    {
                        ++i;
                        // "**/" also matches no folder
                        if ((i + 1 < glob.Length) && (glob[i + 1] == '/'))
                        {
                            ++i;
                            sb.Append("(?:.*/)?");
                        }
                        else
                            sb.Append(".*");
                    }
                    else
                        sb.Append("[^/]*");
                    continue;
                }
                if (c == '?')
                {
                    sb.Append("[^/]");
                    continue;
                }
                sb.Append(Regex.Escape(c.ToString()));
            }
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Test if a folder (relative path) is matched (by a folder pattern on its own name or a path pattern).
        /// </summary>
        /// <param name="relFolder">The relative folder path</param>
        /// <returns>True if matched</returns>
        public bool IsFolderMatch(String relFolder)
        {
            relFolder = relFolder.Replace('\\', '/').Trim('/');
            var i = relFolder.LastIndexOf('/');
            var name = relFolder.Substring(i + 1);
            foreach (var r in Folders)
                if (r.IsMatch(name))
                    return true;
            foreach (var r in Paths)
                if (r.IsMatch(relFolder) || r.IsMatch(relFolder + "/"))
                    return true;
            return false;
        }

        /// <summary>
        /// Test if a file (relative path) is matched (by any folder in its path, its name or its path).
        /// </summary>
        /// <param name="relFile">The relative file path</param>
        /// <returns>True if matched</returns>
        public bool IsFileMatch(String relFile)
        {
            relFile = relFile.Replace('\\', '/').Trim('/');
            var parts = relFile.Split('/');
            for (int i = 0; i < parts.Length - 1; ++i)
                foreach (var r in Folders)
                    if (r.IsMatch(parts[i]))
                        return true;
            var name = parts[parts.Length - 1];
            foreach (var r in Names)
                if (r.IsMatch(name))
                    return true;
            foreach (var r in Paths)
                if (r.IsMatch(relFile))
                    return true;
            return false;
        }
    }
}
