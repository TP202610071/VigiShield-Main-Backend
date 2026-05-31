using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Faces;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Application.Services;

public class FaceService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public FaceService(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<List<FaceDto>> GetFacesAsync(Guid householdId)
    {
        return await _db.AuthorizedFaces
            .Where(f => f.HouseholdId == householdId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => ToDto(f))
            .ToListAsync();
    }

    public async Task<FaceDto> AddFaceAsync(Guid householdId, string personName, List<IFormFile> photos)
    {
        if (string.IsNullOrWhiteSpace(personName))
            throw new AppException("El nombre de la persona es requerido");

        if (photos.Count < 3)
            throw new AppException("Se requieren al menos 3 fotos para el reconocimiento facial");

        var uploadDir = Path.Combine(
            _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"),
            "uploads", "faces", householdId.ToString());

        Directory.CreateDirectory(uploadDir);

        var photoPaths = new List<string>();
        foreach (var photo in photos)
        {
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png"))
                throw new AppException("Solo se permiten imágenes JPG o PNG");

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadDir, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await photo.CopyToAsync(stream);

            photoPaths.Add($"/uploads/faces/{householdId}/{fileName}");
        }

        var face = new AuthorizedFace
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            PersonName = personName.Trim(),
            PhotoPathsJson = JsonSerializer.Serialize(photoPaths),
            CreatedAt = DateTime.UtcNow
        };

        _db.AuthorizedFaces.Add(face);
        await _db.SaveChangesAsync();

        return ToDto(face);
    }

    public async Task DeleteFaceAsync(Guid householdId, Guid faceId)
    {
        var face = await _db.AuthorizedFaces
            .FirstOrDefaultAsync(f => f.Id == faceId && f.HouseholdId == householdId)
            ?? throw AppException.NotFound("Perfil facial no encontrado");

        _db.AuthorizedFaces.Remove(face);
        await _db.SaveChangesAsync();
    }

    private static FaceDto ToDto(AuthorizedFace face)
    {
        var photos = JsonSerializer.Deserialize<List<string>>(face.PhotoPathsJson) ?? [];
        return new FaceDto(face.Id, face.PersonName, photos, face.CreatedAt);
    }
}
