using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;

namespace ML.Charity.API.Client.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LeaderboardsController : ControllerBase
{
    private const string DEFAULT_PANCHAYATH = "Madavoor";
    private readonly ITableStorageService<DonationEntity> _donationRepository;
    private readonly ITableStorageService<UserEntity> _userRepository;
    private readonly ITableStorageService<WardEntity>? _wardRepository;
    private readonly ITableStorageService<SponsorshipEntity>? _sponsorshipRepository;

    private static readonly Dictionary<int, string> DefaultMadavoorWardNames = new()
    {
        { 1, "Ankathayi" },
        { 2, "Eravannur North" },
        { 3, "Eravannur South" },
        { 4, "Nariyachal" },
        { 5, "Pullaloor" },
        { 6, "Eranhukunnu" },
        { 7, "Rampoyil" },
        { 8, "Madavoor" },
        { 9, "Madavoormukku" },
        { 10, "Paimbalassery" },
        { 11, "Kottakkavayal" },
        { 12, "Arambram" },
        { 13, "Chakkalakkal" },
        { 14, "Arambram West" },
        { 15, "Pullikkoth" },
        { 16, "Arankilthazham" },
        { 17, "Muttanchery" },
        { 18, "Pullorammal" },
        { 19, "Chathanarambu" }
    };


    public LeaderboardsController(
        ITableStorageService<DonationEntity> donationRepository,
        ITableStorageService<UserEntity> userRepository,
        ITableStorageService<WardEntity>? wardRepository = null,
        ITableStorageService<SponsorshipEntity>? sponsorshipRepository = null)
    {
        _donationRepository = donationRepository;
        _userRepository = userRepository;
        _wardRepository = wardRepository;
        _sponsorshipRepository = sponsorshipRepository;
    }

