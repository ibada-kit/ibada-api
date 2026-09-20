using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public SettingsController(ITableStorageService<CampaignSettingsEntity> settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }

        /// <summary>
        /// Reads the current campaign relief kit unit price and last modified date from Azure Table Storage.
        /// Available anonymously so public donors, volunteers, and coordinators can calculate totals.
        /// </summary>
        [HttpGet("kit-price")]
        [AllowAnonymous]
        public async Task<IActionResult> GetKitPrice()
        {
            var setting = await _settingsRepository.GetEntityAsync(SETTINGS_PARTITION, KIT_PRICE_ROW);
            if (setting == null)
            {
                return Ok(new KitPriceResponse
                {
                    KitPrice = DEFAULT_KIT_PRICE,
                    ModifiedDate = DateTimeOffset.UtcNow,
                    ModifiedBy = "Default"
                });
            }

            return Ok(new KitPriceResponse
            {
                KitPrice = setting.KitPrice > 0 ? setting.KitPrice : DEFAULT_KIT_PRICE,
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
                ModifiedDate = entity.ModifiedDate,
                ModifiedBy = entity.ModifiedBy
            });
        }
    }
}
