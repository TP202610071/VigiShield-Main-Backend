using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VigiShield.Common.Extensions;

namespace VigiShield.Common.Security;

/// <summary>Solo usuarios con rol Admin (misma comprobación que los endpoints
/// de administración existentes: <c>User.IsAdmin()</c>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SoloAdminAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.User.IsAdmin()) context.Result = new ForbidResult();
    }
}
