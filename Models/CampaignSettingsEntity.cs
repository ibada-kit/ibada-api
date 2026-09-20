using Azure;
using Azure.Data.Tables;
using System;

namespace ML.Charity.API.Client.Models
{
    /// <summary>
    /// Represents campaign configuration stored in Azure Table Storage under table "CampaignSettings".
    /// PartitionKey: "GLOBAL"
    /// RowKey: "KitPrice"
    /// </summary>
    public class CampaignSettingsEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "GLOBAL";
        public string RowKey { get; set; } = "KitPrice";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public double KitPrice { get; set; } = 1000.0;
        public DateTimeOffset ModifiedDate { get; set; } = DateTimeOffset.UtcNow;
        public string ModifiedBy { get; set; } = "System";
    }
}
