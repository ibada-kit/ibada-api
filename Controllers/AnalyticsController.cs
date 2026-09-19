using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace ML.Charity.API.Client.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly ITableStorageService<UserEntity> _userRepository;
        private readonly ITableStorageService<DonationEntity> _donationRepository;

        public AnalyticsController(
            ITableStorageService<UserEntity> userRepository,
            ITableStorageService<DonationEntity> donationRepository)
        {
            _userRepository = userRepository;
            _donationRepository = donationRepository;
        }

        [HttpGet("my-progress")]
        public async Task<IActionResult> GetMyProgress()
        {
            var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst("sub")?.Value
                      ?? User.FindFirst("UserId")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var role = User.FindFirst(ClaimTypes.Role)?.Value
                    ?? User.FindFirst("role")?.Value;

            var wardString = User.FindFirst("WardNumber")?.Value;

            if (string.IsNullOrEmpty(userId)) return Unauthorized("User claim not found in token.");

            var users = await _userRepository.QueryAsync(u => u.UserId == userId || u.RowKey == userId);
            var currentUser = users.FirstOrDefault();

            if (currentUser == null) return NotFound("Current user profile not found.");
            userId = currentUser.UserId;

            int collectedKits = 0;
            double collectedAmount = 0;

            // 1. Calculate for Admin (Total Organization Global Progress - Admin has no targets)
            if (role == "Admin")
            {
                var allDonations = await _donationRepository.QueryAsync(d => d.TotalAmount > 0);
                collectedKits = allDonations.Sum(d => d.KitCount);
                collectedAmount = allDonations.Sum(d => d.TotalAmount);

                return Ok(new
                {
                    TargetKits = 0,
                    TargetAmount = 0.0,
                    CollectedKits = collectedKits,
                    CollectedAmount = collectedAmount,
                    AchievementPercentage = 0.0
                });
            }
            // 2. Calculate for Ward Committee (Sum of all donations in their specific Ward)
            else if (role == "WardCommittee")
            {
                int ward = currentUser.WardNumber;
                if (!string.IsNullOrEmpty(wardString) && int.TryParse(wardString, out int parsedWard))
                {
                    ward = parsedWard;
                }

                var wardDonations = await _donationRepository.QueryAsync(d => d.WardNumber == ward);
                collectedKits = wardDonations.Sum(d => d.KitCount);
                collectedAmount = wardDonations.Sum(d => d.TotalAmount);
            }
            // 3. Calculate for Coordinator (Sum of donations by them AND their assigned Volunteers)
            else if (role == "Coordinator")
            {
                // Find all users created by this coordinator, plus themselves
                var team = await _userRepository.QueryAsync(u => u.ParentUserId == userId || u.UserId == userId);
                var teamUserIds = team.Select(t => t.UserId).ToHashSet();
                teamUserIds.Add(userId);

                // Fetch all donations and filter in memory by the team's UserIds
                var allDonations = await _donationRepository.QueryAsync(d => d.TotalAmount > 0);
                var teamDonations = allDonations.Where(d => teamUserIds.Contains(d.UserId));

                collectedKits = teamDonations.Sum(d => d.KitCount);
                collectedAmount = teamDonations.Sum(d => d.TotalAmount);
            }
            // 4. Calculate for Volunteer
            else
            {
                var myDonations = await _donationRepository.QueryAsync(d => d.UserId == userId);
                collectedKits = myDonations.Sum(d => d.KitCount);
                collectedAmount = myDonations.Sum(d => d.TotalAmount);
            }

            int targetKits = currentUser.TargetKits;
            double targetAmount = currentUser.TargetAmount;

            // Calculate percentage safely to avoid dividing by zero
            double percentage = targetAmount > 0
                ? Math.Round((collectedAmount / targetAmount) * 100, 1)
                : 0;

            return Ok(new MyProgressResponse
            {
                TargetKits = targetKits,
                TargetAmount = targetAmount,
                CollectedKits = collectedKits,
                CollectedAmount = collectedAmount,
                AchievementPercentage = percentage
            });
        }
    }
}