using Azure;
using Azure.Data.Tables;
using System;

namespace ML.Charity.API.Client.Models
{
    /// <summary>
    /// Represents an organization/firm sponsorship transaction.
    /// Stored in Azure Table Storage under table "Sponsorships".
    /// </summary>
    public class SponsorshipEntity : ITableEntity
    {
        // PartitionKey: Panchayath (e.g. "Madavoor")
        public string PartitionKey { get; set; } = "Madavoor";

        // RowKey: ReceiptToken (e.g. "SPON-8F2B1A0C")
        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string SponsorshipId { get; set; } = Guid.NewGuid().ToString();
        public string ReceiptToken { get; set; } = string.Empty;

        // Firm / Organization Info
        public string DonorName { get; set; } = string.Empty; // Name of the firm/company/organization
        public string ContactPerson { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty; // Contact phone/mobile number

        // Sponsored Item Details (Snapshot from SponsorshipItemEntity)
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public double ItemPrice { get; set; }
        public int Quantity { get; set; } = 1;
        public double TotalAmount { get; set; }
        public string ItemsJson { get; set; } = string.Empty;

        // Payment Options: PayFull, Book, Advance
        public string PaymentOption { get; set; } = string.Empty; 
        public double AmountPaid { get; set; } = 0;
        public double BalanceAmount { get; set; } = 0;

        // Payment Status: Completed, Partial, Booked
        public string PaymentStatus { get; set; } = string.Empty;
        public string PaymentMode { get; set; } = string.Empty; // Cash, UPI, Cheque, BankTransfer
        public string TransactionReference { get; set; } = string.Empty;

        // Collection & Hierarchy Attribution
        public string CollectedByUserId { get; set; } = string.Empty;
        public string CollectedByName { get; set; } = string.Empty;
        public string CollectedByRole { get; set; } = string.Empty;
        public string ParentUserId { get; set; } = string.Empty; // Coordinator ID for volunteer
        public int WardNumber { get; set; }

        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime UpdateDate { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = string.Empty;
    }
}
