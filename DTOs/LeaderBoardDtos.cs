namespace ML.Charity.API.Client.DTOs
{
    public class LeaderboardEntry
    {
        public int Position { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int WardNumber { get; set; }
        public string Panchayath { get; set; } = string.Empty;
        public int TotalKits { get; set; }
        public double TotalAmount { get; set; }
        public int DonationsCount { get; set; }
        public int TargetKits { get; set; }
        public int VolunteerCount { get; set; }
        public string RankBadge { get; set; } = string.Empty; // Gold, Silver, Bronze
    }

    public class LeaderboardResponse
    {
        public List<LeaderboardEntry> TopVolunteers { get; set; } = new();
        public List<LeaderboardEntry> TopWards { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// DTO for public anonymous wards display.
    /// Strictly contains only kit counts and counts of sponsors/donors.
    /// Excludes financial amounts, pending balances, and currency figures.
    /// </summary>
    public class PublicWardRankDto
    {
        public int Position { get; set; }
        public int WardNumber { get; set; }
        public string WardName { get; set; } = string.Empty;
        public string WardNameMalayalam { get; set; } = string.Empty;
        public string WardLeadName { get; set; } = string.Empty;
        public int TotalKitsCollected { get; set; }
        public int DonorsCount { get; set; }
        public int SponsorsCount { get; set; }
        public int VolunteerCount { get; set; }
        public int TargetKits { get; set; }
        public double AchievementPercentage { get; set; }
        public string RankBadge { get; set; } = string.Empty; // Gold, Silver, Bronze, Contributor
    }

    public class PublicVolunteerRankDto
    {
        public int Position { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int WardNumber { get; set; }
        public string WardName { get; set; } = string.Empty;
        public string WardLeadName { get; set; } = string.Empty;
        public int TotalKitsCollected { get; set; }
        public int DonorsCount { get; set; }
        public int SponsorsCount { get; set; }
        public int TargetKits { get; set; }
        public double AchievementPercentage { get; set; }
        public string RankBadge { get; set; } = string.Empty; // Gold, Silver, Bronze, Contributor
    }

    public class PublicWardsLeaderboardResponse
    {
        public int TotalKitsCollected { get; set; }
        public int TotalDonors { get; set; }
        public int TotalSponsors { get; set; }
        public List<PublicWardRankDto> Wards { get; set; } = new();
        public List<PublicVolunteerRankDto> TopVolunteers { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        // Date-based topper metadata
        public string? FilterPeriod { get; set; } // "all", "today", "week", "custom"
        public DateTime? FilterFromDate { get; set; }
        public DateTime? FilterToDate { get; set; }
        public bool IsFallbackToLastTransactionDate { get; set; }
        public DateTime? LastActiveTransactionDate { get; set; }
        public PublicWardRankDto? TopperWard { get; set; }
        public PublicVolunteerRankDto? TopperVolunteer { get; set; }
    }
}