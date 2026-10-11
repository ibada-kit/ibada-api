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
        { 12, "Arambram" }
    };

    private static readonly Dictionary<int, string> DefaultMadavoorWardMalayalamNames = new()
    {
        { 1, "അങ്കത്തായി" },
        { 2, "ഇരവണ്ണൂർ നോർത്ത്" },
        { 3, "ഇരവണ്ണൂർ സൗത്ത്" },
        { 4, "നരിയച്ചാൽ" },
        { 5, "പുല്ലാളൂർ" },
        { 6, "എരഞ്ഞിക്കുന്ന്" },
        { 7, "റാംപൊയിൽ" },
        { 8, "മടവൂർ" },
        { 9, "മടവൂർമുക്ക്" },
        { 10, "പൈമ്പാലശ്ശേരി" },
        { 11, "കോട്ടക്കവയൽ" },
        { 12, "ആരംബ്രം" }
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
    public async Task<IActionResult> GetPublicWardsLeaderboard()
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

        // 2. Identify Ward Committee Lead and Volunteers per ward
        var wardUsers = allUsers.Where(u => u.WardNumber > 0).GroupBy(u => u.WardNumber).ToDictionary(g => g.Key, g => g.ToList());

        // 3. Aggregate General Donations per Ward
        var donationsByWard = allDonations
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

        // 4. Aggregate Sponsorships per Ward
        var sponsorshipsByWard = allSponsorships
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

        // 5. Build rank list for standard 12 wards (and any additional recorded wards)
        var wardNumbers = Enumerable.Range(1, 12)
            .Union(donationsByWard.Keys)
            .Union(sponsorshipsByWard.Keys)
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

            int totalKits = (donStat?.TotalKits ?? 0) + (sponStat?.TotalKits ?? 0);
            
            // Count unique sponsors/donors (or fallback to transactions count)
            int donSponsors = donStat != null ? Math.Max(donStat.UniqueDonors, donStat.DonationTransactions) : 0;
            int sponSponsors = sponStat != null ? Math.Max(sponStat.UniqueSponsors, sponStat.SponsorshipTransactions) : 0;
            int totalSponsors = donSponsors + sponSponsors;

            // Ward authentic name
            string englishName = wardMap.TryGetValue(wardNum, out var wEnt) && !string.IsNullOrWhiteSpace(wEnt.WardName) && !wEnt.WardName.StartsWith("Ward ")
                ? wEnt.WardName
                : (DefaultMadavoorWardNames.TryGetValue(wardNum, out var defEn) ? defEn : $"Ward {wardNum}");

            string malayalamName = DefaultMadavoorWardMalayalamNames.TryGetValue(wardNum, out var defMl)
                ? defMl
                : englishName;

            double pct = targetKits > 0 ? Math.Round((double)totalKits / targetKits * 100, 1) : 0;

            publicWards.Add(new PublicWardRankDto
            {
                WardNumber = wardNum,
                WardName = englishName,
                WardNameMalayalam = malayalamName,
                WardLeadName = leadName,
                TotalKitsCollected = totalKits,
                SponsorsCount = totalSponsors,
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

        return Ok(new PublicWardsLeaderboardResponse
        {
            TotalKitsCollected = rankedWards.Sum(w => w.TotalKitsCollected),
            TotalSponsors = rankedWards.Sum(w => w.SponsorsCount),
            Wards = rankedWards,
            GeneratedAt = DateTime.UtcNow
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