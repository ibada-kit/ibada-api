using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ML.Charity.API.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Services
{
    public class ErrorLoggingService : IErrorLoggingService
    {
        private readonly ITableStorageService<ErrorLogEntity> _tableStorage;
        private readonly ILogger<ErrorLoggingService> _logger;
        private const int MaxStringLength = 30000; // Azure Table Storage 32KB string limit safety guard

        public ErrorLoggingService(ITableStorageService<ErrorLogEntity> tableStorage, ILogger<ErrorLoggingService> logger)
        {
            _tableStorage = tableStorage;
            _logger = logger;
        }

        public async Task LogEntityAsync(ErrorLogEntity entity)
        {
            try
            {
                // Ensure field lengths are within Azure Table limits
                entity.Reason = Truncate(entity.Reason, 2000);
                entity.RequestPath = Truncate(entity.RequestPath, 1000);
                entity.QueryString = Truncate(entity.QueryString, 2000);
                entity.Details = Truncate(entity.Details, MaxStringLength);
                entity.AdditionalInfo = Truncate(entity.AdditionalInfo, MaxStringLength);
                entity.UserAgent = Truncate(entity.UserAgent, 1000);

                await _tableStorage.AddAsync(entity);

                _logger.LogWarning(
                    "[ERROR_LOG] HTTP {StatusCode} | Type: {ErrorType} | Path: {Method} {Path} | User: {UserId} ({UserName}, Role: {UserRole}) | IP: {Ip} | Reason: {Reason}",
                    entity.StatusCode, entity.ErrorType, entity.HttpMethod, entity.RequestPath,
                    entity.UserId, entity.UserName, entity.UserRole, entity.IpAddress, entity.Reason);
            }
            catch (Exception ex)
            {
                // Never let a table storage failure propagate up and crash the application request pipeline
                _logger.LogError(ex, "Failed to persist error log to Azure Table Storage: {Message}", ex.Message);
            }
        }

        public async Task LogAuthenticationFailedAsync(AuthenticationFailedContext context)
        {
            context.HttpContext.Items["ErrorAlreadyLogged"] = true;

            var httpContext = context.HttpContext;
            var authHeader = httpContext.Request.Headers.Authorization.ToString();
            var (tokenUserId, tokenUserName, tokenPhone, tokenRole, exp, iat, issuer, audience) = ParseRawBearerToken(authHeader);

            var ex = context.Exception;
            string errorType;
            string reason;

            if (ex is SecurityTokenExpiredException)
            {
                errorType = "TokenExpired";
                var minutesPast = exp.HasValue ? (DateTime.UtcNow - exp.Value).TotalMinutes : 0;
                reason = $"JWT expired {minutesPast:F1} minutes ago. ValidTo: {exp:O}, ServerUtcNow: {DateTime.UtcNow:O}";
            }
            else if (ex is SecurityTokenInvalidSignatureException)
            {
                errorType = "InvalidSignature";
                reason = $"JWT signature verification failed: {ex.Message}";
            }
            else if (ex is SecurityTokenInvalidIssuerException)
            {
                errorType = "InvalidIssuer";
                reason = $"JWT issuer mismatch: {ex.Message} (Expected issuer configured on server)";
            }
            else if (ex is SecurityTokenInvalidAudienceException)
            {
                errorType = "InvalidAudience";
                reason = $"JWT audience mismatch: {ex.Message}";
            }
            else if (ex != null)
            {
                errorType = "AuthenticationFailed";
                reason = $"Authentication token validation failed: {ex.Message}";
            }
            else
            {
                errorType = "AuthenticationFailed";
                reason = "Authentication failed for provided token.";
            }

            var additionalData = new Dictionary<string, object?>
            {
                { "TokenExpirationUtc", exp?.ToString("O") },
                { "TokenIssuedAtUtc", iat?.ToString("O") },
                { "TokenIssuer", issuer },
                { "TokenAudience", audience },
                { "ExceptionType", ex?.GetType().FullName },
                { "HasBearerToken", !string.IsNullOrWhiteSpace(authHeader) }
            };

            var entity = new ErrorLogEntity
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ErrorType = errorType,
                Reason = reason,
                RequestPath = httpContext.Request.Path.Value ?? string.Empty,
                HttpMethod = httpContext.Request.Method,
                QueryString = httpContext.Request.QueryString.Value ?? string.Empty,
                IpAddress = GetClientIpAddress(httpContext),
                UserId = tokenUserId ?? string.Empty,
                UserName = tokenUserName ?? string.Empty,
                PhoneNumber = tokenPhone ?? string.Empty,
                UserRole = tokenRole ?? string.Empty,
                UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
                ExceptionType = ex?.GetType().FullName ?? string.Empty,
                Details = ex?.ToString() ?? string.Empty,
                AdditionalInfo = JsonSerializer.Serialize(additionalData)
            };

            await LogEntityAsync(entity);
        }

        public async Task LogChallengeAsync(JwtBearerChallengeContext context)
        {
            // If already logged by OnAuthenticationFailed, do not log a second time
            if (context.HttpContext.Items.ContainsKey("ErrorAlreadyLogged"))
                return;

            context.HttpContext.Items["ErrorAlreadyLogged"] = true;

            var httpContext = context.HttpContext;
            var authHeader = httpContext.Request.Headers.Authorization.ToString();

            string errorType;
            string reason;

            if (string.IsNullOrWhiteSpace(authHeader))
            {
                errorType = "MissingToken";
                reason = "Protected endpoint accessed without Authorization header or Bearer token.";
            }
            else if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                errorType = "InvalidAuthScheme";
                reason = $"Authorization header must start with 'Bearer '. Received: '{Truncate(authHeader, 20)}'";
            }
            else
            {
                errorType = "UnauthorizedChallenge";
                reason = string.IsNullOrWhiteSpace(context.ErrorDescription)
                    ? (string.IsNullOrWhiteSpace(context.Error) ? "Authorization challenge triggered." : context.Error)
                    : $"{context.Error}: {context.ErrorDescription}";
            }

            var (tokenUserId, tokenUserName, tokenPhone, tokenRole, exp, iat, issuer, audience) = ParseRawBearerToken(authHeader);

            var entity = new ErrorLogEntity
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ErrorType = errorType,
                Reason = reason,
                RequestPath = httpContext.Request.Path.Value ?? string.Empty,
                HttpMethod = httpContext.Request.Method,
                QueryString = httpContext.Request.QueryString.Value ?? string.Empty,
                IpAddress = GetClientIpAddress(httpContext),
                UserId = tokenUserId ?? string.Empty,
                UserName = tokenUserName ?? string.Empty,
                PhoneNumber = tokenPhone ?? string.Empty,
                UserRole = tokenRole ?? string.Empty,
                UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
                ExceptionType = context.AuthenticateFailure?.GetType().FullName ?? string.Empty,
                Details = context.AuthenticateFailure?.ToString() ?? string.Empty,
                AdditionalInfo = JsonSerializer.Serialize(new
                {
                    ChallengeError = context.Error,
                    ChallengeErrorDescription = context.ErrorDescription,
                    HasToken = !string.IsNullOrWhiteSpace(authHeader),
                    TokenExpiry = exp?.ToString("O")
                })
            };

            await LogEntityAsync(entity);
        }

        public async Task LogForbiddenAsync(ForbiddenContext context)
        {
            context.HttpContext.Items["ErrorAlreadyLogged"] = true;

            var httpContext = context.HttpContext;
            var (userId, userName, phone, role) = ExtractUserFromContext(httpContext);

            if (string.IsNullOrEmpty(userId))
            {
                var authHeader = httpContext.Request.Headers.Authorization.ToString();
                var parsed = ParseRawBearerToken(authHeader);
                userId = parsed.userId;
                userName = parsed.userName;
                phone = parsed.phone;
                role = parsed.role;
            }

            var reason = $"Access denied. User '{userName ?? userId}' with role '{role}' lacks permission for this endpoint.";

            var entity = new ErrorLogEntity
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ErrorType = "Forbidden",
                Reason = reason,
                RequestPath = httpContext.Request.Path.Value ?? string.Empty,
                HttpMethod = httpContext.Request.Method,
                QueryString = httpContext.Request.QueryString.Value ?? string.Empty,
                IpAddress = GetClientIpAddress(httpContext),
                UserId = userId ?? string.Empty,
                UserName = userName ?? string.Empty,
                PhoneNumber = phone ?? string.Empty,
                UserRole = role ?? string.Empty,
                UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
                Details = "Endpoint authorization policy or role check rejected this caller."
            };

            await LogEntityAsync(entity);
        }

        public async Task LogExceptionAsync(HttpContext context, Exception ex)
        {
            var (userId, userName, phone, role) = ExtractUserFromContext(context);
            if (string.IsNullOrEmpty(userId))
            {
                var authHeader = context.Request.Headers.Authorization.ToString();
                var parsed = ParseRawBearerToken(authHeader);
                userId = parsed.userId;
                userName = parsed.userName;
                phone = parsed.phone;
                role = parsed.role;
            }

            var entity = new ErrorLogEntity
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorType = "UnhandledException",
                Reason = ex.Message,
                RequestPath = context.Request.Path.Value ?? string.Empty,
                HttpMethod = context.Request.Method,
                QueryString = context.Request.QueryString.Value ?? string.Empty,
                IpAddress = GetClientIpAddress(context),
                UserId = userId ?? string.Empty,
                UserName = userName ?? string.Empty,
                PhoneNumber = phone ?? string.Empty,
                UserRole = role ?? string.Empty,
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                ExceptionType = ex.GetType().FullName ?? string.Empty,
                Details = ex.ToString()
            };

            await LogEntityAsync(entity);
        }

        public async Task LogHttpResponseErrorAsync(HttpContext context, int statusCode, string? customReason = null)
        {
            var (userId, userName, phone, role) = ExtractUserFromContext(context);
            if (string.IsNullOrEmpty(userId))
            {
                var authHeader = context.Request.Headers.Authorization.ToString();
                var parsed = ParseRawBearerToken(authHeader);
                userId = parsed.userId;
                userName = parsed.userName;
                phone = parsed.phone;
                role = parsed.role;
            }

            string errorType = statusCode switch
            {
                401 => "UnauthorizedResponse",
                403 => "ForbiddenResponse",
                400 => "BadRequest",
                404 => "NotFound",
                _ => $"HttpError_{statusCode}"
            };

            string reason = customReason ?? (statusCode switch
            {
                401 when context.Request.Path.Value?.Contains("Auth/login", StringComparison.OrdinalIgnoreCase) == true
                    => "Login attempt failed: invalid phone number or incorrect password.",
                401 => "Request returned 401 Unauthorized.",
                403 => "Request returned 403 Forbidden.",
                400 => "Request returned 400 Bad Request.",
                404 => "Resource not found (404).",
                _ => $"HTTP {statusCode} error occurred."
            });

            var entity = new ErrorLogEntity
            {
                StatusCode = statusCode,
                ErrorType = errorType,
                Reason = reason,
                RequestPath = context.Request.Path.Value ?? string.Empty,
                HttpMethod = context.Request.Method,
                QueryString = context.Request.QueryString.Value ?? string.Empty,
                IpAddress = GetClientIpAddress(context),
                UserId = userId ?? string.Empty,
                UserName = userName ?? string.Empty,
                PhoneNumber = phone ?? string.Empty,
                UserRole = role ?? string.Empty,
                UserAgent = context.Request.Headers.UserAgent.ToString()
            };

            await LogEntityAsync(entity);
        }

        public async Task<List<ErrorLogEntity>> GetRecentErrorLogsAsync(int count = 50, string? partitionDate = null, int? filterStatusCode = null)
        {
            var targetPartition = string.IsNullOrWhiteSpace(partitionDate)
                ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                : partitionDate.Trim();

            var logs = await _tableStorage.QueryEntitiesByPartitionAsync(targetPartition);

            // RowKey inverted ticks already sorts newest first
            var query = logs.AsEnumerable();

            if (filterStatusCode.HasValue)
            {
                query = query.Where(l => l.StatusCode == filterStatusCode.Value);
            }

            return query.Take(Math.Clamp(count, 1, 200)).ToList();
        }

        private static string GetClientIpAddress(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) && !string.IsNullOrWhiteSpace(cfIp))
                return cfIp.ToString().Trim();

            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
            {
                var ips = forwardedFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ips.Length > 0 && !string.IsNullOrWhiteSpace(ips[0]))
                    return ips[0];
            }

            if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp) && !string.IsNullOrWhiteSpace(realIp))
                return realIp.ToString().Trim();

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        private static (string? userId, string? userName, string? phone, string? role) ExtractUserFromContext(HttpContext context)
        {
            var user = context.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirst("UserId")?.Value
                          ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var userName = user.FindFirst(ClaimTypes.Name)?.Value;
                var phone = user.FindFirst("PhoneNumber")?.Value;
                var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
                return (userId, userName, phone, role);
            }
            return (null, null, null, null);
        }

        private static (string? userId, string? userName, string? phone, string? role, DateTime? exp, DateTime? iat, string? issuer, string? audience) ParseRawBearerToken(string? authHeader)
        {
            if (string.IsNullOrWhiteSpace(authHeader))
                return (null, null, null, null, null, null, null, null);

            var parts = authHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                return (null, null, null, null, null, null, null, null);

            var tokenStr = parts[1].Trim();
            try
            {
                var handler = new JsonWebTokenHandler();
                if (handler.CanReadToken(tokenStr))
                {
                    var jwt = handler.ReadJsonWebToken(tokenStr);
                    var userId = jwt.Claims.FirstOrDefault(c => c.Type == "UserId" || c.Type == "sub" || c.Type == "nameid" || c.Type == ClaimTypes.NameIdentifier || c.Type.EndsWith("/nameidentifier"))?.Value;
                    var userName = jwt.Claims.FirstOrDefault(c => c.Type == "FullName" || c.Type == "name" || c.Type == "unique_name" || c.Type == ClaimTypes.Name || c.Type.EndsWith("/name"))?.Value;
                    var phone = jwt.Claims.FirstOrDefault(c => c.Type == "PhoneNumber" || c.Type == "phone" || c.Type.EndsWith("/mobilephone") || c.Type.EndsWith("/otherphone"))?.Value;
                    var role = jwt.Claims.FirstOrDefault(c => c.Type == "role" || c.Type == ClaimTypes.Role || c.Type.EndsWith("/role"))?.Value;
                    return (userId, userName, phone, role, jwt.ValidTo, jwt.IssuedAt, jwt.Issuer, string.Join(",", jwt.Audiences));
                }
            }
            catch
            {
                // Fallback to JwtSecurityTokenHandler if JsonWebTokenHandler fails
                try
                {
                    var jwtHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    if (jwtHandler.CanReadToken(tokenStr))
                    {
                        var jwt = jwtHandler.ReadJwtToken(tokenStr);
                        var userId = jwt.Claims.FirstOrDefault(c => c.Type == "UserId" || c.Type == "sub" || c.Type == "nameid" || c.Type == ClaimTypes.NameIdentifier || c.Type.EndsWith("/nameidentifier"))?.Value;
                        var userName = jwt.Claims.FirstOrDefault(c => c.Type == "FullName" || c.Type == "name" || c.Type == "unique_name" || c.Type == ClaimTypes.Name || c.Type.EndsWith("/name"))?.Value;
                        var phone = jwt.Claims.FirstOrDefault(c => c.Type == "PhoneNumber" || c.Type == "phone" || c.Type.EndsWith("/mobilephone") || c.Type.EndsWith("/otherphone"))?.Value;
                        var role = jwt.Claims.FirstOrDefault(c => c.Type == "role" || c.Type == ClaimTypes.Role || c.Type.EndsWith("/role"))?.Value;
                        return (userId, userName, phone, role, jwt.ValidTo, jwt.ValidFrom, jwt.Issuer, string.Join(",", jwt.Audiences));
                    }
                }
                catch { }
            }

            return (null, null, null, null, null, null, null, null);
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength] + "... [truncated]";
        }
    }
}
