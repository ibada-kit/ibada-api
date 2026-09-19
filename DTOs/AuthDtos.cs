// --- AuthDTOs.cs ---
using System.ComponentModel.DataAnnotations;

namespace ML.Charity.API.Client.DTOs
{
    public class LoginRequest
    {
        [Required(ErrorMessage = "Phone number is required.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        [Required]
        public string OldPassword { get; set; } = string.Empty;

        [Required]
        [MinLength(6, ErrorMessage = "New password must be at least 6 characters.")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool NeedsPasswordChange { get; set; } // Flag to force password change
        public DateTime ExpiresAt { get; set; }
    }
}