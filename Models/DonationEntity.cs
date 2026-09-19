using Azure;
using Azure.Data.Tables;

namespace ML.Charity.API.Client.Models
{
    public class DonationEntity : ITableEntity
    {
        // PartitionKey: Panchayath (e.g., "Madavoor")
        public string PartitionKey { get; set; } = string.Empty;

        // RowKey: ReceiptToken (e.g., "MDV-8F4B2")
        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string DonationId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty; // ID of the volunteer who collected it
        public int WardNumber { get; set; }

        public string DonorName { get; set; } = string.Empty;
        public string WhatsAppNumber { get; set; } = string.Empty;
        public int KitCount { get; set; }
        public double TotalAmount { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    }
}