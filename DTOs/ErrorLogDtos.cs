using ML.Charity.API.Client.Models;
using System;
using System.Collections.Generic;

namespace ML.Charity.API.Client.DTOs
{
    public class ErrorLogsResponse
    {
        public int Total { get; set; }
        public string PartitionDate { get; set; } = string.Empty;
        public int? FilterStatusCode { get; set; }
        public List<ErrorLogEntity> Errors { get; set; } = new();
    }

    public class ErrorSummaryResponse
    {
        public string PartitionDate { get; set; } = string.Empty;
        public int TotalErrors { get; set; }
        public Dictionary<string, int> ByStatusCode { get; set; } = new();
        public Dictionary<string, int> ByErrorType { get; set; } = new();
        public Dictionary<string, int> TopFailedPaths { get; set; } = new();
        public List<AuthErrorSummaryItem> RecentAuthFailures { get; set; } = new();
    }

    public class AuthErrorSummaryItem
    {
        public DateTimeOffset? Timestamp { get; set; }
        public int StatusCode { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string RequestPath { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
    }
}
