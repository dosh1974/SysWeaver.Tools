using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SwWeb.Checking;

namespace SwWeb.Tests
{
    /// <summary>
    /// "SwWeb verify-build": the checks of the BUILT (minified, compressed) web outputs (AS7-2). The "bad" js strings are the real outputs
    /// of the build's minify.exe (tdewolff minify v2.24.19) for the source cases of <see cref="MinifierReorderTests"/>.
    /// </summary>
    [Collection("Console")]
    public class BuildVerifierTests
    {
        static String ToolsRoot([CallerFilePath] String file = null) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), ".."));

        static String RepoRoot() => Path.GetFullPath(Path.Combine(ToolsRoot(), "..", "SysWeaver"));

        static String[] Js(String code) => MinifierChecker.CheckOutput(code).Select(x => x.Name).ToArray();

        static List<BuildVerifier.Problem> Verify(String ext, String text, bool sourceHasContent = true)
        {
            var res = new List<BuildVerifier.Problem>();
            BuildVerifier.VerifyText("x" + ext + ".br", ext, text, res, () => sourceHasContent);
            return res;
        }

        static void WriteBr(String file, String text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using var fs = File.Create(file);
            using var bs = new BrotliStream(fs, CompressionLevel.Fastest);
            var b = Encoding.UTF8.GetBytes(text);
            bs.Write(b, 0, b.Length);
        }

        [Theory]
        // minify.exe output of: const s=t.length,{start:n=0,end:o=s-1}=i  /  var  /  computed key  /  two statements  /  an arrow called at once  /  for head
        [InlineData("function f(e,t,n={}){const{start:i=0,end:o=s-1}=n,s=e.length;return o}", "s")]
        [InlineData("function f(e,t){var{end:s=n-1}=t,n=e.length;return s}", "n")]
        [InlineData("function f(e,t){const{[n]:s}=t,n=e.length;return s}", "n")]
        [InlineData("function f(e,t){const{end:s=n-1}=t,n=e.length;return s}", "n")]
        [InlineData("function f(e,t){const{end:s=(()=>n)()}=t,n=e.length;return s}", "n")]
        [InlineData("function h(){for(let{l:t=e}=o,e=0;;);}", "e")]
        // plain initializers of let / const, inside a top level function called at once (the shape of every built file)
        [InlineData("(function(){\"use strict\";const a=b+1,b=2;return a})()", "b")]
        [InlineData("(()=>{let a=g(b),b=2})()", "b")]
        [InlineData("const a=(function(){return b})(),b=1", "b")]
        public void Reported(String code, String name)
        {
            Assert.Equal(new[] { name }, Js(code));
        }

        [Theory]
        // reads in functions that run later (arrow, function, method, class, getter)
        [InlineData("function f(e,t){const{end:s=1}=t,n=e=>o+e,o=2;return n(s)}")]
        [InlineData("function f(e,t){const{end:s=()=>n}=t,n=e.length;return s}")]
        [InlineData("const a=function(){return b},b=1")]
        [InlineData("const a={m(){return b},get g(){return b}},b=1")]
        [InlineData("const a=class{m(){return b}},b=1")]
        [InlineData("const a=async x=>{await b},b=1")]
        [InlineData("const a=[1].map(b=>b+1),b=2")]
        // a function called at once that binds the name itself
        [InlineData("(function(){\"use strict\";const e=(()=>{const e=1;return e})(),n=e;return n})()")]
        [InlineData("const s=(()=>{const n=1;return n})(),n=2")]
        [InlineData("const s=(function(n){return n})(1),n=2")]
        // var declared again (a parameter or an earlier declarator: legal, vendored code does it)
        [InlineData("function f(r){var a=r.x,r=a}")]
        [InlineData("for(var i=this.nodes,r=Array.isArray(i),n=0,i=r?i:i[Symbol.iterator]();;)break")]
        // correct order, property names, object keys
        [InlineData("const s=e.length,{end:o=s-1}=t")]
        [InlineData("const a=x.b,b=1")]
        [InlineData("const a={b:1},b=2")]
        [InlineData("const a=1;const b=a")]
        public void NotReported(String code)
        {
            Assert.Empty(Js(code));
        }

        [Fact]
        public void TheOwnersChartCrashIsFound()
        {
            //  OWN3-Chart-1: the vendored chart.js through the build's minify.exe threw "Cannot access 'o' before initialization at
            //  chart.js:11:137211"; the external folder is now built with -OptimizeWebFiles false. The check finds that exact place.
            var minify = Path.Combine(ToolsRoot(), "Compress", "tools", "minify.exe");
            var src = Path.Combine(RepoRoot(), "SysWeaver.MicroService.ChartJs", "srcWeb", "chart", "external", "chart.js");
            if (!File.Exists(minify) || !File.Exists(src) || !OperatingSystem.IsWindows())
                return;
            using var f = new TestFolder();
            var outFile = f.PathOf("chart.js");
            var psi = new ProcessStartInfo(minify) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (var a in new[] { "--js-precision", "0", "-o", outFile, src })
                psi.ArgumentList.Add(a);
            using (var p = Process.Start(psi))
            {
                p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }
            if (!File.Exists(outFile))
                return;
            var text = File.ReadAllText(outFile);
            var found = MinifierChecker.CheckOutput(text);
            Assert.Contains(found, x => (x.Name == "o") && (x.Kind == "const") && (new Lexing.SourceText(text).GetLineCol(x.Offset) == (11, 137211)));
        }

        [Fact]
        public void GlslDuplicateDefinitions()
        {
            // shader_minifier 1.6.0 renamed randomLine(float) to rand's name: two bodies with one signature
            var p = Verify(".glsl", "uniform float time;\nfloat t(vec2 a){return a.x;}float t(highp vec2 b){return b.y;}void main(){gl_FragColor=vec4(t(vec2(time)));}");
            Assert.Equal(new[] { "build-glsl-duplicate" }, p.Select(x => x.Rule));
            Assert.Contains("t(vec2)", p[0].Message);
            // overloads with other parameter types, prototypes, structs and exclusive preprocessor branches are fine
            Assert.Empty(Verify(".glsl", "float t(float a);float t(vec2 a){return a.x;}float t(float a){return a;}struct S{float x;};void main(){}"));
            Assert.Empty(Verify(".glsl", "#ifdef VERTEX\nvoid main(){gl_Position=vec4(0);}\n#endif\n#ifdef FRAGMENT\nvoid main(){gl_FragColor=vec4(1);}\n#endif\n"));
            Assert.Empty(Verify(".glsl", "#if A\nfloat f(float x){return x;}\n#else\nfloat f(float x){return -x;}\n#endif\nvoid main(){}"));
            Assert.Equal(new[] { "build-glsl-duplicate" }, Verify(".glsl", "float f(float x){return x;}\n#ifdef A\nfloat f(in float y){return y;}\n#endif\nvoid main(){}").Select(x => x.Rule));
            // comments are not code
            Assert.Empty(Verify(".glsl", "// float f(float x){\n/* float f(float x){} */float f(float x){return x;}void main(){}"));
        }

        [Fact]
        public void BracketsAndEmptyOutputs()
        {
            Assert.Equal(new[] { "build-glsl-brackets" }, Verify(".glsl", "void main(){gl_FragColor=vec4(1);").Select(x => x.Rule));
            Assert.Equal(new[] { "build-js-syntax" }, Verify(".js", "(function(){const a=[1,2);})()").Select(x => x.Rule));
            Assert.Equal(new[] { "build-js-syntax" }, Verify(".js", "const a=\"x").Select(x => x.Rule));
            Assert.Equal(new[] { "build-js-syntax" }, Verify(".js", "const a=`${b`").Select(x => x.Rule));
            Assert.Empty(Verify(".js", "const a=`${b}c${d}`,e=/[(]/;"));
            // an empty output: TwinklingTunnel.glsl shipped empty since 2026-10-14
            Assert.Equal(new[] { "build-empty" }, Verify(".glsl", "").Select(x => x.Rule));
            Assert.Empty(Verify(".css", "  ", false));
            Assert.False(BuildVerifier.HasContent("﻿/* only a header comment */\r\n", ".css"));
            Assert.False(BuildVerifier.HasContent("﻿", ".html"));
            Assert.True(BuildVerifier.HasContent("// x\r\nconst a = 1;", ".js"));
            // a html output: its scripts are checked
            Assert.Equal(new[] { "build-js-tdz" }, Verify(".html", "<p>x</p><script>const{a:b=c}=d,c=1</script>").Select(x => x.Rule));
        }

        [Fact]
        public void CommandFindsOutputsAndTheirSources()
        {
            using var f = new TestFolder();
            f.Write("Proj/srcWeb/app/a.js", "const s = t.length;\nconst { end: o = s - 1 } = i;\n");
            WriteBr(f.PathOf("Proj/web/app/a.js.br"), "const{end:o=s-1}=i,s=t.length");
            f.Write("Proj/srcWeb/app/ok.js", "const a = 1;\n");
            WriteBr(f.PathOf("Proj/web/app/ok.js.br"), "const a=1");
            f.Write("Proj/srcWeb/app/c.css", "/* comment only */\n");
            WriteBr(f.PathOf("Proj/web/app/c.css.br"), "");
            f.Write("Proj/srcData/m.html", "﻿");
            WriteBr(f.PathOf("Proj/data/m.html.br"), "");
            f.Write("Proj/srcWeb/fx/e.glsl", "void main(){}\n");
            WriteBr(f.PathOf("Proj/web/fx/e.glsl.br"), "");
            // build outputs only: never the srcWeb / bin / obj folders
            WriteBr(f.PathOf("Proj/srcWeb/app/z.js.br"), "const{a:b=c}=d,c=1");
            WriteBr(f.PathOf("Proj/bin/Debug/z.js.br"), "const{a:b=c}=d,c=1");
            var outputs = BuildVerifier.FindOutputs(f.Root).Select(x => Path.GetRelativePath(f.Root, x).Replace('\\', '/')).ToArray();
            Assert.Equal(new[] { "Proj/data/m.html.br", "Proj/web/app/a.js.br", "Proj/web/app/c.css.br", "Proj/web/app/ok.js.br", "Proj/web/fx/e.glsl.br" }, outputs);
            Assert.Equal(f.PathOf("Proj/srcData/m.html"), BuildVerifier.FindSource(f.PathOf("Proj/data/m.html.br")), ignoreCase: true);
            var res = BuildVerifier.Run(f.Root);
            Assert.Equal(new[] { "a.js.br:build-js-tdz", "e.glsl.br:build-empty" }, res.Problems.Select(x => Path.GetFileName(x.File) + ":" + x.Rule).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "verify-build", f.Root, "--quiet" }));
            File.Delete(f.PathOf("Proj/web/app/a.js.br"));
            File.Delete(f.PathOf("Proj/web/fx/e.glsl.br"));
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "verify-build", f.Root }));
        }

        [Fact]
        public void TheSysWeaverBuildOutputsVerify()
        {
            //  The gate on what ships: every web/**/*.br the SysWeaver projects embed (as of their last build) passes verify-build.
            //  Run "SwWeb verify-build <repository>" after a build for the full list.
            var root = RepoRoot();
            if (!Directory.Exists(root))
                return;
            var res = BuildVerifier.Run(root);
            Assert.Empty(res.Problems.Select(x => x.ToString(root)));
        }
    }
}
