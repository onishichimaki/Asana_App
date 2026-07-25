using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace TaskCapture.Api.Infrastructure;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ValidationException => (StatusCodes.Status400BadRequest, "入力内容を確認してください"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "この操作は許可されていません"),
            TooManyRequestsException => (StatusCodes.Status429TooManyRequests, "しばらく待ってからやり直してください"),
            InvalidOperationException => (StatusCodes.Status503ServiceUnavailable, "Service is not configured"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        logger.LogError(exception, "Request failed. TraceId={TraceId}; Status={Status}", httpContext.TraceIdentifier, status);
        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : exception.Message,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            },
            Exception = exception
        });
    }
}
