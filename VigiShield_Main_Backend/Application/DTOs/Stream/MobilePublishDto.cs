using System.ComponentModel.DataAnnotations;
namespace VigiShield.Application.DTOs.Stream;
public record UpdateNotificationsRequest([Required] bool? Enabled);
public record PublishRequest([Required, StringLength(65536, MinimumLength = 5)] string Sdp);
public record PublishResponse(Guid SessionId, string Sdp);
