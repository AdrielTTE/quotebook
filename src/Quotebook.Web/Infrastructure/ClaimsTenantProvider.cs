using System.Security.Claims;
using Quotebook.Core;

namespace Quotebook.Web.Infrastructure;

/// <summary>Reads the business id from the signed-in user's claims. Anonymous requests get null.</summary>
public sealed class ClaimsTenantProvider(IHttpContextAccessor accessor) : ITenantProvider
{
    public const string ClaimType = "tenant_id";

    public string? TenantId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimType) : null;
        }
    }
}
