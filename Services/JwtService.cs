using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ML.Charity.API.Client.Models;
using System.Security.Claims;
using System.Text;

namespace ML.Charity.API.Client.Services
{
    public class JwtService
    {
        private readonly IConfiguration _configuration;

        public JwtService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateToken(UserEntity user)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var secretKey = _configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                secretKey = _configuration["Jwt__Key"]
                    ?? _configuration["JWT_SECRET_KEY"]
                    ?? _configuration["JWT_KEY"]
                    ?? Environment.GetEnvironmentVariable("Jwt__Key")
                    ?? Environment.GetEnvironmentVariable("Jwt:Key")
                    ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
                    ?? Environment.GetEnvironmentVariable("JWT_KEY");
            }

            if (string.IsNullOrWhiteSpace(secretKey))
            {
                throw new InvalidOperationException("JWT Secret Key is missing in configuration. Ensure 'Jwt:Key' or 'JWT_SECRET_KEY' is configured in environment variables.");
            }

            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var signingKey = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            // Map user hierarchy to JWT claims for BOLA protection
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId),
                new Claim("UserId", user.UserId),
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim("PhoneNumber", user.PhoneNumber),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("role", user.Role),
                new Claim("Panchayath", user.Panchayath),
                new Claim("WardNumber", user.WardNumber.ToString()),
            };

            // Add dynamic hierarchy claims based on role
            if (!string.IsNullOrEmpty(user.ParentUserId))
            {
                if (user.Role == "Volunteer") claims.Add(new Claim("CoordinatorId", user.ParentUserId));
                if (user.Role == "Coordinator") claims.Add(new Claim("WardCommitteeId", user.ParentUserId));
            }

            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(double.Parse(jwtSettings["DurationInMinutes"]!)),
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"],
                SigningCredentials = credentials
            };

            var handler = new JsonWebTokenHandler();
            return handler.CreateToken(descriptor);
        }
    }
}