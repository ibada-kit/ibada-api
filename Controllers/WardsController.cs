using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ML.Charity.API.Client.DTOs;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WardsController : ControllerBase
    {
        private const string DEFAULT_PANCHAYATH = "Madavoor";
        private readonly ITableStorageService<WardEntity> _wardRepository;

        // Default authentic ward names for Madavoor Panchayath (Kozhikode, Kerala)
        private static readonly Dictionary<int, string> DefaultMadavoorWardNames = new()
        {
            { 1, "Ankathayi" },
            { 2, "Eravannur North" },
            { 3, "Eravannur South" },
            { 4, "Nariyachal" },
            { 5, "Pullaloor" },
            { 6, "Eranhukunnu" },
            { 7, "Rampoyil" },
            { 8, "Madavoor" },
            { 9, "Madavoormukku" },
            { 10, "Paimbalassery" },
            { 11, "Kottakkavayal" },
            { 12, "Arambram" }
        };

        public WardsController(ITableStorageService<WardEntity> wardRepository)
        {
            _wardRepository = wardRepository;
        }

        /// <summary>
        /// Get all wards for a given panchayath.
        /// Auto-seeds Wards 1 to 12 if table/partition is empty.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetWards([FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            panchayath = string.IsNullOrWhiteSpace(panchayath) ? DEFAULT_PANCHAYATH : panchayath.Trim();

            var wards = await _wardRepository.QueryEntitiesByPartitionAsync(panchayath);

            // Auto-seed initial 12 wards if none exist for this panchayath
            if (!wards.Any())
            {
                var seedWards = Enumerable.Range(1, 12).Select(w => new WardEntity
                {
                    PartitionKey = panchayath,
                    RowKey = w.ToString(),
                    WardNumber = w,
                    WardName = DefaultMadavoorWardNames.TryGetValue(w, out var name) ? name : $"Ward {w}",
                    Panchayath = panchayath
                }).ToList();

                foreach (var ward in seedWards)
                {
                    try
                    {
                        await _wardRepository.AddAsync(ward);
                        wards.Add(ward);
                    }
                    catch
                    {
                        // Ignore collision if already seeded concurrently
                    }
                }
            }

            var result = wards
                .OrderBy(w => w.WardNumber)
                .Select(w =>
                {
                    var resolvedName = !string.IsNullOrWhiteSpace(w.WardName) && !w.WardName.StartsWith("Ward ")
                        ? w.WardName
                        : (DefaultMadavoorWardNames.TryGetValue(w.WardNumber, out var defaultName) ? defaultName : (w.WardName ?? $"Ward {w.WardNumber}"));

                    return new WardDto
                    {
                        WardNumber = w.WardNumber,
                        WardName = resolvedName,
                        Panchayath = w.Panchayath
                    };
                })
                .ToList();

            return Ok(result);
        }

        /// <summary>
        /// Update the name of a specific ward.
        /// </summary>
        [HttpPut("{wardNumber}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateWardName(int wardNumber, [FromBody] UpdateWardNameDto dto, [FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            if (string.IsNullOrWhiteSpace(dto.WardName))
                return BadRequest("Ward name cannot be empty.");

            panchayath = string.IsNullOrWhiteSpace(panchayath) ? DEFAULT_PANCHAYATH : panchayath.Trim();
            var entity = await _wardRepository.GetEntityAsync(panchayath, wardNumber.ToString());

            if (entity == null)
            {
                entity = new WardEntity
                {
                    PartitionKey = panchayath,
                    RowKey = wardNumber.ToString(),
                    WardNumber = wardNumber,
                    WardName = dto.WardName.Trim(),
                    Panchayath = panchayath
                };
                await _wardRepository.AddAsync(entity);
            }
            else
            {
                entity.WardName = dto.WardName.Trim();
                await _wardRepository.UpdateAsync(entity);
            }

            return Ok(new WardDto
            {
                WardNumber = entity.WardNumber,
                WardName = entity.WardName,
                Panchayath = entity.Panchayath
            });
        }

        /// <summary>
        /// Seed or update all wards with standard panchayath names in Azure Table Storage.
        /// </summary>
        [HttpPost("seed-names")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SeedWardNames([FromQuery] string panchayath = DEFAULT_PANCHAYATH)
        {
            panchayath = string.IsNullOrWhiteSpace(panchayath) ? DEFAULT_PANCHAYATH : panchayath.Trim();

            foreach (var kvp in DefaultMadavoorWardNames)
            {
                var entity = await _wardRepository.GetEntityAsync(panchayath, kvp.Key.ToString());
                if (entity == null)
                {
                    await _wardRepository.AddAsync(new WardEntity
                    {
                        PartitionKey = panchayath,
                        RowKey = kvp.Key.ToString(),
                        WardNumber = kvp.Key,
                        WardName = kvp.Value,
                        Panchayath = panchayath
                    });
                }
                else
                {
                    entity.WardName = kvp.Value;
                    await _wardRepository.UpdateAsync(entity);
                }
            }

            return await GetWards(panchayath);
        }
    }
}
