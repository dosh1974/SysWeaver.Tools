using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwWeb.Bundling;

namespace SwWeb.Tests
{
    /// <summary>
    /// SwWeb bundle refuses to write a .js bundle whose sources don't parse or call a name that no bundle source declares (a half edited
    /// file), names the file and line, and keeps the previous bundle.
    /// </summary>
    [Collection("Console")]
    public class BundleVerifyTests
    {
        const String Manifest = "{ \"Bundles\": [ { \"Out\": \"sw/core.js\", \"Files\": [\"core/a.js\"] }, { \"Out\": \"sw/ui.js\", \"Files\": [\"ui/b.js\", \"ui/c.js\"] } ] }";

        static TestFolder Setup(String b, String c = "/** @file c */\nvar C = 1;\n")
        {
            var f = new TestFolder();
            f.Write("sw/bundles.json", Manifest);
            f.Write("core/a.js", "/** @file a */\nfunction coreHelper(x) { return x; }\nconst { Pick, Other: renamed } = window.Lib || {};\n");
            f.Write("ui/b.js", b);
            f.Write("ui/c.js", c);
            return f;
        }

        static List<BundleResult> Build(TestFolder f) => Bundler.BuildAll(f.Root, BundleManifest.Load(f.PathOf("sw/bundles.json")), "sw/bundles.json", false);

        [Fact]
        public void ValidSourcesAreWritten()
        {
            //  Calls of: another bundle's function, a destructured name, a parameter, a catch binding, an arrow parameter, globals,
            //  members, method shorthands (definitions, not calls), a nested function declared later
            using var f = Setup("/** @file b */\nfunction b(cb) {\n  try { cb(coreHelper(1)); } catch (onErr) { onErr(); }\n  [1].map(fn => fn());\n  Pick(); renamed();\n"
                + "  setTimeout(() => later(), 1); Sw.Net.Send(); new URL(\"x\", location.href); new Uint8Array(2);\n  const o = { save(v) { return v; }, async load() { } };\n  function later() { }\n}\nclass K { run(x) { super.run(x); } }\n");
            var r = Build(f);
            Assert.All(r, x => Assert.Equal(BundleStatus.Written, x.Status));
            Assert.All(r, x => Assert.Empty(x.Errors));
        }

        [Fact]
        public void UndefinedCallFailsWithFileAndLineAndKeepsThePreviousBundle()
        {
            using var f = Setup("/** @file b */\nfunction b() { }\n");
            Assert.All(Build(f), x => Assert.Equal(BundleStatus.Written, x.Status));
            var before = f.Read("sw/ui.js");
            //  The incident: a source half edited, it calls a helper that isn't written yet
            f.Write("ui/b.js", "/** @file b */\nfunction b() {\n  startSave();\n  endSave();\n}\nfunction startSave() { }\n");
            var r = Build(f);
            Assert.Equal(BundleStatus.Written, r[0].Status == BundleStatus.UpToDate ? BundleStatus.Written : r[0].Status);
            Assert.Equal(BundleStatus.Failed, r[1].Status);
            Assert.Contains(r[1].Errors, e => e.StartsWith("ui/b.js(4,3): undefined: \"endSave(...)\"", StringComparison.Ordinal));
            Assert.DoesNotContain(r[1].Errors, e => e.Contains("startSave"));
            Assert.Contains(r[1].Errors, e => e.Contains("previous sw/ui.js stays in place"));
            Assert.Equal(before, f.Read("sw/ui.js"));
            Assert.False(File.Exists(f.PathOf("sw/ui.js.tmp")));
            //  The command fails (exit code 3); --no-verify writes it anyway
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "bundle", f.Root }));
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "bundle", f.Root, "--no-verify" }));
            Assert.Contains("endSave();", f.Read("sw/ui.js"));
        }

        [Fact]
        public void SyntaxErrorsFailPerSource()
        {
            using var f = Setup("/** @file b */\nfunction b() {\n  if (x) {\n    go(1;\n}\n");
            var r = Build(f);
            Assert.Equal(BundleStatus.Failed, r[1].Status);
            Assert.Contains(r[1].Errors, e => e.StartsWith("ui/b.js(", StringComparison.Ordinal) && e.Contains("): syntax: unbalanced brackets"));
            Assert.False(File.Exists(f.PathOf("sw/ui.js")));
            using var g = Setup("/** @file b */\nvar s = `half;\n");
            Assert.Contains(Build(g)[1].Errors, e => e.StartsWith("ui/b.js(2,9): syntax: unterminated", StringComparison.Ordinal));
        }

        [Fact]
        public void MembersAndDocumentedNamesAreKnown()
        {
            Assert.Empty(BundleVerifier.Verify("x.js", "/** @file x */\nfoo.bar(); window.baz();\n", new HashSet<String>()));
            Assert.Single(BundleVerifier.Verify("x.js", "/** @file x */\nbaz(); baz();\n", new HashSet<String>()));
            //  Names of other scripts documented in a comment of the source
            Assert.Empty(BundleVerifier.Verify("x.js", "/** @file x (needs edit/Edit.js: Edit) */\n/* global Ext */\nnew Edit(); Ext();\n", new HashSet<String>()));
        }

        [Fact]
        public void ADeclarationTwiceInOneBlockFails()
        {
            //  The incident of series 4 round 5: two "const why" in one block of ui/cells.js (an early SyntaxError: sw/ui.js did not load)
            var r = BundleVerifier.Verify("x.js", "/** @file x */\nfunction f(a) {\n  if (a) {\n    const why = 1;\n    go(why);\n    const why = 2;\n  }\n}\nfunction go() { }\n", new HashSet<String>());
            Assert.Contains(r, e => e.StartsWith("x.js(6,11): syntax: \"why\" is declared twice in one block (also on line 4)", StringComparison.Ordinal));
            Assert.Single(BundleVerifier.Verify("x.js", "/** @file x */\nlet a = 1, b = 2;\nclass b { }\n", new HashSet<String>()));
            Assert.Single(BundleVerifier.Verify("x.js", "/** @file x */\nconst { p, q: r } = window.o || {};\nlet r = 1;\n", new HashSet<String>()));
            //  The same name in sibling / nested blocks, loops, catch blocks and functions is fine; var may repeat
            Assert.Empty(BundleVerifier.Verify("x.js", "/** @file x */\nfor (let i = 0; i < 2; ++i) { }\nfor (let i = 0; i < 2; ++i) { const x = i; }\nfor (const x of []) { }\n"
                + "if (window.a) { const x = 1; } else { const x = 2; }\nconst x = 3;\nfunction g() { const x = 4; return () => { const x = 5; return x; }; }\nvar v = 1; var v = 2;\n"
                + "try { const e = 1; } catch (e) { const t = e; }\nconst K = class K { };\nconst o = { let: 1, const: 2 };\nconst s = `${(() => { const x = 1; return x; })()}`;\ng(o, s, K, v);\n", new HashSet<String>()).Where(e => e.Contains("declared twice")));
        }

        [Fact]
        public void TheFrameworkSourcesDeclareNothingTwice()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "SysWeaver", "SysWeaver.HttpServer", "srcWeb"));
            if (!Directory.Exists(root))
                return;
            var bad = new List<String>();
            foreach (var dir in new[] { "core", "ui", "shell", "common" })
                foreach (var js in Directory.EnumerateFiles(Path.Combine(root, dir), "*.js"))
                    bad.AddRange(BundleVerifier.Verify(dir + "/" + Path.GetFileName(js), File.ReadAllText(js), new HashSet<String>()).Where(e => e.Contains("declared twice")));
            Assert.Empty(bad);
        }
    }
}
