using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SponsorshipsController : ControllerBase
    {
        private readonly ITableStorageService<SponsorshipItemEntity> _itemRepository;
        private readonly ITableStorageService<SponsorshipEntity> _sponsorshipRepository;
        private readonly ITableStorageService<UserEntity>? _userRepository;

        // Fallback default panchayath
        private const string DEFAULT_PANCHAYATH = "Madavoor";

        public SponsorshipsController(
            ITableStorageService<SponsorshipItemEntity> itemRepository,
            ITableStorageService<SponsorshipEntity> sponsorshipRepository,
            ITableStorageService<UserEntity>? userRepository = null)
        {
            _itemRepository = itemRepository;
            _sponsorshipRepository = sponsorshipRepository;
            _userRepository = userRepository;
        }

        // ====================================================================
        // 1. SPONSORSHIP ITEM CATALOG (MENU) ENDPOINTS
        // ====================================================================

        /// <summary>
        /// Fetches the list of active sponsorship items/packages (maximum 4 to 5 items) for the sponsorship menu.
        /// Pre-seeds standard items if table is empty.
        /// </summary>
        [HttpGet("items")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSponsorshipItems([FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            var items = await _itemRepository.QueryEntitiesAsync(null);

            // Filter by panchayath (or global partition) and active state
            var filtered = items
                .Where(i => (string.IsNullOrEmpty(i.PartitionKey) || i.PartitionKey.Equals(panchayath, StringComparison.OrdinalIgnoreCase)) && i.IsActive)
                .OrderBy(i => i.DisplayOrder)
                .ThenBy(i => i.ItemPrice)
                .Take(5) // Max 4 or 5 items in menu as per product owner requirements
                .ToList();

            // Auto-seed initial 4 standard items if the catalog is empty
            if (!filtered.Any())
            {
                var defaultItems = GetDefaultCatalogItems(panchayath);
                foreach (var item in defaultItems)
                {
                    try
                    {
                        await _itemRepository.AddAsync(item);
                        filtered.Add(item);
                    }
                    catch
                    {
                        // Ignore collision if already seeded concurrently
                    }
                }
            }

            var result = filtered.Take(5).Select(i => new SponsorshipItemDto
            {
                ItemId = i.ItemId,
                Name = i.Name,
                ItemPrice = i.ItemPrice,
                Description = i.Description,
                IsActive = i.IsActive,
                DisplayOrder = i.DisplayOrder,
                UpdateDate = i.UpdateDate,
                UpdatedBy = i.UpdatedBy
            });

            return Ok(result);
        }

        /// <summary>
        /// Adds a new sponsorship item to the catalog (Admin & Coordinator only).
        /// </summary>
        [HttpPost("items")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateSponsorshipItem([FromBody] CreateSponsorshipItemRequest request, [FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            var callerName = GetCallerFullName();
            var itemId = string.IsNullOrWhiteSpace(request.ItemId)
                ? $"ITEM-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}"
                : request.ItemId.Trim();

            var existing = await _itemRepository.GetEntityAsync(panchayath, itemId);
            if (existing != null)
            {
                return Conflict(new { message = $"Sponsorship item with ID '{itemId}' already exists." });
            }

            var entity = new SponsorshipItemEntity
            {
                PartitionKey = panchayath,
                RowKey = itemId,
                ItemId = itemId,
                Name = request.Name.Trim(),
                ItemPrice = request.ItemPrice,
                Description = request.Description?.Trim() ?? string.Empty,
                IsActive = true,
                DisplayOrder = request.DisplayOrder,
                UpdateDate = DateTime.UtcNow,
                UpdatedBy = callerName
            };

            await _itemRepository.AddAsync(entity);

            return CreatedAtAction(nameof(GetSponsorshipItems), new { panchayath }, new SponsorshipItemDto
            {
                ItemId = entity.ItemId,
                Name = entity.Name,
                ItemPrice = entity.ItemPrice,
                Description = entity.Description,
                IsActive = entity.IsActive,
                DisplayOrder = entity.DisplayOrder,
                UpdateDate = entity.UpdateDate,
                UpdatedBy = entity.UpdatedBy
            });
        }

        /// <summary>
        /// Updates an existing sponsorship item in the catalog (Admin & Coordinator only).
        /// </summary>
        [HttpPut("items/{itemId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateSponsorshipItem(string itemId, [FromBody] UpdateSponsorshipItemRequest request, [FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            var item = await _itemRepository.GetEntityAsync(panchayath, itemId);
            if (item == null)
            {
                var queryList = await _itemRepository.QueryAsync(i => i.ItemId == itemId || i.RowKey == itemId);
                item = queryList.FirstOrDefault();
            }

            if (item == null)
            {
                return NotFound(new { message = $"Sponsorship item '{itemId}' not found." });
            }

            var callerName = GetCallerFullName();
            item.Name = request.Name.Trim();
            item.ItemPrice = request.ItemPrice;
            item.Description = request.Description?.Trim() ?? string.Empty;
            item.IsActive = request.IsActive;
            item.DisplayOrder = request.DisplayOrder;
            item.UpdateDate = DateTime.UtcNow;
            item.UpdatedBy = callerName;

            await _itemRepository.UpdateAsync(item);

            return Ok(new SponsorshipItemDto
            {
                ItemId = item.ItemId,
                Name = item.Name,
                ItemPrice = item.ItemPrice,
                Description = item.Description,
                IsActive = item.IsActive,
                DisplayOrder = item.DisplayOrder,
                UpdateDate = item.UpdateDate,
                UpdatedBy = item.UpdatedBy
            });
        }

        // ====================================================================
        // 2. ACCEPT & MANAGE SPONSORSHIPS
        // ====================================================================

        /// <summary>
        /// Accepts a sponsorship from a firm or organization with flexible payment terms (PayFull, Book, Advance).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AcceptSponsorship([FromBody] CreateSponsorshipRequest request)
        {
            var userId = GetCallerUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Invalid user token. Missing required user claims." });
            }

            var panchayath = GetCallerPanchayath();
            var callerRole = GetCallerRole();
            var callerName = GetCallerFullName();
            int callerWard = GetCallerWardNumber();
            string parentUserId = string.Empty;

            // Enrich user context from database if repository is available
            if (_userRepository != null)
            {
                var users = await _userRepository.QueryAsync(u => u.UserId == userId || u.RowKey == userId);
                var currentUser = users.FirstOrDefault();
                if (currentUser != null)
                {
                    userId = currentUser.UserId;
                    callerWard = currentUser.WardNumber;
                    parentUserId = currentUser.ParentUserId ?? string.Empty;
                    if (!string.IsNullOrEmpty(currentUser.FullName))
                    {
                        callerName = currentUser.FullName;
                    }
                }
            }

            // 1. Resolve item(s) from catalog and compute totals
            var itemDetails = new List<SponsorshipItemDetailDto>();
            double totalAmount = 0.0;
            int totalQuantity = 0;
            string primaryItemId = string.Empty;
            string combinedItemName = string.Empty;
            double avgItemPrice = 0.0;

            if (request.Items != null && request.Items.Count > 0)
            {
                var allCatalogItems = (await _itemRepository.QueryEntitiesAsync(null)).ToList();
                foreach (var sel in request.Items)
                {
                    if (sel.Quantity <= 0) continue;
                    var catItem = allCatalogItems.FirstOrDefault(i => 
                        (string.IsNullOrEmpty(i.PartitionKey) || i.PartitionKey.Equals(panchayath, StringComparison.OrdinalIgnoreCase)) &&
                        (i.ItemId == sel.ItemId || i.RowKey == sel.ItemId));

                    if (catItem == null || !catItem.IsActive)
                    {
                        return BadRequest(new { message = $"Sponsorship item with ID '{sel.ItemId}' is invalid or inactive." });
                    }

                    double subtotal = catItem.ItemPrice * sel.Quantity;
                    totalAmount += subtotal;
                    totalQuantity += sel.Quantity;
                    itemDetails.Add(new SponsorshipItemDetailDto
                    {
                        ItemId = catItem.ItemId,
                        Name = catItem.Name,
                        UnitPrice = catItem.ItemPrice,
                        Quantity = sel.Quantity,
                        Subtotal = subtotal
                    });
                }

                if (itemDetails.Count == 0)
                {
                    return BadRequest(new { message = "At least one sponsorship item with quantity > 0 is required." });
                }

                primaryItemId = itemDetails[0].ItemId;
                combinedItemName = itemDetails.Count == 1 
                    ? itemDetails[0].Name 
                    : string.Join(", ", itemDetails.Select(d => $"{d.Quantity}x {d.Name}"));
                avgItemPrice = totalQuantity > 0 ? Math.Round(totalAmount / totalQuantity, 2) : 0;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.ItemId))
                {
                    return BadRequest(new { message = "Sponsorship item is required." });
                }

                var item = await _itemRepository.GetEntityAsync(panchayath, request.ItemId);
                if (item == null)
                {
                    var allItems = await _itemRepository.QueryAsync(i => i.ItemId == request.ItemId || i.RowKey == request.ItemId);
                    item = allItems.FirstOrDefault();
                }

                if (item == null || !item.IsActive)
                {
                    return BadRequest(new { message = $"Sponsorship item with ID '{request.ItemId}' is invalid or inactive." });
                }

                primaryItemId = item.ItemId;
                combinedItemName = item.Name;
                avgItemPrice = item.ItemPrice;
                totalQuantity = request.Quantity > 0 ? request.Quantity : 1;
                totalAmount = avgItemPrice * totalQuantity;
                itemDetails.Add(new SponsorshipItemDetailDto
                {
                    ItemId = item.ItemId,
                    Name = item.Name,
                    UnitPrice = item.ItemPrice,
                    Quantity = totalQuantity,
                    Subtotal = totalAmount
                });
            }

            // 2. Process the 3 Payment Options: PayFull, Book, Advance
            double amountPaid;
            double balanceAmount;
            string paymentStatus;

            if (request.InitialAmountPaid.HasValue && request.InitialAmountPaid.Value < 0)
            {
                return BadRequest(new { message = "Payment amount cannot be negative." });
            }

            switch (request.PaymentOption.Trim())
            {
                case "PayFull":
                    amountPaid = totalAmount;
                    balanceAmount = 0.0;
                    paymentStatus = "Completed";
                    break;

                case "Book":
                    // Booking: can be 0 or small booking token amount
                    amountPaid = request.InitialAmountPaid ?? 0.0;
                    if (amountPaid < 0)
                    {
                        return BadRequest(new { message = "Payment amount cannot be negative." });
                    }
                    if (amountPaid > totalAmount)
                    {
                        return BadRequest(new { message = $"Initial booking amount ({amountPaid}) cannot exceed total amount ({totalAmount})." });
                    }
                    balanceAmount = totalAmount - amountPaid;
                    paymentStatus = balanceAmount == 0 ? "Completed" : (amountPaid > 0 ? "Partial" : "Booked");
                    break;

                case "Advance":
                    if (!request.InitialAmountPaid.HasValue || request.InitialAmountPaid.Value <= 0)
                    {
                        return BadRequest(new { message = "An initial payment amount greater than zero is required when choosing 'Advance'." });
                    }
                    if (request.InitialAmountPaid.Value > totalAmount)
                    {
                        return BadRequest(new { message = $"Advance payment ({request.InitialAmountPaid.Value}) cannot exceed total amount ({totalAmount})." });
                    }

                    amountPaid = request.InitialAmountPaid.Value;
                    balanceAmount = totalAmount - amountPaid;
                    paymentStatus = balanceAmount == 0 ? "Completed" : "Partial";
                    break;

                default:
                    return BadRequest(new { message = "Invalid payment option. Allowed options are: 'PayFull', 'Book', 'Advance'." });
            }

            // 3. Generate unique receipt token & GUID
            string shortCode = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
            string receiptToken = $"SPON-{shortCode}";
            string sponsorshipId = Guid.NewGuid().ToString();
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            string itemsJson = JsonSerializer.Serialize(itemDetails, jsonOptions);

            var entity = new SponsorshipEntity
            {
                PartitionKey = panchayath,
                RowKey = receiptToken,
                SponsorshipId = sponsorshipId,
                ReceiptToken = receiptToken,
                DonorName = request.DonorName.Trim(),
                ContactPerson = request.ContactPerson?.Trim() ?? string.Empty,
                MobileNumber = request.MobileNumber.Trim(),
                ItemId = primaryItemId,
                ItemName = combinedItemName,
                ItemPrice = avgItemPrice,
                Quantity = totalQuantity,
                TotalAmount = totalAmount,
                ItemsJson = itemsJson,
                PaymentOption = request.PaymentOption.Trim(),
                AmountPaid = amountPaid,
                BalanceAmount = balanceAmount,
                PaymentStatus = paymentStatus,
                PaymentMode = request.PaymentMode?.Trim() ?? "Cash",
                TransactionReference = request.TransactionReference?.Trim() ?? string.Empty,
                CollectedByUserId = userId,
                CollectedByName = callerName,
                CollectedByRole = callerRole,
                ParentUserId = parentUserId,
                WardNumber = callerWard,
                Notes = request.Notes?.Trim() ?? string.Empty,
                CreatedDate = DateTime.UtcNow,
                UpdateDate = DateTime.UtcNow,
                UpdatedBy = $"{callerName} ({callerRole})"
            };

            await _sponsorshipRepository.AddAsync(entity);

            return Ok(MapToResponse(entity));
        }

        /// <summary>
        /// Retrieves all sponsorships filtered according to caller's hierarchy:
        /// - Volunteer: sees own sponsorships
        /// - Coordinator: sees own + subordinate volunteers' sponsorships + same ward
        /// - Ward Committee: sees all sponsorships in their ward
        /// - Admin: sees all campaign sponsorships
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSponsorships([FromQuery] string? status = null)
        {
            var userId = GetCallerUserId();
            var role = GetCallerRole();
            var callerWard = GetCallerWardNumber();

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Invalid user token." });
            }

            // Enrich user if repo available
            if (_userRepository != null)
            {
                var users = await _userRepository.QueryAsync(u => u.UserId == userId || u.RowKey == userId);
                var currentUser = users.FirstOrDefault();
                if (currentUser != null)
                {
                    userId = currentUser.UserId;
                    callerWard = currentUser.WardNumber;
                }
            }

            var all = await _sponsorshipRepository.QueryEntitiesAsync(null);
            IEnumerable<SponsorshipEntity> filtered;

            if (role == "Admin")
            {
                filtered = all;
            }
            else
            {
                // Visibility Model:
                // Only collected volunteer (himself), parent coordinator, and all members under the same ward
                var teamUserIds = new HashSet<string> { userId };
                if (_userRepository != null)
                {
                    var team = await _userRepository.QueryAsync(u => u.ParentUserId == userId || u.UserId == userId);
                    foreach (var member in team)
                    {
                        teamUserIds.Add(member.UserId);
                    }
                }

                filtered = all.Where(s =>
                    s.CollectedByUserId == userId ||
                    (!string.IsNullOrEmpty(s.ParentUserId) && s.ParentUserId == userId) ||
                    teamUserIds.Contains(s.CollectedByUserId) ||
                    (callerWard > 0 && s.WardNumber == callerWard)
                );
            }

            // Optional status filter
            if (!string.IsNullOrWhiteSpace(status))
            {
                filtered = filtered.Where(s => s.PaymentStatus.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            var ordered = filtered
                .OrderByDescending(s => s.UpdateDate != default ? s.UpdateDate : s.CreatedDate)
                .Select(MapToResponse)
                .ToList();

            return Ok(ordered);
        }

        /// <summary>
        /// Retrieves single sponsorship record by receipt token.
        /// Authenticated users must be Super Admin, the collector, parent coordinator, or belonging to the same ward.
        /// </summary>
        [HttpGet("{receiptToken}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSponsorship(string receiptToken, [FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            var sponsorship = await _sponsorshipRepository.GetEntityAsync(panchayath, receiptToken);
            if (sponsorship == null)
            {
                var queryList = await _sponsorshipRepository.QueryAsync(s => s.RowKey == receiptToken || s.ReceiptToken == receiptToken);
                sponsorship = queryList.FirstOrDefault();
            }

            if (sponsorship == null)
            {
                return NotFound(new { message = $"Sponsorship with receipt token '{receiptToken}' not found." });
            }

            var userId = GetCallerUserId();
            var role = GetCallerRole();
            var callerWard = GetCallerWardNumber();

            // If an authenticated user from another ward tries to view this, restrict access
            if (!string.IsNullOrEmpty(userId))
            {
                bool isAuthorized = role == "Admin"
                    || sponsorship.CollectedByUserId == userId
                    || (!string.IsNullOrEmpty(sponsorship.ParentUserId) && sponsorship.ParentUserId == userId)
                    || (callerWard > 0 && sponsorship.WardNumber == callerWard);

                if (!isAuthorized && _userRepository != null)
                {
                    var team = await _userRepository.QueryAsync(u => u.ParentUserId == userId);
                    if (team.Any(t => t.UserId == sponsorship.CollectedByUserId))
                    {
                        isAuthorized = true;
                    }
                }

                if (!isAuthorized)
                {
                    return StatusCode(403, new { message = "You are not authorized to view sponsorships outside of your assigned ward or team." });
                }
            }

            return Ok(MapToResponse(sponsorship));
        }

        /// <summary>
        /// Updates the payment for a sponsorship. Can be performed by:
        /// - The volunteer who collected the sponsorship
        /// - The parent coordinator managing that volunteer
        /// - Ward Committee Lead for their ward
        /// - Admin
        /// </summary>
        [HttpPost("{receiptToken}/payments")]
        public async Task<IActionResult> UpdateSponsorshipPayment(string receiptToken, [FromBody] UpdateSponsorshipPaymentRequest request, [FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            var userId = GetCallerUserId();
            var role = GetCallerRole();
            var callerName = GetCallerFullName();
            var callerWard = GetCallerWardNumber();

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Invalid user token." });
            }

            // Enrich user if repo available
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

            var sponsorship = await _sponsorshipRepository.GetEntityAsync(panchayath, receiptToken);
            if (sponsorship == null)
            {
                var queryList = await _sponsorshipRepository.QueryAsync(s => s.RowKey == receiptToken || s.ReceiptToken == receiptToken);
                sponsorship = queryList.FirstOrDefault();
            }

            if (sponsorship == null)
            {
                return NotFound(new { message = $"Sponsorship with receipt token '{receiptToken}' not found." });
            }

            // Check authorization: Volunteer, Parent Coordinator, Ward Committee (same ward), or Admin
            bool isAuthorized = role == "Admin"
                || sponsorship.CollectedByUserId == userId
                || (!string.IsNullOrEmpty(sponsorship.ParentUserId) && sponsorship.ParentUserId == userId)
                || (role == "WardCommittee" && sponsorship.WardNumber == callerWard);

            if (!isAuthorized && _userRepository != null)
            {
                // Check if coordinator is parent of collector
                var collectorTeam = await _userRepository.QueryAsync(u => u.UserId == sponsorship.CollectedByUserId);
                var collector = collectorTeam.FirstOrDefault();
                if (collector != null && collector.ParentUserId == userId)
                {
                    isAuthorized = true;
                }
            }

            if (!isAuthorized)
            {
                return StatusCode(403, new { message = "You are not authorized to update payments for this sponsorship. Only the collector volunteer, parent coordinator, or ward lead may update it." });
            }

            // Check if already completed
            if (sponsorship.BalanceAmount <= 0 || sponsorship.PaymentStatus == "Completed")
            {
                return BadRequest(new { message = "This sponsorship is already fully paid. No further balance payment is due." });
            }

            // Validate payment amount
            if (request.AmountToPay <= 0)
            {
                return BadRequest(new { message = "Payment amount must be greater than zero." });
            }

            if (request.AmountToPay > sponsorship.BalanceAmount)
            {
                return BadRequest(new { message = $"Payment amount ({request.AmountToPay:F2}) exceeds remaining balance ({sponsorship.BalanceAmount:F2})." });
            }

            // Update financial progress
            sponsorship.AmountPaid += request.AmountToPay;
            sponsorship.BalanceAmount = sponsorship.TotalAmount - sponsorship.AmountPaid;

            if (sponsorship.BalanceAmount <= 0.001)
            {
                sponsorship.BalanceAmount = 0.0;
                sponsorship.PaymentStatus = "Completed";
            }
            else
            {
                sponsorship.PaymentStatus = "Partial";
            }

            if (!string.IsNullOrWhiteSpace(request.PaymentMode))
            {
                sponsorship.PaymentMode = request.PaymentMode.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.TransactionReference))
            {
                sponsorship.TransactionReference = request.TransactionReference.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                sponsorship.Notes = string.IsNullOrWhiteSpace(sponsorship.Notes)
                    ? request.Notes.Trim()
                    : $"{sponsorship.Notes}; {request.Notes.Trim()}";
            }

            sponsorship.UpdateDate = DateTime.UtcNow;
            sponsorship.UpdatedBy = $"{callerName} ({role})";

            await _sponsorshipRepository.UpdateAsync(sponsorship);

            return Ok(MapToResponse(sponsorship));
        }

        // ====================================================================
        // 3. SEPARATE SPONSORSHIP LEADERBOARD ENDPOINT
        // ====================================================================

        /// <summary>
        /// Computes and returns the dedicated corporate/organization sponsorship leaderboard.
        /// This is kept strictly SEPARATE from the individual donation rewards and leaderboards.
        /// Publicly viewable for campaign transparency.
        /// </summary>
        [HttpGet("leaderboard")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSponsorshipLeaderboard()
        {
            var allSponsorships = await _sponsorshipRepository.QueryEntitiesAsync(null);

            // Fetch users to enrich collector profiles
            var userMap = new Dictionary<string, UserEntity>();
            if (_userRepository != null)
            {
                var allUsers = await _userRepository.QueryEntitiesAsync(null);
                userMap = allUsers
                    .Where(u => !string.IsNullOrEmpty(u.UserId))
                    .GroupBy(u => u.UserId)
                    .ToDictionary(g => g.Key, g => g.First());
            }

            // 1. Top Collectors (Volunteers, Coordinators, Ward Committee Leads)
            var collectorGroups = allSponsorships
                .Where(s => !string.IsNullOrEmpty(s.CollectedByUserId))
                .GroupBy(s => s.CollectedByUserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    SponsorshipCount = g.Count(),
                    TotalCommittedAmount = g.Sum(s => s.TotalAmount),
                    TotalPaidAmount = g.Sum(s => s.AmountPaid),
                    BalanceAmount = g.Sum(s => s.BalanceAmount)
                })
                .OrderByDescending(c => c.TotalPaidAmount)
                .ThenByDescending(c => c.TotalCommittedAmount)
                .ToList();

            var topCollectors = new List<SponsorshipLeaderboardEntry>();
            int collectorPos = 1;
            foreach (var stat in collectorGroups)
            {
                userMap.TryGetValue(stat.UserId, out var user);
                topCollectors.Add(new SponsorshipLeaderboardEntry
                {
                    Position = collectorPos,
                    UserId = stat.UserId,
                    Name = user?.FullName ?? "Fundraiser",
                    Role = user?.Role ?? "Volunteer",
                    WardNumber = user?.WardNumber ?? 0,
                    Panchayath = user?.Panchayath ?? DEFAULT_PANCHAYATH,
                    SponsorshipCount = stat.SponsorshipCount,
                    TotalCommittedAmount = stat.TotalCommittedAmount,
                    TotalPaidAmount = stat.TotalPaidAmount,
                    BalanceAmount = stat.BalanceAmount,
                    RankBadge = AssignBadge(collectorPos)
                });
                collectorPos++;
            }

            // 2. Top Wards for Sponsorships
            var wardGroups = allSponsorships
                .Select(s => new
                {
                    WardNumber = s.WardNumber > 0 ? s.WardNumber : (userMap.TryGetValue(s.CollectedByUserId, out var u) ? u.WardNumber : 0),
                    s.TotalAmount,
                    s.AmountPaid
                })
                .Where(x => x.WardNumber > 0)
                .GroupBy(x => x.WardNumber)
                .Select(g => new
                {
                    WardNumber = g.Key,
                    SponsorshipCount = g.Count(),
                    TotalCommittedAmount = g.Sum(x => x.TotalAmount),
                    TotalPaidAmount = g.Sum(x => x.AmountPaid)
                })
                .OrderByDescending(w => w.TotalPaidAmount)
                .ThenByDescending(w => w.TotalCommittedAmount)
                .ToList();

            var topWards = new List<SponsorshipWardLeaderboardEntry>();
            int wardPos = 1;
            foreach (var stat in wardGroups)
            {
                topWards.Add(new SponsorshipWardLeaderboardEntry
                {
                    Position = wardPos,
                    WardNumber = stat.WardNumber,
                    WardName = $"Ward {stat.WardNumber}",
                    SponsorshipCount = stat.SponsorshipCount,
                    TotalCommittedAmount = stat.TotalCommittedAmount,
                    TotalPaidAmount = stat.TotalPaidAmount,
                    RankBadge = AssignBadge(wardPos)
                });
                wardPos++;
            }

            // 3. Top Sponsoring Firms / Organizations
            // Confidentiality Model:
            // Sponsoring firms and donor details are NOT visible to the general public or other wards.
            // Only visible to:
            // - Super Admin (all campaign firms)
            // - Himself (collector volunteer)
            // - All members under the same ward (volunteers and ward committee lead under same ward)
            // - Parent coordinator
            var callerUserId = GetCallerUserId();
            var callerRole = GetCallerRole();
            var callerWard = GetCallerWardNumber();

            if (!string.IsNullOrEmpty(callerUserId) && _userRepository != null)
            {
                var users = await _userRepository.QueryAsync(u => u.UserId == callerUserId || u.RowKey == callerUserId);
                var curUser = users.FirstOrDefault();
                if (curUser != null)
                {
                    callerUserId = curUser.UserId;
                    callerWard = curUser.WardNumber;
                    if (string.IsNullOrEmpty(callerRole)) callerRole = curUser.Role;
                }
            }

            List<TopSponsoringFirmEntry> topSponsoringFirms;

            if (string.IsNullOrEmpty(callerUserId))
            {
                // Public / Anonymous visitors: Hide firm identities & contact details
                topSponsoringFirms = new List<TopSponsoringFirmEntry>();
            }
            else if (callerRole == "Admin")
            {
                // Super Admin: Sees all campaign sponsoring firms
                topSponsoringFirms = allSponsorships
                    .OrderByDescending(s => s.TotalAmount)
                    .ThenByDescending(s => s.AmountPaid)
                    .Take(50)
                    .Select((s, index) => new TopSponsoringFirmEntry
                    {
                        Position = index + 1,
                        FirmName = s.DonorName,
                        ContactPerson = s.ContactPerson,
                        MobileNumber = s.MobileNumber,
                        ItemName = s.ItemName,
                        Quantity = s.Quantity,
                        TotalAmount = s.TotalAmount,
                        AmountPaid = s.AmountPaid,
                        BalanceAmount = s.BalanceAmount,
                        PaymentStatus = s.PaymentStatus,
                        CollectedByName = s.CollectedByName,
                        Date = s.CreatedDate
                    })
                    .ToList();
            }
            else
            {
                // Authenticated Ward Member / Coordinator / Volunteer:
                // Only sees: himself, all of his ward members (volunteers under same ward), and parent coordinator
                var teamUserIds = new HashSet<string> { callerUserId };
                if (_userRepository != null)
                {
                    var team = await _userRepository.QueryAsync(u => u.ParentUserId == callerUserId || u.UserId == callerUserId);
                    foreach (var member in team)
                    {
                        teamUserIds.Add(member.UserId);
                    }
                }

                topSponsoringFirms = allSponsorships
                    .Where(s =>
                        s.CollectedByUserId == callerUserId ||
                        (!string.IsNullOrEmpty(s.ParentUserId) && s.ParentUserId == callerUserId) ||
                        teamUserIds.Contains(s.CollectedByUserId) ||
                        (callerWard > 0 && s.WardNumber == callerWard)
                    )
                    .OrderByDescending(s => s.TotalAmount)
                    .ThenByDescending(s => s.AmountPaid)
                    .Take(50)
                    .Select((s, index) => new TopSponsoringFirmEntry
                    {
                        Position = index + 1,
                        FirmName = s.DonorName,
                        ContactPerson = s.ContactPerson,
                        MobileNumber = s.MobileNumber,
                        ItemName = s.ItemName,
                        Quantity = s.Quantity,
                        TotalAmount = s.TotalAmount,
                        AmountPaid = s.AmountPaid,
                        BalanceAmount = s.BalanceAmount,
                        PaymentStatus = s.PaymentStatus,
                        CollectedByName = s.CollectedByName,
                        Date = s.CreatedDate
                    })
                    .ToList();
            }

            // 4. Summary Totals
            var summary = new SponsorshipSummary
            {
                TotalSponsorships = allSponsorships.Count,
                TotalCommittedAmount = allSponsorships.Sum(s => s.TotalAmount),
                TotalPaidAmount = allSponsorships.Sum(s => s.AmountPaid),
                TotalPendingBalance = allSponsorships.Sum(s => s.BalanceAmount),
                CompletedCount = allSponsorships.Count(s => s.PaymentStatus == "Completed"),
                PartialCount = allSponsorships.Count(s => s.PaymentStatus == "Partial"),
                BookedCount = allSponsorships.Count(s => s.PaymentStatus == "Booked")
            };

            return Ok(new SponsorshipLeaderboardResponse
            {
                TopCollectors = topCollectors.Take(100).ToList(),
                TopWards = topWards,
                TopSponsoringFirms = topSponsoringFirms,
                Summary = summary,
                GeneratedAt = DateTime.UtcNow
            });
        }

        // ====================================================================
        // HELPER METHODS
        // ====================================================================

        private static SponsorshipResponse MapToResponse(SponsorshipEntity s)
        {
            List<SponsorshipItemDetailDto>? parsedItems = null;
            if (!string.IsNullOrWhiteSpace(s.ItemsJson))
            {
                try
                {
                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    parsedItems = JsonSerializer.Deserialize<List<SponsorshipItemDetailDto>>(s.ItemsJson, jsonOptions);
                }
                catch
                {
                    parsedItems = null;
                }
            }

            return new SponsorshipResponse
            {
                SponsorshipId = s.SponsorshipId,
                ReceiptToken = s.RowKey,
                DonorName = s.DonorName,
                ContactPerson = s.ContactPerson,
                MobileNumber = s.MobileNumber,
                ItemId = s.ItemId,
                ItemName = s.ItemName,
                ItemPrice = s.ItemPrice,
                Quantity = s.Quantity,
                TotalAmount = s.TotalAmount,
                ItemsJson = s.ItemsJson,
                Items = parsedItems,
                PaymentOption = s.PaymentOption,
                AmountPaid = s.AmountPaid,
                BalanceAmount = s.BalanceAmount,
                PaymentStatus = s.PaymentStatus,
                PaymentMode = s.PaymentMode,
                TransactionReference = s.TransactionReference,
                Panchayath = s.PartitionKey,
                WardNumber = s.WardNumber,
                CollectedByUserId = s.CollectedByUserId,
                CollectedByName = s.CollectedByName,
                CollectedByRole = s.CollectedByRole,
                ParentUserId = s.ParentUserId,
                Notes = s.Notes,
                CreatedDate = s.CreatedDate,
                UpdateDate = s.UpdateDate,
                UpdatedBy = s.UpdatedBy
            };
        }

        private static List<SponsorshipItemEntity> GetDefaultCatalogItems(string panchayath)
        {
            return new List<SponsorshipItemEntity>
            {
                new SponsorshipItemEntity
                {
                    PartitionKey = panchayath,
                    RowKey = "ITEM-001",
                    ItemId = "ITEM-001",
                    Name = "Family Food Relief Kit Pack",
                    ItemPrice = 5000.0,
                    Description = "Provides essential food supplies and ration for a needy family for one month.",
                    IsActive = true,
                    DisplayOrder = 1,
                    UpdateDate = DateTime.UtcNow,
                    UpdatedBy = "System"
                },
                new SponsorshipItemEntity
                {
                    PartitionKey = panchayath,
                    RowKey = "ITEM-002",
                    ItemId = "ITEM-002",
                    Name = "Student Education & School Kit Pack",
                    ItemPrice = 2500.0,
                    Description = "Includes school bag, books, uniform materials, and stationery for underprivileged students.",
                    IsActive = true,
                    DisplayOrder = 2,
                    UpdateDate = DateTime.UtcNow,
                    UpdatedBy = "System"
                },
                new SponsorshipItemEntity
                {
                    PartitionKey = panchayath,
                    RowKey = "ITEM-003",
                    ItemId = "ITEM-003",
                    Name = "Emergency Medical Care Support Pack",
                    ItemPrice = 10000.0,
                    Description = "Supports life-saving medicines and treatment costs for chronically ill community members.",
                    IsActive = true,
                    DisplayOrder = 3,
                    UpdateDate = DateTime.UtcNow,
                    UpdatedBy = "System"
                },
                new SponsorshipItemEntity
                {
                    PartitionKey = panchayath,
                    RowKey = "ITEM-004",
                    ItemId = "ITEM-004",
                    Name = "Complete Ramadan / Eid Family Hamper",
                    ItemPrice = 7500.0,
                    Description = "Full celebratory festive food, clothing assistance, and gift hamper for a vulnerable family.",
                    IsActive = true,
                    DisplayOrder = 4,
                    UpdateDate = DateTime.UtcNow,
                    UpdatedBy = "System"
                }
            };
        }

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

        private string GetCallerUserId()
        {
            return User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("UserId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? string.Empty;
        }

        private string GetCallerRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value
                ?? "Volunteer";
        }

        private string GetCallerFullName()
        {
            return User.FindFirst("FullName")?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? User.Identity?.Name
                ?? "Volunteer";
        }

        private string GetCallerPanchayath()
        {
            return User.FindFirst("Panchayath")?.Value ?? DEFAULT_PANCHAYATH;
        }

        private int GetCallerWardNumber()
        {
            var wardString = User.FindFirst("WardNumber")?.Value;
            if (!string.IsNullOrEmpty(wardString) && int.TryParse(wardString, out int ward))
            {
                return ward;
            }
            return 0;
        }
    }
}