    private static double GetEffectiveAmountPaid(DonationEntity d)
    {
        if (d.AmountPaid > 0 || d.BalanceAmount > 0)
        {
            return d.AmountPaid;
        }
        if (string.Equals(d.PaymentStatus, "Booked", StringComparison.OrdinalIgnoreCase))
        {
            return 0.0;
        }
        return d.TotalAmount;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetLeaderboard()
    {
        // 1. Fetch all active donations and users
        var allDonations = (await _donationRepository.QueryEntitiesAsync(null)) 
            ?? (await _donationRepository.QueryAsync(d => true)) 
            ?? new List<DonationEntity>();
        var allUsers = (await _userRepository.QueryEntitiesAsync(null)) 
            ?? (await _userRepository.QueryAsync(u => true)) 
            ?? new List<UserEntity>();
        var userMap = allUsers.Where(u => !string.IsNullOrEmpty(u.UserId)).ToDictionary(u => u.UserId, u => u);

        // 2. Aggregate Top Volunteers / Fundraisers (real counts, no dummy estimates)
        var volunteerStats = allDonations
            .Where(d => !string.IsNullOrEmpty(d.UserId))
            .GroupBy(d => d.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalKits = g.Sum(d => d.KitCount),
                TotalAmount = g.Sum(GetEffectiveAmountPaid),
                DonationsCount = g.Count()
            })
            .OrderByDescending(v => v.TotalKits)
            .ToList();

        var topVolunteers = new List<LeaderboardEntry>();
        int position = 1;

        foreach (var stat in volunteerStats)
        {
            userMap.TryGetValue(stat.UserId, out var user);

            topVolunteers.Add(new LeaderboardEntry
            {
                Position = position,
                UserId = stat.UserId,
                Name = user?.FullName ?? "Fundraiser",
                Role = user?.Role ?? "Volunteer",
                WardNumber = user?.WardNumber ?? 0,
                Panchayath = user?.Panchayath ?? "Madavoor",
                TotalKits = stat.TotalKits,
                TotalAmount = stat.TotalAmount,
                DonationsCount = stat.DonationsCount,
                TargetKits = user?.TargetKits ?? 0,
                RankBadge = AssignBadge(position)
            });
            position++;
        }

        // 3. Aggregate Top Wards
        // Business Rule: A ward's relief target is fixed and defined by the Ward Committee Lead.
        // Volunteers assigned to the ward share this target rather than accumulating into or inflating it.
        var wardTargets = allUsers
            .Where(u => u.WardNumber > 0)
            .GroupBy(u => u.WardNumber)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    // Primary: Authoritative Ward Committee Lead target
                    var lead = g.FirstOrDefault(u => u.Role == "WardCommittee" && u.TargetKits > 0);
                    if (lead != null)
                        return lead.TargetKits;

                    // Secondary: Assigned Coordinator target if any
                    var coord = g.FirstOrDefault(u => u.Role == "Coordinator" && u.TargetKits > 0);
                    if (coord != null)
                        return coord.TargetKits;

                    // Fallback: If only volunteers exist with no lead, use sum of their targets
                    return g.Where(u => u.Role == "Volunteer").Sum(u => u.TargetKits);
                });

        var wardVolunteers = allUsers
            .Where(u => u.WardNumber > 0 && u.Role == "Volunteer")
            .GroupBy(u => u.WardNumber)
            .ToDictionary(g => g.Key, g => g.Count());

        var wardStatsMap = allDonations
            .Select(d => new
            {
                WardNumber = d.WardNumber > 0 ? d.WardNumber : (userMap.TryGetValue(d.UserId, out var u) ? u.WardNumber : 0),
                d.KitCount,
                Amount = GetEffectiveAmountPaid(d)
            })
            .Where(x => x.WardNumber > 0)
            .GroupBy(x => x.WardNumber)
            .ToDictionary(g => g.Key, g => new
            {
                WardNumber = g.Key,
                TotalKits = g.Sum(x => x.KitCount),
                TotalAmount = g.Sum(x => x.Amount),
                DonationsCount = g.Count()
            });

        // Include all wards that have recorded donations OR have assigned users/targets
        var allWardNumbers = wardStatsMap.Keys
            .Union(wardTargets.Keys)
            .Where(w => w > 0)
            .Distinct()
            .ToList();

        var topWards = allWardNumbers
            .Select(w =>
            {
                wardStatsMap.TryGetValue(w, out var stat);
                int totalKits = stat?.TotalKits ?? 0;
                double totalAmount = stat?.TotalAmount ?? 0.0;
                int donationsCount = stat?.DonationsCount ?? 0;
                int target = wardTargets.TryGetValue(w, out int t) ? t : 0;
                int volCount = wardVolunteers.TryGetValue(w, out int vc) ? vc : 0;

                return new
                {
                    WardNumber = w,
                    TotalKits = totalKits,
                    TotalAmount = totalAmount,
                    DonationsCount = donationsCount,
                    TargetKits = target,
                    VolunteerCount = volCount
                };
            })
            .OrderByDescending(w => w.TotalKits)
            .ThenBy(w => w.WardNumber)
            .Select((w, idx) => new LeaderboardEntry
            {
                Position = idx + 1,
                WardNumber = w.WardNumber,
                Name = $"Ward {w.WardNumber}",
                TotalKits = w.TotalKits,
                TotalAmount = w.TotalAmount,
                DonationsCount = w.DonationsCount,
                TargetKits = w.TargetKits,
                VolunteerCount = w.VolunteerCount,
                RankBadge = AssignBadge(idx + 1)
            })
            .ToList();

        // 4. Return the combined payload
        return Ok(new LeaderboardResponse
        {
            TopVolunteers = topVolunteers.Take(100).ToList(),
            TopWards = topWards,
            GeneratedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Dedicated public leaderboard for Wards display.
    /// Strictly Anonymous access, displays ONLY kit counts and sponsors count.
    /// No financial amounts, balances, or currency figures are returned.
    /// </summary>
    [HttpGet("public-wards")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicWardsLeaderboard(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? period = null)
    {
        // 1. Fetch all active donations, users, wards, and sponsorships
        var allDonations = (await _donationRepository.QueryEntitiesAsync(null))
            ?? (await _donationRepository.QueryAsync(d => true))
            ?? new List<DonationEntity>();

        var allUsers = (await _userRepository.QueryEntitiesAsync(null))
            ?? (await _userRepository.QueryAsync(u => true))
            ?? new List<UserEntity>();

        var userMap = allUsers
            .Where(u => !string.IsNullOrEmpty(u.UserId))
            .GroupBy(u => u.UserId)
            .ToDictionary(g => g.Key, g => g.First());

        List<WardEntity> allWards = new();
        if (_wardRepository != null)
        {
            try
            {
                allWards = (await _wardRepository.QueryEntitiesByPartitionAsync(DEFAULT_PANCHAYATH)) ?? new();
            }
            catch
            {
                // Fall back gracefully
            }
        }
        var wardMap = allWards.GroupBy(w => w.WardNumber).ToDictionary(g => g.Key, g => g.First());

        List<SponsorshipEntity> allSponsorships = new();
        if (_sponsorshipRepository != null)
        {
            try
            {
                allSponsorships = (await _sponsorshipRepository.QueryEntitiesAsync(null)) ?? new();
            }
            catch
            {
                // Fall back gracefully
            }
        }

        // 2. Identify the most recent transaction date across all donations
        DateTime? lastActiveTransactionDate = allDonations
            .Select(d => (DateTime?)(d.TransactionDate != default ? d.TransactionDate : d.Timestamp?.UtcDateTime))
            .Where(dt => dt.HasValue)
            .OrderByDescending(dt => dt!.Value)
            .FirstOrDefault();

        DateTime? filterFrom = null;
        DateTime? filterTo = null;
        bool isFallback = false;
        string effectivePeriod = string.IsNullOrWhiteSpace(period) 
            ? (fromDate.HasValue || toDate.HasValue ? "custom" : "all") 
            : period.Trim().ToLowerInvariant();

        if (effectivePeriod == "today" || effectivePeriod == "current-day")
        {
            var todayUtc = DateTime.UtcNow.Date;
            var todayEndUtc = todayUtc.AddDays(1).AddTicks(-1);
            bool hasToday = allDonations.Any(d =>
            {
                var dt = d.TransactionDate != default ? d.TransactionDate : (d.Timestamp?.UtcDateTime ?? DateTime.MinValue);
                return dt >= todayUtc && dt <= todayEndUtc;
            });

            if (hasToday)
            {
                filterFrom = todayUtc;
                filterTo = todayEndUtc;
            }
            else if (lastActiveTransactionDate.HasValue)
            {
                // Fall back to whole full 24h day of the last transaction!
                filterFrom = lastActiveTransactionDate.Value.Date;
                filterTo = filterFrom.Value.AddDays(1).AddTicks(-1);
                isFallback = true;
            }
            else
            {
                filterFrom = todayUtc;
                filterTo = todayEndUtc;
            }
        }
        else if (effectivePeriod == "week" || effectivePeriod == "weekly" || effectivePeriod == "this-week")
        {
            var refDate = lastActiveTransactionDate ?? DateTime.UtcNow;
            filterTo = refDate.Date.AddDays(1).AddTicks(-1);
            filterFrom = refDate.Date.AddDays(-6);
        }
        else if (fromDate.HasValue || toDate.HasValue)
        {
            effectivePeriod = "custom";
            filterFrom = fromDate?.Date;
            filterTo = toDate?.Date.AddDays(1).AddTicks(-1);
            if (filterFrom.HasValue && !filterTo.HasValue)
            {
                filterTo = filterFrom.Value.AddDays(1).AddTicks(-1);
            }
            else if (!filterFrom.HasValue && filterTo.HasValue)
            {
                filterFrom = filterTo.Value.Date;
            }
        }

        // Apply date filtering to donations and sponsorships for ranking
        var donationsForRanking = allDonations;
        if (filterFrom.HasValue || filterTo.HasValue)
        {
            donationsForRanking = allDonations.Where(d =>
            {
                var dt = d.TransactionDate != default ? d.TransactionDate : (d.Timestamp?.UtcDateTime ?? DateTime.MinValue);
                if (filterFrom.HasValue && dt < filterFrom.Value) return false;
                if (filterTo.HasValue && dt > filterTo.Value) return false;
                return true;
            }).ToList();
        }

        var sponsorshipsForRanking = allSponsorships;
        if (filterFrom.HasValue || filterTo.HasValue)
        {
            sponsorshipsForRanking = allSponsorships.Where(s =>
            {
                var dt = s.CreatedDate != default ? s.CreatedDate : (s.Timestamp?.UtcDateTime ?? DateTime.MinValue);
                if (filterFrom.HasValue && dt < filterFrom.Value) return false;
                if (filterTo.HasValue && dt > filterTo.Value) return false;
                return true;
            }).ToList();
        }

        // 3. Identify Ward Committee Lead and Volunteers per ward
        var wardUsers = allUsers.Where(u => u.WardNumber > 0).GroupBy(u => u.WardNumber).ToDictionary(g => g.Key, g => g.ToList());

        // 4. Aggregate General Donations per Ward
        var donationsByWard = donationsForRanking
            .Select(d => new
            {
                Ward = d.WardNumber > 0 ? d.WardNumber : (userMap.TryGetValue(d.UserId, out var u) ? u.WardNumber : 0),
                d.KitCount,
                Donor = d.DonorName?.Trim() ?? string.Empty
            })
            .Where(x => x.Ward > 0)
            .GroupBy(x => x.Ward)
            .ToDictionary(g => g.Key, g => new
            {
                TotalKits = g.Sum(x => x.KitCount),
                UniqueDonors = g.Select(x => x.Donor).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count(),
                DonationTransactions = g.Count()
            });

        // 5. Aggregate Sponsorships per Ward
        var sponsorshipsByWard = sponsorshipsForRanking
            .Select(s => new
            {
                Ward = s.WardNumber > 0 ? s.WardNumber : (userMap.TryGetValue(s.CollectedByUserId, out var u) ? u.WardNumber : 0),
                Kits = s.Quantity,
                Donor = s.DonorName?.Trim() ?? string.Empty
            })
            .Where(x => x.Ward > 0)
            .GroupBy(x => x.Ward)
            .ToDictionary(g => g.Key, g => new
            {
                TotalKits = g.Sum(x => x.Kits),
                UniqueSponsors = g.Select(x => x.Donor).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count(),
                SponsorshipTransactions = g.Count()
            });

        // 6. Build rank list for all wards (Wards 1 through 19 and any recorded wards)
        var wardNumbers = allWards.Select(w => w.WardNumber)
            .Union(donationsByWard.Keys)
            .Union(sponsorshipsByWard.Keys)
            .Union(wardUsers.Keys)
            .Union(Enumerable.Range(1, 19))
            .Where(w => w > 0)
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        var publicWards = new List<PublicWardRankDto>();

        foreach (var wardNum in wardNumbers)
        {
            wardUsers.TryGetValue(wardNum, out var members);
            members ??= new List<UserEntity>();

            // Authoritative Ward Lead: WardCommittee role first, then Coordinator
            var lead = members.FirstOrDefault(u => u.Role == "WardCommittee")
                    ?? members.FirstOrDefault(u => u.Role == "Coordinator");
            var leadName = !string.IsNullOrWhiteSpace(lead?.FullName) ? lead.FullName : "Ward Committee Lead";

            int volunteerCount = members.Count(u => u.Role == "Volunteer");

            // Targets: Derived from Ward Committee Lead (to avoid double-counting)
            int targetKits = lead?.TargetKits > 0 
                ? lead.TargetKits 
                : members.Where(u => u.Role == "Coordinator").Sum(u => u.TargetKits);
            if (targetKits <= 0)
            {
                targetKits = members.Where(u => u.Role == "Volunteer").Sum(u => u.TargetKits);
            }

            donationsByWard.TryGetValue(wardNum, out var donStat);
            sponsorshipsByWard.TryGetValue(wardNum, out var sponStat);

            // PURE RELIEF KITS COUNT: Derived strictly from Donations (1 Kit = 1 Relief Kit)
            int totalKits = donStat?.TotalKits ?? 0;
            
            // STRICT SEPARATION:
            // Donors = Individual relief kit contributors (from Donations)
            // Sponsors = Organization/firm item sponsors (from Sponsorships)
            int donDonors = donStat != null ? Math.Max(donStat.UniqueDonors, donStat.DonationTransactions) : 0;
            int sponSponsors = sponStat != null ? Math.Max(sponStat.UniqueSponsors, sponStat.SponsorshipTransactions) : 0;

            // Ward authentic name
            string englishName = wardMap.TryGetValue(wardNum, out var wEnt) && !string.IsNullOrWhiteSpace(wEnt.WardName) && !wEnt.WardName.StartsWith("Ward ")
                ? wEnt.WardName
                : (DefaultMadavoorWardNames.TryGetValue(wardNum, out var defEn) ? defEn : $"Ward {wardNum}");

            double pct = targetKits > 0 ? Math.Round((double)totalKits / targetKits * 100, 1) : 0;

            publicWards.Add(new PublicWardRankDto
            {
                WardNumber = wardNum,
                WardName = englishName,
                WardNameMalayalam = string.Empty,
                WardLeadName = leadName,
                TotalKitsCollected = totalKits,
                DonorsCount = donDonors,
                SponsorsCount = sponSponsors,
                VolunteerCount = volunteerCount,
                TargetKits = targetKits,
                AchievementPercentage = pct
            });
        }

        // Rank strictly based on total kit sponsored / collected descending
        var rankedWards = publicWards
            .OrderByDescending(w => w.TotalKitsCollected)
            .ThenBy(w => w.WardNumber)
            .Select((w, idx) =>
            {
                w.Position = idx + 1;
                w.RankBadge = AssignBadge(idx + 1);
                return w;
            })
            .ToList();

        // Compute Top 10 Volunteers as per ranking
        var topVolunteersStats = donationsForRanking
            .Where(d => !string.IsNullOrEmpty(d.UserId))
            .GroupBy(d => d.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalKits = g.Sum(d => d.KitCount),
                DonationsCount = g.Count(),
                UniqueDonors = g.Select(d => d.DonorName?.Trim()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count()
            })
            .OrderByDescending(v => v.TotalKits)
            .Take(10)
            .ToList();

        var volunteerSponsorships = sponsorshipsForRanking
            .Where(s => !string.IsNullOrEmpty(s.CollectedByUserId))
            .GroupBy(s => s.CollectedByUserId)
            .ToDictionary(g => g.Key, g => g.Count());

        var rankedVolunteers = new List<PublicVolunteerRankDto>();
        int volPos = 1;

        foreach (var stat in topVolunteersStats)
        {
            userMap.TryGetValue(stat.UserId, out var user);
            int vWardNum = user?.WardNumber ?? 0;
            string vWardName = vWardNum > 0
                ? (wardMap.TryGetValue(vWardNum, out var wEnt) && !string.IsNullOrWhiteSpace(wEnt.WardName) && !wEnt.WardName.StartsWith("Ward ")
                    ? wEnt.WardName
                    : (DefaultMadavoorWardNames.TryGetValue(vWardNum, out var defEn) ? defEn : $"Ward {vWardNum}"))
                : "No Ward Assigned";

            string vLeadName = "No Ward Lead";
            if (vWardNum > 0 && wardUsers.TryGetValue(vWardNum, out var wardMembers))
            {
                var lead = wardMembers.FirstOrDefault(u => u.Role == "WardCommittee" || u.Role == "WardLead")
                           ?? wardMembers.FirstOrDefault(u => u.Role == "Coordinator");
                if (lead != null && !string.IsNullOrWhiteSpace(lead.FullName))
                {
                    vLeadName = lead.FullName;
                }
            }

            int targetKits = user?.TargetKits ?? 0;
            double pct = targetKits > 0 ? Math.Round((double)stat.TotalKits / targetKits * 100, 1) : 0;
            int volDonors = Math.Max(stat.UniqueDonors, stat.DonationsCount);
            int volSponsors = volunteerSponsorships.TryGetValue(stat.UserId, out int sc) ? sc : 0;

            rankedVolunteers.Add(new PublicVolunteerRankDto
            {
                Position = volPos,
                UserId = stat.UserId,
                Name = user?.FullName ?? "Volunteer",
                Role = user?.Role ?? "Volunteer",
                WardNumber = vWardNum,
                WardName = vWardName,
                WardLeadName = vLeadName,
                TotalKitsCollected = stat.TotalKits,
                DonorsCount = volDonors,
                SponsorsCount = volSponsors,
                TargetKits = targetKits,
                AchievementPercentage = pct,
                RankBadge = AssignBadge(volPos)
            });
            volPos++;
        }

        var topperWard = rankedWards.FirstOrDefault(w => w.TotalKitsCollected > 0) ?? rankedWards.FirstOrDefault();
        var topperVolunteer = rankedVolunteers.FirstOrDefault(v => v.TotalKitsCollected > 0) ?? rankedVolunteers.FirstOrDefault();

        return Ok(new PublicWardsLeaderboardResponse
        {
            TotalKitsCollected = rankedWards.Sum(w => w.TotalKitsCollected),
            TotalDonors = rankedWards.Sum(w => w.DonorsCount),
            TotalSponsors = rankedWards.Sum(w => w.SponsorsCount),
            Wards = rankedWards,
            TopVolunteers = rankedVolunteers,
            GeneratedAt = DateTime.UtcNow,
            FilterPeriod = effectivePeriod,
            FilterFromDate = filterFrom,
            FilterToDate = filterTo,
            IsFallbackToLastTransactionDate = isFallback,
            LastActiveTransactionDate = lastActiveTransactionDate,
            TopperWard = topperWard,
            TopperVolunteer = topperVolunteer
        });
    }

    // =======================================================
    // HELPER METHOD: Assigns visual ranks based on position
    // =======================================================
    private static string AssignBadge(int position)
    {
        return position switch
        {
            1 => "Gold",
            2 => "Silver",
            3 => "Bronze",
            _ => "Contributor"
        };
    }
}