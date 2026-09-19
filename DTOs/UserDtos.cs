// --- UserDTOs.cs ---
using System.ComponentModel.DataAnnotations;

namespace ML.Charity.API.Client.DTOs
{
    // ==========================================
    // INCOMING REQUEST: Creating a new user
    // ==========================================
    public class CreateUserRequest
    {
        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Phone(ErrorMessage = "Invalid phone number format. Include country code.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^(WardCommittee|Coordinator|Volunteer)$", ErrorMessage = "Invalid Role.")]
        public string Role { get; set; } = string.Empty;

        [MinLength(6)]
        public string? DefaultPassword { get; set; }

        // Used by Admin when assigning a Volunteer to a specific Coordinator/Ward
        public string? AssignedParentUserId { get; set; }

        public string? District { get; set; }
        public string? Panchayath { get; set; }
        public int? WardNumber { get; set; }
        public int? TargetKits { get; set; }
    }

    public class UserResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public string Panchayath { get; set; } = string.Empty;
        public int WardNumber { get; set; }
        public string ParentUserId { get; set; } = string.Empty;
        public int TargetKits { get; set; }
        public double TargetAmount { get; set; }
        public int KitsCollected { get; set; }
        public double CollectedAmount { get; set; }
    }

    public class UpdateTargetRequest
    {
        [Required]
        [Range(1, 100000)]
        public int TargetKits { get; set; }
    }

    public class UpdateUserRequest
    {
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Role { get; set; }
        public int? WardNumber { get; set; }
        public string? District { get; set; }
        public string? Panchayath { get; set; }
        public int? TargetKits { get; set; }
        public string? NewPassword { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string? NewPassword { get; set; }
    }

    public class MyProgressResponse
    {
        public int TargetKits { get; set; }
        public double TargetAmount { get; set; }
        public int CollectedKits { get; set; }
        public double CollectedAmount { get; set; }
        public double AchievementPercentage { get; set; }
    }
}