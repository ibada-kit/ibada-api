using Azure;
using Azure.Data.Tables;
using System;

namespace ML.Charity.API.Client.Models
{
    public class ErrorLogEntity : ITableEntity
    {
        // PartitionKey is the UTC Date (e.g., "2026-10-04") for efficient daily archiving and query performance
        public string PartitionKey { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // RowKey uses inverted UTC ticks + unique ID for automatic descending chronological order (newest first)
        public string RowKey { get; set; } = $"{(DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks):D19}_{Guid.NewGuid():N}";

        public DateTimeOffset? Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public ETag ETag { get; set; }

        // HTTP Response Status (401, 403, 400, 500, etc.)
        public int StatusCode { get; set; }

        // Categorized Error Type (e.g., "TokenExpired", "MissingToken", "InvalidSignature", "Forbidden", "UnhandledException", "LoginFailed")
        public string ErrorType { get; set; } = string.Empty;

        // Clear, human-readable description of why the error happened
        public string Reason { get; set; } = string.Empty;

        // Where the error occurred
        public string RequestPath { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = string.Empty;
        public string QueryString { get; set; } = string.Empty;

        // Caller Identity details (even from expired or unverified tokens)
        public string IpAddress { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;

        // Exception & Technical Details
        public string ExceptionType { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string AdditionalInfo { get; set; } = string.Empty;
    }
}
