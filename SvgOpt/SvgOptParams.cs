using System;

namespace SysWeaver
{
    public sealed class SvgOptParams
    {
        /// <summary>
        /// Remove all fill attributes.
        /// </summary>
        public bool RemoveFill = false;
        /// <summary>
        /// Remove size attribute.
        /// </summary>
        public bool RemoveSize = false;

        /// <summary>
        /// Remove comments.
        /// </summary>
        public bool RemoveComments = true;
        /// <summary>
        /// Remove white spaces
        /// </summary>
        public bool RemoveWhiteSpaces = true;

        /// <summary>
        /// Remove background rectangle
        /// </summary>
        public bool RemoveBackground = false;

        /// <summary>
        /// Remove clip-rules on paths (if they don't affect output)
        /// </summary>
        public bool RemoveClipRule = true;

        /// <summary>
        /// Remove fill-rules on paths (if they don't affect output)
        /// </summary>
        public bool RemoveFillRule = true;

        /// <summary>
        /// Try remove any attributes (on render items)
        /// </summary>
        public bool TryRemoveAttributes = false;

        /// <summary>
        /// Remove atribute spaces
        /// </summary>
        public bool RemoveAttributeSpaces = true;

        /// <summary>
        /// Remove title elements
        /// </summary>
        public bool RemoveTitle = true;

        /// <summary>
        /// Remove metadata elements
        /// </summary>
        public bool RemoveMetadata = true;

        /// <summary>
        /// Remove the doc type
        /// </summary>
        public bool RemoveDocType = true;

        /// <summary>
        /// Select the shortest color representation
        /// </summary>
        public bool ShortenColors = true;

        /// <summary>
        /// Remove spaces from a path
        /// </summary>
        public bool ShortenPath = true;

        /// <summary>
        /// Remove version
        /// </summary>
        public bool RemoveVersion = true;

        /// <summary>
        /// Remove spaces before an /&gt;
        /// </summary>
        public bool TrimTagClose = true;

        /// <summary>
        /// Remove transform
        /// </summary>
        public bool TrimTransform = true;

        /// <summary>
        /// Remove the leading zeros, i.e "0.5" => ".5" and "-0.3" to "-.3"
        /// </summary>
        public bool TrimLeadingZeros = true;

        /// Remove trailing zeros, i.e "10.500" => "10.5", "13.0" => "13", "0.0" => "0", ".10" => ".1", ".0" => "0", "-0.0" => "0", "-.000" => "0"
        public bool TrimTrailingZeros = true;

        /// <summary>
        /// Remove empty and / or unnecessary graphs
        /// </summary>
        public bool RemoveGraphs = true;

        /// <summary>
        /// Remove empty text nodes
        /// </summary>
        public bool RemoveTextNodes = true;

        /// <summary>
        /// Remove hidden nodes (display = "none")
        /// </summary>
        public bool RemoveHidden = true;

        /// <summary>
        /// Remove unused id's
        /// </summary>
        public bool RemoveIds = true;

        /// <summary>
        /// Remove use width or height of 100% 
        /// </summary>
        public bool RemoveUseSize = true;


        /// <summary>
        /// Rename id's to shorten them
        /// </summary>
        public bool RenameIds = true;


        /// <summary>
        /// Automatically select number of decimals so that at least this many coordinates are alllowed
        /// </summary>
        public int MaxDecimalRes = 0;

        /// <summary>
        /// The maximum number of decimals to keep, less than zero to disable
        /// </summary>
        public int MaxDecimals = -1;

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
