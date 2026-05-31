using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Faces;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/faces")]
[Authorize]
public class FacesController : ControllerBase
{
    private readonly FaceService _faceService;

    public FacesController(FaceService faceService) => _faceService = faceService;

    [HttpGet]
    public async Task<ActionResult<List<FaceDto>>> GetFaces()
    {
        return Ok(await _faceService.GetFacesAsync(User.GetHouseholdId()));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<FaceDto>> AddFace(
        [FromForm] string personName,
        [FromForm] List<IFormFile> photos)
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        var result = await _faceService.AddFaceAsync(User.GetHouseholdId(), personName, photos);
        return CreatedAtAction(nameof(GetFaces), result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFace(Guid id)
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        await _faceService.DeleteFaceAsync(User.GetHouseholdId(), id);
        return NoContent();
    }
}
