using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwWeb.Bundling
{
    /// <summary>
    /// One bundle in the bundle manifest (<c>sw/bundles.json</c>).
    /// </summary>
    public sealed class BundleDefinition
    {
        /// <summary>
        /// The output file, relative to the web root, ex: "sw/core.js". Must end with ".js" or ".css".
        /// </summary>
        public String Out { get; set; }

        /// <summary>
        /// The source files in concatenation order, relative to the web root. Must have the same extension as <see cref="Out"/>.
        /// </summary>
        public List<String> Files { get; set; } = new List<String>();

        /// <summary>
        /// Optional text added to the generated header comment (may contain new lines).
        /// </summary>
        public String Header { get; set; }

        /// <summary>
        /// JavaScript only: wrap every source in a load once guard, so a source that more than one bundle of a page contains runs once
        /// (ex: sw/session.js followed by sw/core.js runs the shared core files once: one BroadcastChannel, one session manager).
        /// The guard is <c>if (!((window.Sw = window.Sw || {}).Sources = window.Sw.Sources || {})["core/x.js"]) { window.Sw.Sources["core/x.js"] = "sw/out.js"; ... }</c>:
        /// <c>Sw.Sources</c> maps each source that ran to the bundle that ran it. A source of such a bundle must not declare let, const or class
        /// at its top level (they would become local to the guard block), so it must be an IIFE (checked). A source whose only top level
        /// declarations are functions (core/i18n.js, the global translation markers) is not wrapped: it guards the rest of its code itself.
        /// </summary>
        public bool LoadOnce { get; set; }

        /// <summary>
        /// Optional size budget of the output in KB (1024 bytes, as written, UTF-8 with BOM): a bundle that grows over it fails
        /// "SwWeb bundle" and "SwWeb bundle --check" (it is still written), so a small bundle (sw/session.js) can't grow back into the full
        /// core unnoticed. Raise it in the manifest only when the growth is intended.
        /// </summary>
        public double? MaxKB { get; set; }

        /// <summary>
        /// Optional size budget of the gzip compressed output in KB (gzip at the smallest size, about gzip -9; what a page downloads),
        /// checked as <see cref="MaxKB"/>.
        /// </summary>
        public double? MaxGzipKB { get; set; }
    }

    /// <summary>
    /// The bundle manifest (<c>sw/bundles.json</c>).
    /// </summary>
    public sealed class BundleManifest
    {
        /// <summary>
        /// The bundles, built in this order.
        /// </summary>
        public List<BundleDefinition> Bundles { get; set; } = new List<BundleDefinition>();

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = false,
        };

        /// <summary>
        /// Parse a manifest from json text.
        /// </summary>
        /// <param name="json">The json text</param>
        /// <returns>The manifest</returns>
        public static BundleManifest Parse(String json)
        {
            var m = JsonSerializer.Deserialize<BundleManifest>(json, Options) ?? throw new FormatException("The bundle manifest is empty");
            m.Bundles ??= new List<BundleDefinition>();
            foreach (var b in m.Bundles)
                if (b != null)
                    b.Files ??= new List<String>();
            return m;
        }

        /// <summary>
        /// Load a manifest file.
        /// </summary>
        /// <param name="fileName">The file name</param>
        /// <returns>The manifest</returns>
        public static BundleManifest Load(String fileName) => Parse(File.ReadAllText(fileName));
    }
}
