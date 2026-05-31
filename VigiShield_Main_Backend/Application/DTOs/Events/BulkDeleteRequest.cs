using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Events;

public record BulkDeleteRequest([Required, MinLength(1)] List<Guid> EventIds);
