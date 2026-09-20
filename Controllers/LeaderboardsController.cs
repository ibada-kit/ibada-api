using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;

namespace ML.Charity.API.Client.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize] // All logged-in users can view the leaderboard
public class LeaderboardsController : ControllerBase
{
    private readonly ITableStorageService<DonationEntity> _donationRepository;
    private readonly ITableStorageService<UserEntity> _userRepository;

    public LeaderboardsController(
        ITableStorageService<DonationEntity> donationRepository,
        ITableStorageService<UserEntity> userRepository)
    {
        _donationRepository = donationRepository;
        _userRepository = userRepository;
    }

    [HttpGet]
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
                TotalAmount = g.Sum(d => d.TotalAmount),
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
                d.TotalAmount
            })
            .Where(x => x.WardNumber > 0)
            .GroupBy(x => x.WardNumber)
            .ToDictionary(g => g.Key, g => new
            {
                WardNumber = g.Key,
                TotalKits = g.Sum(x => x.KitCount),
                TotalAmount = g.Sum(x => x.TotalAmount),
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