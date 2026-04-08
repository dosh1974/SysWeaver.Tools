namespace SwCurl
{
    enum SwMethods
    {
        /// <summary>
        /// GET if no payload, POST if payload.
        /// If auth method is SysWeaver, GET can be used even with payload.
        /// </summary>
        Auto,
        POST,
        GET,
        DELETE,
        PUT,
    }



}
