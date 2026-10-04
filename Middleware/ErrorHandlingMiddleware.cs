using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ML.Charity.API.Client.Services;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Middleware
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IErrorLoggingService errorLogger)
        {
            try
            {
                await _next(context);

                // If the response completed with an error status code (>= 400)
                // and has not already been logged by JwtBearerEvents or other middleware
                if (context.Response.StatusCode >= 400 && !context.Items.ContainsKey("ErrorAlreadyLogged"))
                {
                    context.Items["ErrorAlreadyLogged"] = true;
                    await errorLogger.LogHttpResponseErrorAsync(context, context.Response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception occurred while processing request {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                // Avoid duplicate logs if already recorded
                if (!context.Items.ContainsKey("ErrorAlreadyLogged"))
                {
                    context.Items["ErrorAlreadyLogged"] = true;
                    await errorLogger.LogExceptionAsync(context, ex);
                }

                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";

                    var errorResponse = new
                    {
                        statusCode = 500,
                        error = "InternalServerError",
                        message = "An unexpected server error occurred. The error details have been logged for administrative review.",
                        path = context.Request.Path.Value,
                        timestamp = DateTime.UtcNow
                    };

                    await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
                }
            }
        }
    }
}
