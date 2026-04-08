namespace SwCurl
{
    enum SwCompression
    {
        /// <summary>
        /// No payload compression at all.
        /// If auth method in SysWeaver, Brotli compression is used.
        /// </summary>
        Auto,
        None,
        Brotli,
        Deflate,
        GZip,
    }



}
