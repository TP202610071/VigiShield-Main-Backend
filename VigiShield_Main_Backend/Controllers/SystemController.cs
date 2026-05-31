using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.System;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/system")]
[Authorize]
public class SystemController : ControllerBase
{
    private readonly SystemService _systemService;

    public SystemController(SystemService systemService) => _systemService = systemService;

    [HttpGet("status")]
    public async Task<ActionResult<SystemStatusDto>> GetStatus()
    {
        return Ok(await _systemService.GetStatusAsync(User.GetHouseholdId()));
    }

    [HttpPut("pause")]
    public async Task<IActionResult> PauseMonitoring()
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        await _systemService.PauseMonitoringAsync(User.GetHouseholdId());
        return NoContent();
    }

    [HttpPut("resume")]
    public async Task<IActionResult> ResumeMonitoring()
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        await _systemService.ResumeMonitoringAsync(User.GetHouseholdId());
        return NoContent();
    }
}
