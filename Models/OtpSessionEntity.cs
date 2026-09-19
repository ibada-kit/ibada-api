using Azure;
using Azure.Data.Tables;
using System;

namespace ML.Charity.API.Client.Models
{
    public class OtpSessionEntity : ITableEntity
    {
        // PartitionKey: "OTP"
        public string PartitionKey { get; set; } = "OTP";

        // RowKey: Normalized PhoneNumber (e.g., "+919876543210")
        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string OtpHash { get; set; } = string.Empty;
        public DateTimeOffset ExpiryTime { get; set; }
        public int FailedAttempts { get; set; } = 0;
    }
}