using System.Security.Claims;

namespace VigiShield.Common.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid GetHouseholdId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue("householdId")!);

    // Admin accounts inherit every Primary-resident power.
    public static bool IsPrimaryResident(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Role) is "Primary" or "Admin";

    public static bool IsAdmin(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Role) == "Admin";
}
