using System;
using System.IO;
using System.Linq;
using SwWeb.Checking;
using Xunit.Abstractions;

namespace SwWeb.Tests
{
    /// <summary>
    /// Contract tests from every api row of the framework READMEs (series 4 round 5, Architect guard rail 1: in three rounds a fix changed a
    /// documented function's result and no test saw it). The static part runs here (the documented name is defined, its documented options
    /// are read, a JSDoc @returns agrees); the browser part is the generated sw/readme_contracts.js that core/contracttest.html runs.
    /// </summary>
    public class ReadmeContractTests
    {
        readonly ITestOutputHelper Out;

        public ReadmeContractTests(ITestOutputHelper output)
        {
            Out = output;
        }

        const String Readme = "# x\n\n## API reference\n\n### Sw.Net (net.js)\n\n| Member | Description |\n|---|---|\n"
            + "| `Send(url, arg, opts)` | Api call. Options: `Reload`, `Post` (force POST / allow `Get`), `Signal`. Returns the json. |\n"
            + "| `FailText(name, error, o)` → string, `Compose(what, why)` → **string** | The composers. |\n"
            + "| `Online`, `IsWrite(opts, kind)` | State. |\n\n### Button\n`new Sw.Button({ Text, Icon, Title, OnClick(button, event) })`: a button.\n\n"
            + "## Storage keys\n\n| `Not.Api(x)` | not in a reference part |\n";

        [Fact]
        public void RowsBecomeContracts()
        {
            var c = ReadmeContracts.Parse(Readme, "core/README.md").ToDictionary(x => x.Name);
            Assert.Equal(new[] { "Reload", "Post", "Signal" }, c["Sw.Net.Send"].Options);
            Assert.Equal(new[] { "url", "arg", "opts" }, c["Sw.Net.Send"].Params);
            Assert.Equal("string", c["Sw.Net.FailText"].Returns);
            Assert.Equal("string", c["Sw.Net.Compose"].Returns);
            Assert.Equal("member", c["Sw.Net.Online"].Kind);
            Assert.Equal("function", c["Sw.Net.IsWrite"].Kind);
            Assert.Equal("constructor", c["Sw.Button"].Kind);
            Assert.Equal(new[] { "Text", "Icon", "Title", "OnClick" }, c["Sw.Button"].Options);
            Assert.Equal("core/README.md:9", c["Sw.Net.Send"].Where);
            Assert.False(c.ContainsKey("Not.Api"));
        }

        [Fact]
        public void StaticChecksFindDrift()
        {
            var contracts = ReadmeContracts.Parse(Readme, "core/README.md");
            var src = new ReadmeContracts.Sources();
            //  FailText returns an object per its JSDoc (the round 5 bug: a string was documented), Signal is never read, Compose is missing
            src.Add("/** @file net */\nconst Net = {\n  /** @returns {*} json */\n  Send(url, arg, opts) { return opts.Reload || opts.Post; },\n"
                + "  /** @param {string} name x\n   * @returns {{Text: string}} parts */\n  FailText(name, error, o) { return { Text: name }; },\n  Online: true,\n  IsWrite(opts, kind) { return true; },\n};\n"
                + "class Button { constructor(o) { this.o = [o.Text, o.Icon, o.Title, o.OnClick]; } }\n");
            var p = ReadmeContracts.CheckStatic(contracts, src);
            Out.WriteLine(String.Join("\n", p));
            Assert.Contains(p, x => x.Contains("Sw.Net.Compose is documented, but no framework source defines \"Compose\""));
            Assert.Contains(p, x => x.Contains("Sw.Net.Send documents the option \"Signal\", but no framework source reads it"));
            Assert.Contains(p, x => x.Contains("Sw.Net.FailText is documented to return string, but its JSDoc says @returns {{Text: string}}"));
            Assert.Equal(3, p.Count);
        }

        [Fact]
        public void ManifestRunsInTheBrowser()
        {
            var m = ReadmeContracts.Manifest(ReadmeContracts.Parse(Readme, "core/README.md"));
            Assert.Contains("{ Name: \"Sw.Net.FailText\", Kind: \"function\", Where: \"core/README.md:10\", Returns: \"string\", Pure: true },", m);
            Assert.Contains("window.SwReadmeContracts = { Rows: rows, Run: run };", m);
            //  The generated script lexes
            var toks = SwWeb.Lexing.JsLexer.Tokenize(m);
            Assert.DoesNotContain(toks, t => t.Unterminated);
        }

        /// <summary>The SysWeaver repository: every api row of core / ui / shell / explore README.md has a definition, read options and an agreeing JSDoc.</summary>
        [Fact]
        public void TheRepositoryReadmesMatchTheCode()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "SysWeaver"));
            if (!Directory.Exists(root))
                return;
            var (contracts, problems) = ReadmeContracts.Run(root);
            Out.WriteLine(contracts.Count + " documented apis");
            Assert.True(contracts.Count > 400, contracts.Count + " documented apis");
            Assert.Empty(problems);
        }
    }
}
