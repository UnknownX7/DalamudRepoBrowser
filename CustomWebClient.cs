using System;
using System.Net;

namespace DalamudRepoBrowser;

/**
 * Custom Web Client that uses AutomaticDecompression by default to handle gzip response payloads.
 * This is a work-around as AutomaticDecompression is not exposed by WebClient.
 */
public class CustomWebClient : WebClient
{
    protected override WebRequest GetWebRequest(Uri address)
    {
        if (base.GetWebRequest(address) is not HttpWebRequest request) return base.GetWebRequest(address);
        request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
        return request;
    }
}