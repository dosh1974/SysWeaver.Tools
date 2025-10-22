using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace SysWeaver
{
    public static class ImageCompareGallery
    {

        /// <summary>
        /// Creates an html files that can be used to compare images
        /// </summary>
        /// <param name="dest">Name of the output html file</param>
        /// <param name="imagePairs">Each pair contains an original and modified filename</param>
        public static void Create(String dest, IEnumerable<Tuple<String, String>> imagePairs)
        {
            dest = new FileInfo(dest).FullName;
            var dp = Path.GetDirectoryName(dest) + Path.DirectorySeparatorChar;
            List<String> allLines = new List<string>();
            foreach (var x in imagePairs)
                allLines.Add(BuildLine(x.Item1, x.Item2, dp));

            var page = Page.Trim().Replace("{0}", String.Join('\n', allLines));
            var destPath = Path.GetDirectoryName(dest);
            if (!Directory.Exists(destPath))
                Directory.CreateDirectory(destPath);
            File.WriteAllText(dest, page);
        }

        static String BuildLine(String src, String dest, String removeFront)
        {
            var sf = new FileInfo(src);
            var df = new FileInfo(dest);
            src = sf.FullName;
            dest = df.FullName;
            var sl = sf.Length;
            var dl = df.Length;
            var rl = removeFront.Length;
            if (src.StartsWith(removeFront))
                src = src.Substring(rl);
            if (dest.StartsWith(removeFront))
                dest = dest.Substring(rl);

            var ratio = 100M * (Decimal)dl / (Decimal)sl;
            var text = String.Concat(
                "File: ", Path.GetFileName(src).ToQuoted(), "\n",
                "Source size: ", sl, " bytes\n",
                "Dest size: ", dl, " bytes\n",
                "Ratio: ", ratio.ToString("0.00").Replace(',', '.') + " %");
            return String.Format(CmpFormat.Trim(),
                HttpUtility.HtmlAttributeEncode(src),
                HttpUtility.HtmlAttributeEncode(dest),
                HttpUtility.HtmlAttributeEncode(text),
                HttpUtility.HtmlEncode(String.Join(" - ", text.Split('\n').Select(x => x.Trim())))
                );
        }

        #region Template

        const String Page = @"
<!doctype html>
<html>
    <head>
        <style>
html, body
{
	margin: 0;
	padding: 0;
}

imgcmp-control
{
	user-select: none;
	display: block;
	width: 100%;
	padding: 0 2em 0 2em;
	font-size: 200%;
	position: fixed;
    top: 0;
    left: 0;
    z-index: 1;
	background-color: #ccc;
	box-shadow: 0 5px 10px rgba(0, 0, 0, 0.5);
	text-transform: uppercase;
	font-weight: bold;
	line-height: 2em;
	vertical-align: middle;
}

	imgcmp-control input
	{
		vertical-align: middle;
		width: 2em;
		height: 2em;
		cursor: pointer;
	}
	
	imgcmp-control label
	{
		vertical-align: middle;
		cursor: pointer;
	}

imgcmp-data
{
	display: block;
	width: 100%;
	padding-top: 2em;
	text-align: center;
}

imgcmp-pair
{
	background-color: #ddd;
	display: inline-block;
	margin: 4em 1em 1em 1em;
	box-shadow: 2px 5px 10px rgba(0, 0, 0, 0.5);
}

imgcmp-images
{
	display: block;
	width: 100%;
	text-align: center;
}

imgcmp-data img:nth-child(1)
{
	display: none;
}

imgcmp-data img:nth-child(2)
{
	display: inline-block;
}

imgcmp-data.Original img:nth-child(1)
{
	display: inline-block;
}

imgcmp-data.Original img:nth-child(2)
{
	display: none;
}


imgcmp-stats
{
	display: block;
	width: 100%;
	text-align: center;
	padding: 4px 8px 4px 8px;
	box-sizing: border-box;
}


img
{
	max-width: min(80vw, 70vh);
	max-height: min(80vw, 70vh);
}        </style>
    </head>
    <script>
        function onCheck(ev) {
            const x = document.getElementById('data').classList;
            if (ev.target.checked)
                x.add('Original');
            else
                x.remove('Original');
        }
    </script>
    <body>
        <ImgCmp-Control>
            <input id='check' type='checkbox' onchange='onCheck(event)' title='Click to toggle original on and off' />
            <label for='check' title='Click to toggle original on and off'>Show original</label>
        </ImgCmp-Control>
        <ImgCmp-Data id='data'>
{0}
        </ImgCmp-Data>
    </body>
</html>
";

            const String CmpFormat = @"
            <ImgCmp-Pair title='{2}'>
                <ImgCmp-Images>
                    <img src='{0}' />
                    <img src='{1}' />
                </ImgCmp-Images>
                <ImgCmp-Stats>
                    {3}
                </ImgCmp-Stats>
            </ImgCmp-Pair>
";

            #endregion//Template

        

    }

}
