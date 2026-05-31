using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Config;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/config")]
[Authorize]
public class ConfigController : ControllerBase
{
    private readonly ConfigService _configService;

    public ConfigController(ConfigService configService) => _configService = configService;

    [HttpGet("alerts")]
    public async Task<ActionResult<AlertConfigDto>> GetAlertConfig()
    {
        return Ok(await _configService.GetAlertConfigAsync(User.GetHouseholdId()));
    }

    [HttpPut("alerts")]
    public async Task<ActionResult<AlertConfigDto>> UpdateAlertConfig([FromBody] UpdateAlertConfigRequest request)
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        return Ok(await _configService.UpdateAlertConfigAsync(User.GetHouseholdId(), request));
    }
}
