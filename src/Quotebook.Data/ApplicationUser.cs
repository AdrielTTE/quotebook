using Microsoft.AspNetCore.Identity;

namespace Quotebook.Data;

public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// The business this user belongs to. For an owner it is their own Id;
    /// staff carry their owner's Id.
    /// </summary>
    public string TenantId { get; set; } = "";

    public string DisplayName { get; set; } = "";
}
