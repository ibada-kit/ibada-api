using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ML.Charity.API.Client.DTOs
{
    // ==========================================
    // SPONSORSHIP ITEM (CATALOG) DTOs
    // ==========================================

    public class SponsorshipItemDto
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double ItemPrice { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
        public DateTime UpdateDate { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    public class CreateSponsorshipItemRequest
    {
        public string? ItemId { get; set; }

        [Required(ErrorMessage = "Item name is required.")]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Item price is required.")]
        [Range(1.0, 100000000.0, ErrorMessage = "Item price must be greater than zero.")]
        public double ItemPrice { get; set; }

        public string? Description { get; set; }
        public int DisplayOrder { get; set; } = 0;
    }

    public class UpdateSponsorshipItemRequest
    {
        [Required(ErrorMessage = "Item name is required.")]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Item price is required.")]
        [Range(1.0, 100000000.0, ErrorMessage = "Item price must be greater than zero.")]
        public double ItemPrice { get; set; }

        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 0;
    }

    // ==========================================
    // SPONSORSHIP TRANSACTION DTOs
    // ==========================================

    public class SponsorshipItemSelectionDto
    {
        public string ItemId { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
    }

    public class SponsorshipItemDetailDto
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
        public int Quantity { get; set; }
        public double Subtotal { get; set; }
    }

    public class CreateSponsorshipRequest
    {
        [Required(ErrorMessage = "Firm or Organization name is required.")]
        [StringLength(150, MinimumLength = 2)]
        public string DonorName { get; set; } = string.Empty;

        public string? ContactPerson { get; set; }

        [Required(ErrorMessage = "Contact phone/mobile number is required.")]
        [Phone(ErrorMessage = "Invalid mobile number format.")]
        public string MobileNumber { get; set; } = string.Empty;

        // For backward-compatibility or single-item calls
        public string? ItemId { get; set; }
        public int Quantity { get; set; } = 1;

        // For multi-item sponsorship (Creates a SINGLE receipt for all selected items)
        public List<SponsorshipItemSelectionDto>? Items { get; set; }

        /// <summary>
        /// Three payment options: PayFull, Book, Advance
        /// </summary>
        [Required(ErrorMessage = "Payment option is required.")]
        [RegularExpression("^(PayFull|Book|Advance)$", ErrorMessage = "Payment option must be 'PayFull', 'Book', or 'Advance'.")]
        public string PaymentOption { get; set; } = "PayFull";

        /// <summary>
        /// Initial amount paid. Required if PaymentOption is 'Advance'.
        /// For 'PayFull', this will be set to the full total automatically if omitted.
        /// For 'Book', this can be 0 or nominal booking fee.
        /// </summary>
        [Range(0.0, 100000000.0, ErrorMessage = "Initial amount paid cannot be negative.")]
        public double? InitialAmountPaid { get; set; }

        public string? PaymentMode { get; set; } // Cash, UPI, Cheque, BankTransfer
        public string? TransactionReference { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateSponsorshipPaymentRequest
    {
        [Required(ErrorMessage = "Payment amount is required.")]
        [Range(0.01, 100000000.0, ErrorMessage = "Payment amount must be greater than 0.")]
        public double AmountToPay { get; set; }

        public string? PaymentMode { get; set; } // Cash, UPI, Cheque, BankTransfer
        public string? TransactionReference { get; set; }
        public string? Notes { get; set; }
    }

    public class SponsorshipResponse
    {
        public string SponsorshipId { get; set; } = string.Empty;
        public string ReceiptToken { get; set; } = string.Empty;
        public string DonorName { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;

        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public double ItemPrice { get; set; }
        public int Quantity { get; set; }
        public double TotalAmount { get; set; }
        public string ItemsJson { get; set; } = string.Empty;
        public List<SponsorshipItemDetailDto>? Items { get; set; }

        public string PaymentOption { get; set; } = string.Empty; // PayFull, Book, Advance
        public double AmountPaid { get; set; }
        public double BalanceAmount { get; set; }
        public string PaymentStatus { get; set; } = string.Empty; // Completed, Partial, Booked
        public string PaymentMode { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;

        public string Panchayath { get; set; } = string.Empty;
        public int WardNumber { get; set; }
        public string CollectedByUserId { get; set; } = string.Empty;
        public string CollectedByName { get; set; } = string.Empty;
        public string CollectedByRole { get; set; } = string.Empty;
        public string ParentUserId { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    // ==========================================
    // SEPARATE SPONSORSHIP LEADERBOARD DTOs
    // ==========================================

    public class SponsorshipLeaderboardEntry
    {
        public int Position { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int WardNumber { get; set; }
        public string Panchayath { get; set; } = string.Empty;
        public int SponsorshipCount { get; set; }
        public double TotalCommittedAmount { get; set; }
        public double TotalPaidAmount { get; set; }
        public double BalanceAmount { get; set; }
        public string RankBadge { get; set; } = string.Empty; // Gold, Silver, Bronze, Contributor
    }

    public class SponsorshipWardLeaderboardEntry
    {
        public int Position { get; set; }
        public int WardNumber { get; set; }
        public string WardName { get; set; } = string.Empty;
        public int SponsorshipCount { get; set; }
        public double TotalCommittedAmount { get; set; }
        public double TotalPaidAmount { get; set; }
        public string RankBadge { get; set; } = string.Empty;
    }

    public class TopSponsoringFirmEntry
    {
        public int Position { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public double TotalAmount { get; set; }
        public double AmountPaid { get; set; }
        public double BalanceAmount { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string CollectedByName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    public class SponsorshipSummary
    {
        public int TotalSponsorships { get; set; }
        public double TotalCommittedAmount { get; set; }
        public double TotalPaidAmount { get; set; }
        public double TotalPendingBalance { get; set; }
        public int CompletedCount { get; set; }
        public int PartialCount { get; set; }
        public int BookedCount { get; set; }
    }

    public class SponsorshipLeaderboardResponse
    {
        public List<SponsorshipLeaderboardEntry> TopCollectors { get; set; } = new();
        public List<SponsorshipWardLeaderboardEntry> TopWards { get; set; } = new();
        public List<TopSponsoringFirmEntry> TopSponsoringFirms { get; set; } = new();
        public SponsorshipSummary Summary { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
