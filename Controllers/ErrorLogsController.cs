using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class ErrorLogsController : ControllerBase
    {
        private readonly IErrorLoggingService _errorLogger;

        public ErrorLogsController(IErrorLoggingService errorLogger)
        {
            _errorLogger = errorLogger;
        }

        /// <summary>
        /// Retrieves recent error logs from Azure Table Storage (Admin only).
        /// </summary>
        /// <param name="date">UTC Date partition (format: yyyy-MM-dd). Defaults to today's UTC date.</param>
        /// <param name="count">Maximum entries to return (1-200). Default is 50.</param>
        /// <param name="statusCode">Optional filter by HTTP status code (e.g. 401, 403, 500).</param>
        [HttpGet]
        public async Task<ActionResult<ErrorLogsResponse>> GetErrorLogs(
            [FromQuery] string? date = null,
            [FromQuery] int count = 50,
            [FromQuery] int? statusCode = null)
        {
            var logs = await _errorLogger.GetRecentErrorLogsAsync(count, date, statusCode);
            return Ok(new ErrorLogsResponse
            {
                Total = logs.Count,
                PartitionDate = string.IsNullOrWhiteSpace(date) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : date,
                FilterStatusCode = statusCode,
                Errors = logs
            });
        }

        /// <summary>
        /// Provides an aggregated breakdown of errors for a given date (Admin only).
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<ErrorSummaryResponse>> GetErrorSummary([FromQuery] string? date = null)
        {
            var logs = await _errorLogger.GetRecentErrorLogsAsync(200, date);

            var byStatusCode = logs
                .GroupBy(l => l.StatusCode)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var byErrorType = logs
                .GroupBy(l => l.ErrorType)
                .ToDictionary(g => g.Key, g => g.Count());

            var topFailedPaths = logs
                .GroupBy(l => $"{l.HttpMethod} {l.RequestPath}")
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            var authErrors = logs
                .Where(l => l.StatusCode == 401 || l.StatusCode == 403)
                .Select(l => new AuthErrorSummaryItem
                {
                    Timestamp = l.Timestamp,
                    StatusCode = l.StatusCode,
                    ErrorType = l.ErrorType,
                    Reason = l.Reason,
                    RequestPath = l.RequestPath,
                    IpAddress = l.IpAddress,
                    UserId = l.UserId,
                    UserName = l.UserName,
                    PhoneNumber = l.PhoneNumber,
                    UserRole = l.UserRole
                })
                .Take(20)
                .ToList();

            return Ok(new ErrorSummaryResponse
            {
                PartitionDate = string.IsNullOrWhiteSpace(date) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : date,
                TotalErrors = logs.Count,
                ByStatusCode = byStatusCode,
                ByErrorType = byErrorType,
                TopFailedPaths = topFailedPaths,
                RecentAuthFailures = authErrors
            });
        }
    }
}
