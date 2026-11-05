using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    /// <summary>
    /// Rule minifier-reorder: the cases were run through the build's minify.exe (tdewolff minify v2.24.19, Compress flags); the
    /// "bad" ones come out with the destructuring in front of the name its default / computed key reads.
    /// </summary>
    [Collection("Console")]
    public class MinifierReorderTests
    {
        const String Header = "/** @file test */\r\n";

        static String[] Ids(String code, String rel = "x.js")
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext(rel, new SourceText(Header + code), new CheckOptions(), f));
            return f.Select(x => x.Rule.Id).ToArray();
        }

        [Theory]
        // const / let / var, object and array patterns, one or two statements
        [InlineData("function f(t,e,i={}){const s=t.length,{start:n=0,end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){let s=t.length,{end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){var s=t.length,{end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length,[o=s-1]=i;return o}")]
        [InlineData("function f(t,i){const s=t.length;const {end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){\r\n    const s = t.length;\r\n    const { end: o = s - 1 } = i;\r\n    return o;\r\n}")]
        [InlineData("function f(t,i){let s=t.length;let {end:o=s-1}=i;return o}")]
        // computed key, nested default, template, rest, await, call, closure called at once
        [InlineData("function f(t,i){const s=t.length,{[s]:o}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length,{end:{x:o=s}={}}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length,{end:o=`${s}`}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length,{end:o=s,...r}=i;return r}")]
        [InlineData("async function f(t,i){const s=await t,{end:o=s}=i;return o}")]
        [InlineData("function f(t,i){const s=g(t),{end:o=s}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length,{end:o=(()=>s)()}=i;return o}")]
        // the default reads the second plain declarator; the moved one is the second pattern
        [InlineData("function f(t,i){const s=t.length,u=s+1,{end:o=u}=i;return o}")]
        [InlineData("function h(){const a=f(),{c}=k(a),{e=a}=n();return a+c+e}")]
        // top level, block, arrow, method, loop body, for head
        [InlineData("const s=t.length,{end:o=s-1}=i;console.log(o);")]
        [InlineData("{const s=t.length,{end:o=s-1}=i;console.log(o)}")]
        [InlineData("const f=(t,i)=>{const s=t.length,{end:o=s-1}=i;return o};")]
        [InlineData("class C{m(t,i){const s=t.length,{end:o=s-1}=i;return o}}")]
        [InlineData("function f(t,i){for(const x of t){const s=x.length,{end:o=s-1}=i;g(o)}}")]
        [InlineData("function h(){for(let i=0,{l=i}=o;;);}")]
        // no semicolons (ASI) between consecutive declarations
        [InlineData("function f(t,i){\r\n const s = t.length\r\n const { end: o = s - 1 } = i\r\n return o\r\n}")]
        public void Reported(String code)
        {
            Assert.Equal(new[] { "minifier-reorder" }, Ids(code));
        }

        [Theory]
        // the pattern's initializer reads the earlier name: not moved
        [InlineData("function f(t,i){const s=t.length,{end:o}=i[s];return o}")]
        [InlineData("function f(t,i){const s=t.length,{end:o=s-1}=s&&i;return o}")]
        [InlineData("function h(){const a=f(),{c=a}=a;return c}")]
        // no default / key reads an earlier declarator
        [InlineData("function f(t,i){const s=t.length,o=i[s];return o}")]
        [InlineData("function f(t,i){const s=t.length,{end:o}=i,x=o+s;return x}")]
        [InlineData("function f(t,i){const s=t.length,{end:o=i.x}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length,{end:o=1}=i;return o+s}")]
        [InlineData("function f(t,i){const s=t.length,{end:o=x.s}=i;return o}")]
        // the first declarator is a pattern: nothing moves
        [InlineData("function f(t,i){const {a:s}=t,{end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){const [s]=t,{end:o=s}=i;return o}")]
        // the first moved pattern is fine, the later one stays after the name
        [InlineData("function h(){const a=f(),{c}=k(),{e=a}=n();return a+c+e}")]
        // a parameter default (not a declaration)
        [InlineData("function f(t,i){const s=t.length;return (({end:o=s-1}=i)=>o)()}")]
        // the fixes: a statement or a different kind in between, the default computed after the destructuring
        [InlineData("function f(t,i){const s=t.length;g();const {end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length;if(!i)return 0;const {end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length;let {end:o=s-1}=i;return o}")]
        [InlineData("function f(t,i){const s=t.length;const {end:e}=i;const o=e===undefined?s-1:e;return o}")]
        // the earlier name is shadowed by an object key or a property
        [InlineData("function f(t,i){const s=t.length,{end:o={s:1}}=i;return o}")]
        // marked
        [InlineData("function f(t,i){const s=t.length,\r\n// SwWeb:allow minifier-reorder (the default is only called later)\r\n{end:o=()=>s}=i;return o}")]
        public void NotReported(String code)
        {
            Assert.DoesNotContain("minifier-reorder", Ids(code));
        }

        [Fact]
        public void InlineScript()
        {
            var f = new List<Finding>();
            var html = "<!doctype html><html><head><script>function f(t,i){const s=t.length,{end:o=s-1}=i;return o}</script></head><body></body></html>";
            Checker.CheckHtmlFile(new Checker.FileContext("x.html", new SourceText(html), new CheckOptions(), f));
            Assert.Contains("minifier-reorder", f.Select(x => x.Rule.Id));
        }
    }
}
