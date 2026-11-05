using System;
using System.IO;
using System.Text;

namespace SwWeb.Tests
{
    /// <summary>
    /// A temporary folder that is deleted on dispose.
    /// </summary>
    sealed class TestFolder : IDisposable
    {
        public TestFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "SwWebTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public readonly String Root;

        public String PathOf(String rel) => Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));

        public void Write(String rel, String text, bool bom = false)
        {
            var p = PathOf(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, text, new UTF8Encoding(bom));
        }

        public byte[] ReadBytes(String rel) => File.ReadAllBytes(PathOf(rel));

        public String Read(String rel) => File.ReadAllText(PathOf(rel));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch
            {
            }
        }
    }
}
