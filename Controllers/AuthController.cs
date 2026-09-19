using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace ML.Charity.API.Client.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ITableStorageService<UserEntity> _userRepository;
        private readonly JwtService _jwtService;
        private readonly IConfiguration? _configuration;

        public AuthController(ITableStorageService<UserEntity> userRepository, JwtService jwtService, IConfiguration? configuration = null)
        {
            _userRepository = userRepository;
            _jwtService = jwtService;
            _configuration = configuration;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Query across partitions for the phone number (RowKey)
            var users = await _userRepository.QueryAsync(u => u.RowKey == request.PhoneNumber);
            var user = users.FirstOrDefault();

            if (user == null || !user.IsActive)
                return Unauthorized(new { Message = "Invalid phone number or inactive account." });

            // Enforce standard, secure, single-pass case-sensitive verification
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

            if (!isPasswordValid)
                return Unauthorized(new { Message = "Invalid password." });

            var token = _jwtService.GenerateToken(user);
            double duration = _configuration != null && double.TryParse(_configuration["Jwt:DurationInMinutes"], out var d)
                ? d
                : _jwtService.DurationInMinutes;

            return Ok(new LoginResponse
            {
                Token = token,
                Role = user.Role,
                FullName = user.FullName,
                UserId = user.UserId,
                PhoneNumber = user.PhoneNumber,
                Panchayath = user.Panchayath,
                WardNumber = user.WardNumber,
                TargetKits = user.TargetKits,
                TargetAmount = user.TargetAmount,
                NeedsPasswordChange = false, // You can add logic here if you track password changes
                ExpiresAt = DateTime.UtcNow.AddMinutes(duration) // Match JWT settings
            });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var identifier = User.FindFirst("UserId")?.Value
                          ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                          ?? User.FindFirst("PhoneNumber")?.Value
                          ?? User.Identity?.Name;
            var users = await _userRepository.QueryAsync(u => u.UserId == identifier || u.RowKey == identifier);
            var user = users.FirstOrDefault();

            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
                return BadRequest(new { Message = "Incorrect old password." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            await _userRepository.UpdateAsync(user);

            return Ok(new { Message = "Password updated successfully." });
        }
    }
}