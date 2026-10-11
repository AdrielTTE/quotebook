using Microsoft.AspNetCore.Identity;
using Quotebook.Core;

namespace Quotebook.Web.Infrastructure;

public static class RoleSeeder
{
    /// <summary>Creates the Owner and Staff roles if they are missing. Safe to run on every start.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in new[] { Roles.Owner, Roles.Staff })
        {
            if (!await roles.RoleExistsAsync(name))
                await roles.CreateAsync(new IdentityRole(name));
        }
    }
}
