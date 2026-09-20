using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ML.Charity.API.Client.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DonationsController : ControllerBase
    {
        private readonly ITableStorageService<DonationEntity> _donationRepository;
        private readonly ITableStorageService<UserEntity>? _userRepository;
        private readonly IWhatsAppService _whatsappService;
        private readonly ITableStorageService<CampaignSettingsEntity>? _settingsRepository;

        // Fallback default cost of one kit (1 Kit = 1000 INR)
        private const double PRICE_PER_KIT = 1000.0;

        public DonationsController(
            ITableStorageService<DonationEntity> donationRepository,
            ITableStorageService<UserEntity>? userRepository,
            IWhatsAppService whatsappService,
            ITableStorageService<CampaignSettingsEntity>? settingsRepository = null)
        {
            _donationRepository = donationRepository;
            _userRepository = userRepository;
            _whatsappService = whatsappService;
            _settingsRepository = settingsRepository;
        }

        private async Task<double> GetCurrentKitPriceAsync()
        {
            if (_settingsRepository != null)
            {
                var setting = await _settingsRepository.GetEntityAsync("GLOBAL", "KitPrice");
                if (setting != null && setting.KitPrice > 0)
                {
                    return setting.KitPrice;
                }
            }
            return PRICE_PER_KIT;
        }

        [HttpGet]
        public async Task<IActionResult> GetDonations()
        {
            var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst("sub")?.Value
                      ?? User.FindFirst("UserId")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value
                    ?? User.FindFirst("role")?.Value;
            var wardString = User.FindFirst("WardNumber")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Invalid user token. Missing required claims.");
            }

            int callerWard = 0;
            if (_userRepository != null)
            {
                var users = await _userRepository.QueryAsync(u => u.UserId == userId || u.RowKey == userId);
                var currentUser = users.FirstOrDefault();
                if (currentUser != null && !string.IsNullOrEmpty(currentUser.UserId))
                {
                    userId = currentUser.UserId;
                }
                callerWard = currentUser?.WardNumber ?? 0;
            }

            if (!string.IsNullOrEmpty(wardString) && int.TryParse(wardString, out int parsedWard))
            {
                callerWard = parsedWard;
            }

            var allDonations = await _donationRepository.QueryEntitiesAsync(null);
            IEnumerable<DonationEntity> filteredDonations;

            if (role == "Admin")
            {
                // Admin sees all donations across the entire campaign
                filteredDonations = allDonations;
            }
            else if (role == "WardCommittee")
            {
                // Ward Committee sees all donations for their assigned Ward
                filteredDonations = allDonations.Where(d => d.WardNumber == callerWard || d.UserId == userId);
            }
            else if (role == "Coordinator")
            {
                // Coordinator sees donations by themselves AND any volunteers under them, plus any in their ward
                var teamUserIds = new HashSet<string> { userId };
                if (_userRepository != null)
                {
                    var team = await _userRepository.QueryAsync(u => u.ParentUserId == userId || u.UserId == userId);
                    foreach (var member in team)
                    {
                        teamUserIds.Add(member.UserId);
                    }
                }

                filteredDonations = allDonations.Where(d => teamUserIds.Contains(d.UserId) 
                    || (callerWard > 0 && d.WardNumber == callerWard));
            }
            else
            {
                // Volunteer sees their own donations
                filteredDonations = allDonations.Where(d => d.UserId == userId);
            }

            // Order newest first
            var ordered = filteredDonations
                .OrderByDescending(d => d.TransactionDate != default ? d.TransactionDate : d.Timestamp?.DateTime ?? DateTime.MinValue)
                .ToList();

            // Load users to enrich with collector name & role
            var userDict = new Dictionary<string, UserEntity>();
            if (_userRepository != null)
            {
                var allUsers = await _userRepository.QueryEntitiesAsync(null);
                userDict = allUsers
                    .Where(u => !string.IsNullOrEmpty(u.UserId))
                    .GroupBy(u => u.UserId)
                    .ToDictionary(g => g.Key, g => g.First());
            }

            var result = ordered.Select(d =>
            {
                userDict.TryGetValue(d.UserId, out var collector);
                return new DonationResponse
                {
                    DonationId = d.DonationId,
                    ReceiptToken = d.RowKey,
                    DonorName = d.DonorName,
                    WhatsAppNumber = d.WhatsAppNumber,
                    KitCount = d.KitCount,
                    TotalAmount = d.TotalAmount,
                    Panchayath = d.PartitionKey,
                    WardNumber = d.WardNumber,
                    CollectedByUserId = d.UserId,
                    CollectedByName = collector?.FullName ?? "Volunteer",
                    CollectedByRole = collector?.Role ?? "Volunteer",
                    Timestamp = d.Timestamp ?? DateTimeOffset.UtcNow
                };
            });

            return Ok(result);
        }

        [HttpGet("my-donations")]
        public async Task<IActionResult> GetMyDonations()
        {
            var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst("sub")?.Value
                      ?? User.FindFirst("UserId")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Invalid user token. Missing required claims.");
            }

            var donations = await _donationRepository.QueryAsync(d => d.UserId == userId);
            return Ok(donations.OrderByDescending(d => d.TransactionDate != default ? d.TransactionDate : d.Timestamp?.DateTime ?? DateTime.MinValue));
        }

        [HttpGet("receipt/{receiptToken}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetReceipt(string receiptToken, [FromQuery] string panchayath = "Madavoor")
        {
            var donation = await _donationRepository.GetEntityAsync(panchayath, receiptToken);
            if (donation == null)
            {
                var queryList = await _donationRepository.QueryAsync(d => d.RowKey == receiptToken);
                donation = queryList.FirstOrDefault();
            }

            if (donation == null)
            {
                return NotFound("Receipt not found.");
            }

            return Ok(donation);
        }

        [HttpPost]
        public async Task<IActionResult> LogDonation([FromBody] CreateDonationRequest request)
        {
            var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst("sub")?.Value
                      ?? User.FindFirst("UserId")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var panchayath = User.FindFirst("Panchayath")?.Value ?? "Madavoor";
            var wardString = User.FindFirst("WardNumber")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Invalid user token. Missing required claims.");
            }

            int wardNumber = 0;
            if (!string.IsNullOrEmpty(wardString))
            {
                int.TryParse(wardString, out wardNumber);
            }

            // Calculate exact amount securely on the server using dynamic kit price from Azure Table Storage
            double currentKitPrice = await GetCurrentKitPriceAsync();
            double calculatedAmount = request.KitCount * currentKitPrice;

            // Generate Keys
            string shortId = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
            string receiptToken = $"MDV-{shortId}";
            string donationId = Guid.NewGuid().ToString();

            // Create the Entity matching DTO properties
            var donation = new DonationEntity
            {
                PartitionKey = panchayath,
                RowKey = receiptToken,
                DonationId = donationId,
                UserId = userId,
                WardNumber = wardNumber,
                DonorName = request.DonorName,
                WhatsAppNumber = request.WhatsAppNumber ?? string.Empty,
                KitCount = request.KitCount,
                TotalAmount = calculatedAmount,
                TransactionDate = DateTime.UtcNow,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _donationRepository.AddAsync(donation);

            // Send WhatsApp Receipt if option is enabled and phone number provided
            bool whatsAppSent = false;
            if (request.SendWhatsApp && !string.IsNullOrWhiteSpace(request.WhatsAppNumber))
            {
                try
                {
                    whatsAppSent = await _whatsappService.SendReceiptAsync(
                        phoneNumber: request.WhatsAppNumber,
                        donorName: request.DonorName,
                        kits: request.KitCount,
                        amount: calculatedAmount,
                        receiptToken: receiptToken
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] WhatsApp receipt failed for {receiptToken}: {ex.Message}");
                }
            }

            // Return the exact response format with collector info
            var callerName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Volunteer";
            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? "Volunteer";

            return Ok(new DonationResponse
            {
                DonationId = donationId,
                ReceiptToken = receiptToken,
                DonorName = request.DonorName,
                WhatsAppNumber = request.WhatsAppNumber ?? string.Empty,
                KitCount = request.KitCount,
                TotalAmount = calculatedAmount,
                Panchayath = panchayath,
                WardNumber = wardNumber,
                CollectedByUserId = userId,
                CollectedByName = callerName,
                CollectedByRole = callerRole,
                WhatsAppSent = whatsAppSent,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// On-demand endpoint allowing users to send or re-send a WhatsApp receipt message for any existing donation.
        /// </summary>
        [HttpPost("receipt/{receiptToken}/send-whatsapp")]
        public async Task<IActionResult> SendReceiptViaWhatsApp(
            string receiptToken, 
            [FromBody] SendWhatsAppReceiptRequest? request = null, 
            [FromQuery] string panchayath = "Madavoor")
        {
            var donation = await _donationRepository.GetEntityAsync(panchayath, receiptToken);
            if (donation == null)
            {
                var donations = await _donationRepository.QueryAsync(d => d.RowKey == receiptToken);
                donation = donations.FirstOrDefault();
            }

            if (donation == null)
            {
                return NotFound(new { message = "Receipt not found." });
            }

            var targetPhone = !string.IsNullOrWhiteSpace(request?.PhoneNumber)
                ? request.PhoneNumber
                : donation.WhatsAppNumber;

            if (string.IsNullOrWhiteSpace(targetPhone))
            {
                return BadRequest(new { message = "No valid WhatsApp phone number provided or recorded on this donation." });
            }

            try
            {
                var success = await _whatsappService.SendReceiptAsync(
                    phoneNumber: targetPhone,
                    donorName: donation.DonorName,
                    kits: donation.KitCount,
                    amount: donation.TotalAmount,
                    receiptToken: donation.RowKey
                );

                return Ok(new
                {
                    message = success ? "WhatsApp receipt dispatched successfully." : "WhatsApp receipt dispatch failed. Please check WhatsApp configuration or logs.",
                    receiptToken = donation.RowKey,
                    sentTo = targetPhone,
                    success
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"WhatsApp dispatch failed: {ex.Message}" });
            }
        }
    }
}