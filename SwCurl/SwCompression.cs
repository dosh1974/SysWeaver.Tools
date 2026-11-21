namespace SwCurl
{
    /// <summary>
    /// The compression of the request payload (the -Compression option).
    /// </summary>
    enum SwCompression
    {
        /// <summary>
        /// Brotli if the SysWeaver login is used and the payload has a compressible MIME type, else no compression.
        /// </summary>
        Auto,
        /// <summary>
        /// No payload compression.
        /// </summary>
        None,
        /// <summary>
        /// Brotli ("br") compression.
        /// </summary>
        Brotli,
        /// <summary>
        /// Deflate compression.
        /// </summary>
        Deflate,
        /// <summary>
        /// GZip compression.
        /// </summary>
        GZip,
    }



}
