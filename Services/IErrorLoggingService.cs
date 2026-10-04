using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using ML.Charity.API.Client.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Services
{
    public interface IErrorLoggingService
    {
        /// <summary>
        /// Directly persists an error log entity into Azure Table Storage and logs via ILogger.
        /// </summary>
        Task LogEntityAsync(ErrorLogEntity entity);

        /// <summary>
        /// Captures JWT authentication failures (e.g. expired tokens, invalid signatures, token structure errors).
        /// </summary>
        Task LogAuthenticationFailedAsync(AuthenticationFailedContext context);

        /// <summary>
        /// Captures unauthorized challenge events (e.g. missing Authorization header or Bearer token on protected endpoints).
        /// </summary>
        Task LogChallengeAsync(JwtBearerChallengeContext context);

        /// <summary>
        /// Captures authorization forbidden events (e.g. role requirement not met).
        /// </summary>
        Task LogForbiddenAsync(ForbiddenContext context);

        /// <summary>
        /// Captures unhandled 500 exceptions thrown in the ASP.NET Core pipeline or controllers.
        /// </summary>
        Task LogExceptionAsync(HttpContext context, Exception ex);

        /// <summary>
        /// Captures HTTP error status codes (e.g. 401 from Auth/login, 400 bad requests).
        /// </summary>
        Task LogHttpResponseErrorAsync(HttpContext context, int statusCode, string? customReason = null);

        /// <summary>
        /// Retrieves recent error logs from Azure Table Storage for diagnostics and admin auditing.
        /// </summary>
        Task<List<ErrorLogEntity>> GetRecentErrorLogsAsync(int count = 50, string? partitionDate = null, int? filterStatusCode = null);
    }
}
