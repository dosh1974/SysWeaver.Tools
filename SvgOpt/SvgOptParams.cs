using System;
using SysWeaver.Minifier;

namespace SysWeaver
{

    public sealed class SvgOptParams : SvgMinifierParams
    {

        /// <summary>
        /// Supply a filename to create a .html file that can be used to compare the original and the modified files
        /// </summary>
        public String Compare;

        const Char Version = 'k';

        public String CacheKey => String.Concat(
            Version,
            TryRemoveAttributes ? 'R' : '_',
            TrimTrailingZeros ? 'Z' : '_',
            TrimLeadingZeros ? 'Z' : '_',
            TrimTransform ? 'T' : '_',
            TrimTagClose ? 'T' : '_',
            RemoveVersion ? 'V' : '_',
            ShortenPath ? 'P' : '_',
            ShortenColors ? 'S' : '_',
            RemoveDocType ? 'D' : '_',
            RemoveMetadata ? 'M' : '_',
            RemoveTitle ? 'T' : '_',
            RemoveAttributeSpaces ? 'A' : '_',
            RemoveClipRule ? 'C' : '_',
            RemoveFillRule ? 'F' : '_',
            RemoveBackground ? 'B' : '_',
            RemoveWhiteSpaces ? 'W' : '_',
            RemoveComments ? 'C' : '_',
            RemoveSize ? 'S' : '_',
            RemoveFill ? 'F' : '_',
            RemoveGraphs ? 'G' : '_',
            RemoveTextNodes ? 'T' : '_',
            RemoveHidden ? 'H' : '_',
            RemoveIds ? 'I' : '_',
            RemoveUseSize ? 'U' : '_',
            RenameIds ? 'R' : '_',
            MaxDecimalRes < 0 ? "0" : MaxDecimalRes.ToString(), '_',
            MaxDecimals < 0 ? "" : MaxDecimals.ToString()
            );
    }


}
