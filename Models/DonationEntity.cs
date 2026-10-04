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
        public int GeneralKitCount { get; set; }
        public int StudentKitCount { get; set; }
        public string KitStyle { get; set; } = nameof(Models.KitStyle.General);
        public double TotalAmount { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Sequential lucky draw serial number starting from 10000 for kit donations.
        /// </summary>
        public int SerialNumber { get; set; }

        // Payment Options: PayFull, Book, Advance
        public string PaymentOption { get; set; } = "PayFull";
        public double AmountPaid { get; set; } = 0;
        public double BalanceAmount { get; set; } = 0;

        // Payment Status: Completed, Partial, Booked
        public string PaymentStatus { get; set; } = "Completed";
        public string PaymentMode { get; set; } = "Cash"; // Cash, UPI, Cheque, BankTransfer
        public string TransactionReference { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;
        public DateTime UpdateDate { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = string.Empty;
    }
}