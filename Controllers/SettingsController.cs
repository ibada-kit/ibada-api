using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SettingsController : ControllerBase
    {
        public const string SETTINGS_PARTITION = "GLOBAL";
        public const string KIT_PRICE_ROW = "KitPrice";
        public const double DEFAULT_KIT_PRICE = 1000.0;

        private readonly ITableStorageService<CampaignSettingsEntity> _settingsRepository;
        private readonly IConfiguration? _configuration;

        public SettingsController(
            ITableStorageService<CampaignSettingsEntity> settingsRepository,
            IConfiguration? configuration = null)
        {
            _settingsRepository = settingsRepository;
            _configuration = configuration;
        }

        private bool GetConfigDefaultBlocked()
        {
            if (_configuration != null)
            {
                var val = _configuration["CampaignSettings:BlockWardKitCollection"]
                       ?? _configuration["BlockWardKitCollection"];
                if (bool.TryParse(val, out var b))
                {
                    return b;
                }
            }
            return false;
        }

        /// <summary>
        /// Reads the current campaign relief kit unit price, block ward collection status, and last modified date.
        /// Available anonymously so public donors, volunteers, and coordinators can calculate totals.
        /// </summary>
        [HttpGet("kit-price")]
        [AllowAnonymous]
        public async Task<IActionResult> GetKitPrice()
        {
            var configDefaultBlocked = GetConfigDefaultBlocked();
            var setting = await _settingsRepository.GetEntityAsync(SETTINGS_PARTITION, KIT_PRICE_ROW);
            if (setting == null)
            {
                return Ok(new KitPriceResponse
                {
                    KitPrice = DEFAULT_KIT_PRICE,
                    BlockWardKitCollection = configDefaultBlocked,
                    ModifiedDate = DateTimeOffset.UtcNow,
                    ModifiedBy = "Default"
                });
            }

            return Ok(new KitPriceResponse
            {
                KitPrice = setting.KitPrice > 0 ? setting.KitPrice : DEFAULT_KIT_PRICE,
                BlockWardKitCollection = setting.BlockWardKitCollection,
                ModifiedDate = setting.ModifiedDate != default ? setting.ModifiedDate : (setting.Timestamp ?? DateTimeOffset.UtcNow),
                ModifiedBy = !string.IsNullOrWhiteSpace(setting.ModifiedBy) ? setting.ModifiedBy : "Admin"
            });
        }

        /// <summary>
        /// Updates the global campaign kit price. Restricted to Admins.
        /// Records the new price, current timestamp (ModifiedDate), and modifier identity in Azure Table Storage.
        /// </summary>
        [HttpPut("kit-price")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateKitPrice([FromBody] UpdateKitPriceRequest request)
        {
            if (request == null || request.KitPrice <= 0)
            {
                return BadRequest("Kit price must be greater than zero.");
            }

            var adminName = User.FindFirst(ClaimTypes.Name)?.Value
                         ?? User.FindFirst("name")?.Value
                         ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                         ?? User.FindFirst("sub")?.Value
                         ?? User.FindFirst("UserId")?.Value
                         ?? "Admin";

            var existing = await _settingsRepository.GetEntityAsync(SETTINGS_PARTITION, KIT_PRICE_ROW);
            var entity = existing ?? new CampaignSettingsEntity
            {
                PartitionKey = SETTINGS_PARTITION,
                RowKey = KIT_PRICE_ROW
            };

            entity.KitPrice = request.KitPrice;
            entity.ModifiedDate = DateTimeOffset.UtcNow;
            entity.ModifiedBy = adminName;

            await _settingsRepository.UpsertAsync(entity);

            return Ok(new KitPriceResponse
            {
                KitPrice = entity.KitPrice,
                BlockWardKitCollection = entity.BlockWardKitCollection,
                ModifiedDate = entity.ModifiedDate,
                ModifiedBy = entity.ModifiedBy
            });
        }

        /// <summary>
        /// Updates the status for blocking new kit collections by Ward Committees and Ward Volunteers.
        /// Restricted to Admins.
        /// </summary>
        [HttpPut("ward-kit-collection-status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateWardKitCollectionStatus([FromBody] UpdateWardKitCollectionStatusRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request cannot be null.");
            }

            var adminName = User.FindFirst(ClaimTypes.Name)?.Value
                         ?? User.FindFirst("name")?.Value
                         ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                         ?? User.FindFirst("sub")?.Value
                         ?? User.FindFirst("UserId")?.Value
                         ?? "Admin";

            var existing = await _settingsRepository.GetEntityAsync(SETTINGS_PARTITION, KIT_PRICE_ROW);
            var entity = existing ?? new CampaignSettingsEntity
            {
                PartitionKey = SETTINGS_PARTITION,
                RowKey = KIT_PRICE_ROW
            };

            entity.BlockWardKitCollection = request.BlockWardKitCollection;
            entity.ModifiedDate = DateTimeOffset.UtcNow;
            entity.ModifiedBy = adminName;

            await _settingsRepository.UpsertAsync(entity);

            return Ok(new KitPriceResponse
            {
                KitPrice = entity.KitPrice > 0 ? entity.KitPrice : DEFAULT_KIT_PRICE,
                BlockWardKitCollection = entity.BlockWardKitCollection,
                ModifiedDate = entity.ModifiedDate,
                ModifiedBy = entity.ModifiedBy
            });
        }
    }
}
