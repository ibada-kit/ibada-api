using Azure.Data.Tables;
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
    public class UsersController : ControllerBase
    {
        private readonly ITableStorageService<UserEntity> _userRepository;
        private readonly ITableStorageService<DonationEntity> _donationRepository;

        public UsersController(
            ITableStorageService<UserEntity> userRepository,
            ITableStorageService<DonationEntity>? donationRepository = null)
        {
            _userRepository = userRepository;
            _donationRepository = donationRepository!;
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            var creatorRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var creatorId = GetCurrentUserId();
            var creatorPanchayath = User.FindFirst("Panchayath")?.Value ?? "Madavoor";

            // 1. Volunteers cannot create anyone
            if (creatorRole == "Volunteer")
                return Forbid("Volunteers are not authorized to create users.");

            // Check if user already exists
            var existingUser = await _userRepository.QueryAsync(u => u.RowKey == request.PhoneNumber);
            if (existingUser.Any())
                return Conflict("A user with this phone number already exists.");

            string defaultPassword = !string.IsNullOrWhiteSpace(request.DefaultPassword) && request.DefaultPassword != "Welcome@123"
                ? request.DefaultPassword
                : GenerateDefaultPassword(request.FullName, request.PhoneNumber);

            var newUser = new UserEntity
            {
                PartitionKey = request.Panchayath ?? creatorPanchayath,
                RowKey = request.PhoneNumber,
                UserId = Guid.NewGuid().ToString(),
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                Role = request.Role,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword),
                District = request.District ?? "Kozhikode",
                Panchayath = request.Panchayath ?? creatorPanchayath,
                IsActive = true,
                TargetKits = request.TargetKits ?? 0,
                TargetAmount = (request.TargetKits ?? 0) * 1000.0 // Calculates financial target securely
            };

            // 2. Enforce Hierarchy & Role Constraints
            if (creatorRole == "Admin")
            {
                if (request.Role == "Volunteer")
                {
                    if (string.IsNullOrEmpty(request.AssignedParentUserId))
                        return BadRequest("Admin must specify an AssignedParentUserId (Coordinator or WardCommittee) when creating a Volunteer.");

                    // Verify the assigned parent exists and is eligible
                    var parentData = await _userRepository.QueryAsync(u => u.UserId == request.AssignedParentUserId);
                    var parent = parentData.FirstOrDefault();
                    if (parent == null || (parent.Role != "Coordinator" && parent.Role != "WardCommittee"))
                        return BadRequest("The assigned parent must be a Coordinator or WardCommittee.");

                    newUser.ParentUserId = parent.UserId;
                    newUser.WardNumber = parent.WardNumber; // Inherit ward from assigned parent
                }
                else if (request.Role == "WardCommittee")
                {
                    if (!request.WardNumber.HasValue) return BadRequest("WardNumber is required for WardCommittee.");

                    // Enforce rule: Each ward has only ONE Ward Coordinator
                    var existingWardCoord = await _userRepository.QueryAsync(u => u.Role == "WardCommittee" && u.WardNumber == request.WardNumber);
                    if (existingWardCoord.Any()) return Conflict($"Ward {request.WardNumber} already has a Ward Coordinator.");

                    newUser.ParentUserId = creatorId;
                    newUser.WardNumber = request.WardNumber.Value;
                }
                else if (request.Role == "Coordinator")
                {
                    newUser.ParentUserId = creatorId;
                }
            }
            else if (creatorRole == "Coordinator")
            {
                if (request.Role == "WardCommittee") return Forbid("Coordinators cannot create Ward Committees.");

                // Coordinator can create Coordinator or Volunteer
                newUser.ParentUserId = creatorId;
                newUser.WardNumber = request.WardNumber ?? 0;
            }
            else if (creatorRole == "WardCommittee")
            {
                if (request.Role != "Volunteer") return Forbid("Ward Committees can only create Volunteers.");

                // WardCommittee creates Volunteer: Inherits parent and exact Ward Number
                var creatorWard = int.Parse(User.FindFirst("WardNumber")?.Value ?? "0");

                newUser.ParentUserId = creatorId;
                newUser.WardNumber = creatorWard;
            }

            // 3. Save to Azure Table
            await _userRepository.AddAsync(newUser);

            return Ok(new { Message = "User created successfully.", UserId = newUser.UserId, DefaultPassword = defaultPassword });
        }

        [HttpGet("coordinators")]
        [Authorize(Roles = "Admin,Coordinator")]
        public async Task<IActionResult> GetCoordinators()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var userId = GetCurrentUserId();

            List<UserEntity> users;
            if (role == "Admin")
            {
                // Admins can see all Coordinators and Ward Committees across the Panchayath
                var fetched = await _userRepository.QueryAsync(u => u.Role == "Coordinator" || u.Role == "WardCommittee");
                users = fetched.ToList();
            }
            else if (role == "Coordinator")
            {
                // Coordinators can only see the sub-coordinators they explicitly created
                var fetched = await _userRepository.QueryAsync(u => u.Role == "Coordinator" && u.ParentUserId == userId);
                users = fetched.ToList();
            }
            else
            {
                return Forbid();
            }

            // Populate live collection stats for each coordinator
            var allDonations = (await _donationRepository.QueryAsync(d => d.TotalAmount > 0)).ToList();
            var allUsers = (await _userRepository.QueryAsync(u => u.IsActive)).ToList();

            var responses = users.Select(u =>
            {
                int kits = 0;
                double amount = 0;

                if (u.Role == "WardCommittee")
                {
                    var wardDons = allDonations.Where(d => d.WardNumber == u.WardNumber);
                    kits = wardDons.Sum(d => d.KitCount);
                    amount = wardDons.Sum(d => d.TotalAmount);
                }
                else // Coordinator
                {
                    // Volunteer IDs managed by this coordinator, plus the coordinator
                    var managedIds = allUsers
                        .Where(v => v.ParentUserId == u.UserId || v.UserId == u.UserId)
                        .Select(v => v.UserId)
                        .ToHashSet();

                    var coordDons = allDonations.Where(d => managedIds.Contains(d.UserId));
                    kits = coordDons.Sum(d => d.KitCount);
                    amount = coordDons.Sum(d => d.TotalAmount);
                }

                var resp = MapToResponse(u);
                resp.KitsCollected = kits;
                resp.CollectedAmount = amount;
                return resp;
            });

            return Ok(responses);
        }

        [HttpGet("volunteers")]
        [Authorize(Roles = "Admin,Coordinator,WardCommittee")]
        public async Task<IActionResult> GetVolunteers()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var userId = GetCurrentUserId();

            if (role == "Admin")
            {
                // Admins can view the entire volunteer list
                var users = await _userRepository.QueryAsync(u => u.Role == "Volunteer");
                return Ok(users.Select(MapToResponse));
            }
            else if (role == "WardCommittee")
            {
                var wardStr = User.FindFirst("WardNumber")?.Value;
                int.TryParse(wardStr, out int wardNum);
                var users = await _userRepository.QueryAsync(u => u.Role == "Volunteer" && (u.ParentUserId == userId || (wardNum > 0 && u.WardNumber == wardNum)));
                return Ok(users.Select(MapToResponse));
            }
            else
            {
                // Coordinators can only view the volunteers assigned directly to them
                var users = await _userRepository.QueryAsync(u => u.Role == "Volunteer" && u.ParentUserId == userId);
                return Ok(users.Select(MapToResponse));
            }
        }

        [HttpPut("{userId}/target")]
        [Authorize(Roles = "Admin,Coordinator")]
        public async Task<IActionResult> UpdateTarget(string userId, [FromBody] UpdateTargetRequest request)
        {
            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var callerId = GetCurrentUserId();

            var users = await _userRepository.QueryAsync(u => u.UserId == userId);
            var targetUser = users.FirstOrDefault();

            if (targetUser == null) return NotFound("User not found.");

            // Coordinators can only update targets for users they created
            if (callerRole == "Coordinator" && targetUser.ParentUserId != callerId)
                return Forbid("You can only set targets for your direct team members.");

            targetUser.TargetKits = request.TargetKits;
            targetUser.TargetAmount = request.TargetKits * 1000.0;

            await _userRepository.UpdateAsync(targetUser);

            return Ok(new { Message = "Target updated successfully.", targetUser.TargetKits });
        }

        [HttpPut("{userId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateUserRequest request)
        {
            var users = await _userRepository.QueryAsync(u => u.UserId == userId);
            var user = users.FirstOrDefault();

            if (user == null) return NotFound("User not found.");

            if (!string.IsNullOrWhiteSpace(request.FullName))
                user.FullName = request.FullName.Trim();

            if (!string.IsNullOrWhiteSpace(request.Role))
                user.Role = request.Role;

            if (request.WardNumber.HasValue)
                user.WardNumber = request.WardNumber.Value;

            if (!string.IsNullOrWhiteSpace(request.District))
                user.District = request.District;

            if (request.TargetKits.HasValue)
            {
                user.TargetKits = request.TargetKits.Value;
                user.TargetAmount = request.TargetKits.Value * 1000.0;
            }

            if (request.IsActive.HasValue)
                user.IsActive = request.IsActive.Value;

            string? changedPassword = null;
            if (!string.IsNullOrWhiteSpace(request.NewPassword))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                changedPassword = request.NewPassword;
            }

            bool phoneChanged = !string.IsNullOrWhiteSpace(request.PhoneNumber) && request.PhoneNumber != user.PhoneNumber;
            bool panchayathChanged = !string.IsNullOrWhiteSpace(request.Panchayath) && request.Panchayath != user.Panchayath;

            if (phoneChanged || panchayathChanged)
            {
                string oldPartition = user.PartitionKey;
                string oldRowKey = user.RowKey;

                if (phoneChanged)
                {
                    var existing = await _userRepository.QueryAsync(u => u.RowKey == request.PhoneNumber);
                    if (existing.Any(u => u.UserId != userId))
                        return Conflict("A user with this phone number already exists.");

                    user.PhoneNumber = request.PhoneNumber!;
                    user.RowKey = request.PhoneNumber!;
                }

                if (panchayathChanged)
                {
                    user.Panchayath = request.Panchayath!;
                    user.PartitionKey = request.Panchayath!;
                }

                await _userRepository.DeleteAsync(oldPartition, oldRowKey);
                await _userRepository.AddAsync(user);
            }
            else
            {
                await _userRepository.UpdateAsync(user);
            }

            return Ok(new
            {
                Message = "User updated successfully.",
                User = MapToResponse(user),
                NewPassword = changedPassword
            });
        }

        [HttpPost("{userId}/reset-password")]
        [Authorize(Roles = "Admin,Coordinator,WardCommittee")]
        public async Task<IActionResult> ResetPassword(string userId, [FromBody] ResetPasswordRequest? request)
        {
            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value
                          ?? User.FindFirst("role")?.Value;
            var cleanUserId = (userId ?? string.Empty).Trim();
            var phoneWithCountry = cleanUserId.StartsWith("+91") ? cleanUserId : "+91" + cleanUserId.TrimStart('+');
            var cleanDigits = cleanUserId.Replace("+91", "").Trim();

            var users = await _userRepository.QueryAsync(u =>
                u.UserId == cleanUserId ||
                u.PhoneNumber == cleanUserId ||
                u.PhoneNumber == phoneWithCountry ||
                u.PhoneNumber == cleanDigits ||
                u.RowKey == cleanUserId ||
                u.RowKey == phoneWithCountry);
            var user = users.FirstOrDefault();

            if (user == null) return NotFound("User not found.");

            // Coordinators and Ward Committees can only reset passwords for volunteers
            if (callerRole != null && (callerRole.Equals("Coordinator", StringComparison.OrdinalIgnoreCase) || callerRole.Equals("WardCommittee", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.Equals(user.Role, "Volunteer", StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid("You are only permitted to change or reset passwords for Volunteers.");
                }
            }
            else if (!string.Equals(callerRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            string newPassword = !string.IsNullOrWhiteSpace(request?.NewPassword)
                ? request.NewPassword.Trim()
                : GenerateDefaultPassword(user.FullName, user.PhoneNumber);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _userRepository.UpdateAsync(user);

            return Ok(new
            {
                Message = $"Password reset successfully for {user.FullName}.",
                NewPassword = newPassword,
                UserId = user.UserId,
                PhoneNumber = user.PhoneNumber,
                FullName = user.FullName
            });
        }

        [HttpDelete("{userId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var users = await _userRepository.QueryAsync(u => u.UserId == userId);
            var user = users.FirstOrDefault();

            if (user == null) return NotFound("User not found.");

            user.IsActive = false;
            await _userRepository.UpdateAsync(user);

            return Ok(new { Message = $"User {user.FullName} deactivated successfully." });
        }

        // Helper method to keep your endpoints clean
        private static UserResponse MapToResponse(UserEntity u)
        {
            return new UserResponse
            {
                UserId = u.UserId,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                Role = u.Role,
                District = u.District,
                TargetAmount = u.TargetAmount,
                TargetKits = u.TargetKits,
                Panchayath = u.Panchayath,
                WardNumber = u.WardNumber,
                ParentUserId = u.ParentUserId
            };
        }

        public static string GenerateDefaultPassword(string fullName, string phoneNumber)
        {
            const string chars = "23456789abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private string? GetCurrentUserId()
        {
            return User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("UserId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}