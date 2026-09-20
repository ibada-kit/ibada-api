using System;
using System.ComponentModel.DataAnnotations;

namespace ML.Charity.API.Client.DTOs
{
    public class KitPriceResponse
    {
        public double KitPrice { get; set; }
        public DateTimeOffset ModifiedDate { get; set; }
        public string ModifiedBy { get; set; } = string.Empty;
    }

    public class UpdateKitPriceRequest
    {
        [Required]
        [Range(1, 1000000, ErrorMessage = "Kit price must be between 1 and 1,000,000 INR.")]
        public double KitPrice { get; set; }
    }
}
