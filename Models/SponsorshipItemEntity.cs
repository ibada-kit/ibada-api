using Azure;
using Azure.Data.Tables;
using System;

namespace ML.Charity.API.Client.Models
{
    /// <summary>
    /// Represents a sponsorship item/package available for organizations and firms to sponsor.
    /// Stored in Azure Table Storage under table "SponsorshipItems".
    /// </summary>
    public class SponsorshipItemEntity : ITableEntity
    {
        // PartitionKey: Panchayath (e.g. "Madavoor")
        public string PartitionKey { get; set; } = "Madavoor";

        // RowKey: ItemId (e.g. "ITEM-001")
        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double ItemPrice { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 0;
        public DateTime UpdateDate { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = string.Empty;
    }
}
