using Azure;
using Azure.Data.Tables;
using System;

namespace ML.Charity.API.Client.Models
{
    public class UserEntity : ITableEntity
    {
        // --- ITableEntity Implementation ---
        // PartitionKey is the Panchayath (e.g., "Madavoor") to group local users
        public string PartitionKey { get; set; } = string.Empty;

        // RowKey is the PhoneNumber (makes login lookups by phone number extremely fast)
        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // --- User Profile Properties ---
        public string UserId { get; set; } = Guid.NewGuid().ToString();
        public string FullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty; // Populated using BCrypt

        // Hierarchy & RBAC
        public string Role { get; set; } = string.Empty; // Admin, WardCommittee, Coordinator, Volunteer
        public string ParentUserId { get; set; } = string.Empty; // The user who created this account

        // Location Data
        public string District { get; set; } = string.Empty;
        public string Panchayath { get; set; } = string.Empty;
        public int WardNumber { get; set; }

        public string ProfilePhotoUrl { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        // Add these two lines to Models/UserEntity.cs
        public int TargetKits { get; set; } = 0;
        public double TargetAmount { get; set; } = 0;
    }
}