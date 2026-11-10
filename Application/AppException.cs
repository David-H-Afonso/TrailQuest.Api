namespace TrailQuest.Api.Application;

public sealed class AppException(string code, int statusCode, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
