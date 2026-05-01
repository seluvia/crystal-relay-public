using System.Net;

namespace VrcTwitchOscBridge.Services;

public sealed class TwitchApiException : Exception
{
    public TwitchApiException(HttpStatusCode statusCode, string apiMessage, DateTimeOffset? retryAfterUtc = null)
        : base(apiMessage)
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage;
        RetryAfterUtc = retryAfterUtc;
    }

    public HttpStatusCode StatusCode { get; }

    public string ApiMessage { get; }

    public DateTimeOffset? RetryAfterUtc { get; }
}
