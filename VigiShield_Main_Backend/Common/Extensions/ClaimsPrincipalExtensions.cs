using System.Security.Claims;

namespace VigiShield.Common.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid GetHouseholdId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue("householdId")!);

    public static bool IsPrimaryResident(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Role) == "Primary";
}
