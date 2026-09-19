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
}