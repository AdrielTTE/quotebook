using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Quotebook.Data;

namespace Quotebook.Web.Infrastructure;

/// <summary>Puts the user's business id into the sign-in cookie, so a request needs no extra lookup.</summary>
public sealed class TenantClaimsFactory(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    public const string DisplayNameClaim = "display_name";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ClaimsTenantProvider.ClaimType, user.TenantId));
        // For the header ("Demo Owner" and the initials), so a page needs no user lookup to show them.
        identity.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));
        return identity;
    }
}
