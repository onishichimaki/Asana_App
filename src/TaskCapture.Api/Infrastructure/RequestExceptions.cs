namespace TaskCapture.Api.Infrastructure;

public sealed class TooManyRequestsException(string message) : Exception(message);
