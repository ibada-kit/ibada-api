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

        public const string SERIAL_COUNTER_ROW = "DonationSerialCounter";
        public const int INITIAL_SERIAL_NUMBER = 10000;
        private static readonly SemaphoreSlim _serialCounterLock = new SemaphoreSlim(1, 1);
        private static int _fallbackNextSerial = INITIAL_SERIAL_NUMBER;

        public static void ResetFallbackSerial(int start = INITIAL_SERIAL_NUMBER)
        {
            _fallbackNextSerial = start;
        }

        private async Task<int> GenerateNextSerialNumberAsync()
        {
            await _serialCounterLock.WaitAsync();
            try
            {
                int serialToAssign = INITIAL_SERIAL_NUMBER;

                if (_settingsRepository != null)
                {
                    CampaignSettingsEntity? counterSetting = null;
                    try
                    {
                        counterSetting = await _settingsRepository.GetEntityAsync("GLOBAL", SERIAL_COUNTER_ROW);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Failed to fetch {SERIAL_COUNTER_ROW} from Azure Table: {ex.Message}");
                    }

                    if (counterSetting != null && counterSetting.NextSerialNumber >= INITIAL_SERIAL_NUMBER)
                    {
                        serialToAssign = counterSetting.NextSerialNumber;
                    }
                    else
                    {
                        // Check if any existing donations in DB have a SerialNumber >= 10000
                        try
                        {
                            var existingDonations = await _donationRepository.QueryAsync(d => d.SerialNumber >= INITIAL_SERIAL_NUMBER);
                            if (existingDonations != null && existingDonations.Any())
                            {
                                serialToAssign = existingDonations.Max(d => d.SerialNumber) + 1;
                            }
                        }
                        catch
                        {
                            serialToAssign = INITIAL_SERIAL_NUMBER;
                        }
                    }

                    // Save the incremented counter to Azure Table
                    var entityToSave = counterSetting ?? new CampaignSettingsEntity
                    {
                        PartitionKey = "GLOBAL",
                        RowKey = SERIAL_COUNTER_ROW
                    };
                    entityToSave.NextSerialNumber = serialToAssign + 1;
                    entityToSave.ModifiedDate = DateTimeOffset.UtcNow;
                    entityToSave.ModifiedBy = "System";

                    try
                    {
                        await _settingsRepository.UpsertAsync(entityToSave);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Failed to upsert {SERIAL_COUNTER_ROW} to Azure Table: {ex.Message}");
                    }
                }
                else
                {
                    if (_fallbackNextSerial < INITIAL_SERIAL_NUMBER)
                    {
                        _fallbackNextSerial = INITIAL_SERIAL_NUMBER;
                    }
                    serialToAssign = _fallbackNextSerial++;
                }

                return serialToAssign;
            }
            finally
            {
                _serialCounterLock.Release();
            }
        }

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

            if (callerWard == 0 && !string.IsNullOrEmpty(wardString) && int.TryParse(wardString, out int parsedWard))
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
                return MapToResponse(d, collector);
            });

            return Ok(result);
        }

        private DonationResponse MapToResponse(DonationEntity d, UserEntity? collector = null)
        {
            // Backwards compatibility fallback for older records
            string paymentOption = !string.IsNullOrEmpty(d.PaymentOption) ? d.PaymentOption : "PayFull";
            string paymentStatus = !string.IsNullOrEmpty(d.PaymentStatus) ? d.PaymentStatus : "Completed";
            double amountPaid = d.AmountPaid > 0 || d.BalanceAmount > 0 ? d.AmountPaid : d.TotalAmount;
            double balanceAmount = d.BalanceAmount;
            string paymentMode = !string.IsNullOrEmpty(d.PaymentMode) ? d.PaymentMode : "Cash";

            return new DonationResponse
            {
                DonationId = d.DonationId,
                ReceiptToken = d.RowKey,
                SerialNumber = d.SerialNumber,
                DonorName = d.DonorName,
                WhatsAppNumber = d.WhatsAppNumber,
                KitCount = d.KitCount,
                TotalAmount = d.TotalAmount,
                Panchayath = d.PartitionKey,
                WardNumber = d.WardNumber,
                CollectedByUserId = d.UserId,
                CollectedByName = collector?.FullName ?? "Volunteer",
                CollectedByRole = collector?.Role ?? "Volunteer",
                PaymentOption = paymentOption,
                AmountPaid = amountPaid,
                BalanceAmount = balanceAmount,
                PaymentStatus = paymentStatus,
                PaymentMode = paymentMode,
                TransactionReference = d.TransactionReference ?? string.Empty,
                Notes = d.Notes ?? string.Empty,
                UpdateDate = d.UpdateDate != default ? d.UpdateDate : d.TransactionDate,
                UpdatedBy = d.UpdatedBy ?? string.Empty,
                Timestamp = d.Timestamp ?? DateTimeOffset.UtcNow
            };
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
            var list = donations.OrderByDescending(d => d.TransactionDate != default ? d.TransactionDate : d.Timestamp?.DateTime ?? DateTime.MinValue).ToList();
            foreach (var d in list)
            {
                if (string.IsNullOrEmpty(d.PaymentStatus))
                {
                    d.PaymentOption = "PayFull";
                    d.PaymentStatus = "Completed";
                    d.AmountPaid = d.TotalAmount;
                    d.BalanceAmount = 0.0;
                    d.PaymentMode = "Cash";
                }
            }
            return Ok(list);
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

            // Fallback for legacy records
            if (string.IsNullOrEmpty(donation.PaymentStatus))
            {
                donation.PaymentOption = "PayFull";
                donation.PaymentStatus = "Completed";
                donation.AmountPaid = donation.TotalAmount;
                donation.BalanceAmount = 0.0;
                donation.PaymentMode = "Cash";
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
            if (_userRepository != null)
            {
                var users = await _userRepository.QueryAsync(u => u.UserId == userId || u.RowKey == userId);
                var currentUser = users.FirstOrDefault();
                if (currentUser != null && currentUser.WardNumber > 0)
                {
                    wardNumber = currentUser.WardNumber;
                }
            }

            if (wardNumber == 0 && !string.IsNullOrEmpty(wardString) && int.TryParse(wardString, out int parsedWard))
            {
                wardNumber = parsedWard;
            }

            // Calculate exact amount securely on the server using dynamic kit price from Azure Table Storage
            double currentKitPrice = await GetCurrentKitPriceAsync();
            double calculatedAmount = request.KitCount * currentKitPrice;

            // Process Payment Option: PayFull, Book, Advance
            double amountPaid;
            double balanceAmount;
            string paymentStatus;
            string paymentOption = string.IsNullOrWhiteSpace(request.PaymentOption) ? "PayFull" : request.PaymentOption.Trim();

            if (request.InitialAmountPaid.HasValue && request.InitialAmountPaid.Value < 0)
            {
                return BadRequest(new { message = "Payment amount cannot be negative." });
            }

            switch (paymentOption)
            {
                case "PayFull":
                    amountPaid = calculatedAmount;
                    balanceAmount = 0.0;
                    paymentStatus = "Completed";
                    break;

                case "Book":
                    amountPaid = request.InitialAmountPaid ?? 0.0;
                    if (amountPaid < 0)
                    {
                        return BadRequest(new { message = "Payment amount cannot be negative." });
                    }
                    if (amountPaid > calculatedAmount)
                    {
                        return BadRequest(new { message = $"Initial booking amount ({amountPaid}) cannot exceed total amount ({calculatedAmount})." });
                    }
                    balanceAmount = calculatedAmount - amountPaid;
                    paymentStatus = balanceAmount == 0 ? "Completed" : (amountPaid > 0 ? "Partial" : "Booked");
                    break;

                case "Advance":
                    if (!request.InitialAmountPaid.HasValue || request.InitialAmountPaid.Value <= 0)
                    {
                        return BadRequest(new { message = "An initial payment amount greater than zero is required when choosing 'Advance'." });
                    }
                    if (request.InitialAmountPaid.Value > calculatedAmount)
                    {
                        return BadRequest(new { message = $"Advance payment ({request.InitialAmountPaid.Value}) cannot exceed total amount ({calculatedAmount})." });
                    }
                    amountPaid = request.InitialAmountPaid.Value;
                    balanceAmount = calculatedAmount - amountPaid;
                    paymentStatus = balanceAmount == 0 ? "Completed" : "Partial";
                    break;

                default:
                    return BadRequest(new { message = "Invalid payment option. Allowed options are: 'PayFull', 'Book', 'Advance'." });
            }

            // Generate Keys & Serial Number (starting at 10000)
            string shortId = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
            string receiptToken = $"MDV-{shortId}";
            string donationId = Guid.NewGuid().ToString();
            int serialNumber = await GenerateNextSerialNumberAsync();

            var callerName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Volunteer";
            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? "Volunteer";

            // Create the Entity matching DTO properties
            var donation = new DonationEntity
            {
                PartitionKey = panchayath,
                RowKey = receiptToken,
                DonationId = donationId,
                SerialNumber = serialNumber,
                UserId = userId,
                WardNumber = wardNumber,
                DonorName = request.DonorName,
                WhatsAppNumber = request.WhatsAppNumber ?? string.Empty,
                KitCount = request.KitCount,
                TotalAmount = calculatedAmount,
                PaymentOption = paymentOption,
                AmountPaid = amountPaid,
                BalanceAmount = balanceAmount,
                PaymentStatus = paymentStatus,
                PaymentMode = request.PaymentMode?.Trim() ?? "Cash",
                TransactionReference = request.TransactionReference?.Trim() ?? string.Empty,
                Notes = request.Notes?.Trim() ?? string.Empty,
                TransactionDate = DateTime.UtcNow,
                UpdateDate = DateTime.UtcNow,
                UpdatedBy = $"{callerName} ({callerRole})",
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
                        amount: amountPaid > 0 ? amountPaid : calculatedAmount,
                        receiptToken: receiptToken
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] WhatsApp receipt failed for {receiptToken}: {ex.Message}");
                }
            }

            return Ok(new DonationResponse
            {
                DonationId = donationId,
                ReceiptToken = receiptToken,
                SerialNumber = serialNumber,
                DonorName = request.DonorName,
                WhatsAppNumber = request.WhatsAppNumber ?? string.Empty,
                KitCount = request.KitCount,
                TotalAmount = calculatedAmount,
                Panchayath = panchayath,
                WardNumber = wardNumber,
                CollectedByUserId = userId,
                CollectedByName = callerName,
                CollectedByRole = callerRole,
                PaymentOption = paymentOption,
                AmountPaid = amountPaid,
                BalanceAmount = balanceAmount,
                PaymentStatus = paymentStatus,
                PaymentMode = donation.PaymentMode,
                TransactionReference = donation.TransactionReference,
                Notes = donation.Notes,
                UpdateDate = donation.UpdateDate,
                UpdatedBy = donation.UpdatedBy,
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

            double sendAmount = (donation.AmountPaid > 0 || donation.BalanceAmount > 0)
                ? donation.AmountPaid
                : donation.TotalAmount;

            try
            {
                var success = await _whatsappService.SendReceiptAsync(
                    phoneNumber: targetPhone,
                    donorName: donation.DonorName,
                    kits: donation.KitCount,
                    amount: sendAmount,
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

        /// <summary>
        /// Updates the balance payment for a kit donation. Can be performed by:
        /// - The volunteer who collected the donation
        /// - The parent coordinator managing that volunteer
        /// - Ward Committee Lead for their ward
        /// - Admin
        /// </summary>
        [HttpPost("{receiptToken}/payments")]
        public async Task<IActionResult> UpdateDonationPayment(string receiptToken, [FromBody] UpdateDonationPaymentRequest request, [FromQuery] string panchayath = "Madavoor")
        {
            var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst("sub")?.Value
                      ?? User.FindFirst("UserId")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value
                    ?? User.FindFirst("role")?.Value ?? "Volunteer";
            var callerName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Volunteer";
            var wardString = User.FindFirst("WardNumber")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Invalid user token." });
            }

            int callerWard = 0;
            if (_userRepository != null)
            {
                var users = await _userRepository.QueryAsync(u => u.UserId == userId || u.RowKey == userId);
                var currentUser = users.FirstOrDefault();
                if (currentUser != null)
                {
                    userId = currentUser.UserId;
                    callerWard = currentUser.WardNumber;
                    if (!string.IsNullOrEmpty(currentUser.FullName))
                    {
                        callerName = currentUser.FullName;
                    }
                }
            }

            if (callerWard == 0 && !string.IsNullOrEmpty(wardString) && int.TryParse(wardString, out int parsedWard))
            {
                callerWard = parsedWard;
            }

            var donation = await _donationRepository.GetEntityAsync(panchayath, receiptToken);
            if (donation == null)
            {
                var queryList = await _donationRepository.QueryAsync(d => d.RowKey == receiptToken);
                donation = queryList.FirstOrDefault();
            }

            if (donation == null)
            {
                return NotFound(new { message = $"Donation with receipt token '{receiptToken}' not found." });
            }

            // Check authorization: Admin, collector volunteer, parent coordinator, or ward committee for same ward
            bool isAuthorized = role == "Admin"
                || donation.UserId == userId
                || (role == "WardCommittee" && donation.WardNumber == callerWard);

            if (!isAuthorized && _userRepository != null)
            {
                // Check if coordinator is parent of collector
                var collectorTeam = await _userRepository.QueryAsync(u => u.UserId == donation.UserId);
                var collector = collectorTeam.FirstOrDefault();
                if (collector != null && collector.ParentUserId == userId)
                {
                    isAuthorized = true;
                }
            }

            if (!isAuthorized)
            {
                return StatusCode(403, new { message = "You are not authorized to update payments for this donation. Only the collector volunteer, parent coordinator, or ward lead may update it." });
            }

            // If already fully paid
            if (donation.BalanceAmount <= 0 && (!string.IsNullOrEmpty(donation.PaymentStatus) && donation.PaymentStatus == "Completed"))
            {
                return BadRequest(new { message = "This donation is already fully paid. No further balance payment is due." });
            }

            if (request.AmountToPay <= 0)
            {
                return BadRequest(new { message = "Payment amount must be greater than zero." });
            }

            double currentBalance = donation.BalanceAmount > 0 ? donation.BalanceAmount : Math.Max(0, donation.TotalAmount - donation.AmountPaid);
            if (request.AmountToPay > currentBalance)
            {
                return BadRequest(new { message = $"Payment amount ({request.AmountToPay:F2}) exceeds remaining balance ({currentBalance:F2})." });
            }

            // Update amounts
            donation.AmountPaid += request.AmountToPay;
            donation.BalanceAmount = donation.TotalAmount - donation.AmountPaid;

            if (donation.BalanceAmount <= 0.001)
            {
                donation.BalanceAmount = 0.0;
                donation.PaymentStatus = "Completed";
            }
            else
            {
                donation.PaymentStatus = "Partial";
            }

            if (!string.IsNullOrWhiteSpace(request.PaymentMode))
            {
                donation.PaymentMode = request.PaymentMode.Trim();
            }
            if (!string.IsNullOrWhiteSpace(request.TransactionReference))
            {
                donation.TransactionReference = request.TransactionReference.Trim();
            }
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                donation.Notes = request.Notes.Trim();
            }

            donation.UpdateDate = DateTime.UtcNow;
            donation.UpdatedBy = $"{callerName} ({role})";

            await _donationRepository.UpsertAsync(donation);

            UserEntity? collectorUser = null;
            if (_userRepository != null)
            {
                var cList = await _userRepository.QueryAsync(u => u.UserId == donation.UserId);
                collectorUser = cList.FirstOrDefault();
            }

            return Ok(MapToResponse(donation, collectorUser));
        }
    }
}