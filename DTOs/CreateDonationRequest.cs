using System.ComponentModel.DataAnnotations;

namespace ML.Charity.API.Client.DTOs
{
    public class CreateDonationRequest
    {
        [Required]
        public string DonorName { get; set; } = string.Empty;

        [Phone] // Valid phone number format
        public string WhatsAppNumber { get; set; } = string.Empty;

        [Range(0, 1000)]
        public int KitCount { get; set; }

        public int GeneralKitCount { get; set; }
        public int StudentKitCount { get; set; }
        public string? KitStyle { get; set; }

        /// <summary>
        /// Option to send receipt via WhatsApp. Defaults to true.
        /// If false, WhatsApp message will not be sent.
        /// </summary>
        public bool SendWhatsApp { get; set; } = true;

        // Payment Options: PayFull, Book, Advance
        public string PaymentOption { get; set; } = "PayFull";
        public double? InitialAmountPaid { get; set; }
        public string? PaymentMode { get; set; } = "Cash";
        public string? TransactionReference { get; set; }
        public string? Notes { get; set; }
    }

    public class SendWhatsAppReceiptRequest
    {
        /// <summary>
        /// Optional destination phone number. If omitted, the phone number recorded with the donation is used.
        /// </summary>
        [Phone]
        public string? PhoneNumber { get; set; }
    }

    public class UpdateDonationPaymentRequest
    {
        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Payment amount must be greater than zero.")]
        public double AmountToPay { get; set; }

        public string? PaymentMode { get; set; } = "Cash";
        public string? TransactionReference { get; set; }
        public string? Notes { get; set; }
    }

    public class DonationResponse
    {
        public string DonationId { get; set; } = string.Empty;
        public string ReceiptToken { get; set; } = string.Empty;
        public int SerialNumber { get; set; }
        public string DonorName { get; set; } = string.Empty;
        public string WhatsAppNumber { get; set; } = string.Empty;
        public int KitCount { get; set; }
        public int GeneralKitCount { get; set; }
        public int StudentKitCount { get; set; }
        public string KitStyle { get; set; } = "General";
        public double TotalAmount { get; set; }
        public string Panchayath { get; set; } = string.Empty;
        public int WardNumber { get; set; }
        public string CollectedByUserId { get; set; } = string.Empty;
        public string CollectedByName { get; set; } = string.Empty;
        public string CollectedByRole { get; set; } = string.Empty;

        // Payment Details
        public string PaymentOption { get; set; } = "PayFull";
        public double AmountPaid { get; set; }
        public double BalanceAmount { get; set; }
        public string PaymentStatus { get; set; } = "Completed";
        public string PaymentMode { get; set; } = "Cash";
        public string TransactionReference { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime? UpdateDate { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;

        public bool WhatsAppSent { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}