namespace SwCurl
{
    /// <summary>
    /// The HTTP method of the request (the -HttpMethod option).
    /// </summary>
    enum SwMethods
    {
        /// <summary>
        /// GET if no payload, POST if payload.
        /// If auth method is SysWeaver, GET is used for payloads smaller than GetThreshold bytes (sent as query data).
        /// </summary>
        Auto,
        POST,
        GET,
        DELETE,
        PUT,
    }



}
