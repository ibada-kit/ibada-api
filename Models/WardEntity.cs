using Azure;
using Azure.Data.Tables;
using System;

namespace ML.Charity.API.Client.Models
{
    /// <summary>
    /// Represents a Ward entity stored in Azure Table Storage under table "Wards".
    /// PartitionKey: Panchayath (e.g., "Madavoor")
    /// RowKey: WardNumber (e.g., "1", "2", ... "12")
    /// </summary>
    public class WardEntity : ITableEntity
    {
        // PartitionKey: Panchayath name
        public string PartitionKey { get; set; } = "Madavoor";

        // RowKey: WardNumber as string (e.g. "1", "2")
        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public int WardNumber { get; set; }
        public string WardName { get; set; } = string.Empty;
        public string Panchayath { get; set; } = "Madavoor";
    }
}
