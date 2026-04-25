using System.Net;

namespace VrcTwitchOscBridge.Services;

public sealed class VrChatApiException : Exception
{
    public VrChatApiException(HttpStatusCode statusCode, string apiMessage)
        : base(apiMessage)
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage;
    }

    public HttpStatusCode StatusCode { get; }

    public string ApiMessage { get; }
}
