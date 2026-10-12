using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages.Team;

[Authorize(Policy = "OwnerOnly")]
public class IndexModel(UserManager<ApplicationUser> users, ITenantProvider tenant) : PageModel
{
    public record Member(string Id, string DisplayName, string? Email, bool IsOwner, bool Pending);

    public List<Member> Members { get; private set; } = [];

    [BindProperty] public InviteInput Invite { get; set; } = new();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }
    /// <summary>Shown once after inviting: there is no email service, so the owner passes the link on.</summary>
    [TempData] public string? InviteLink { get; set; }

    public class InviteInput
    {
        [Required, StringLength(100)] public string DisplayName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostInviteAsync()
    {
        if (IsDemoUser) { Error = "Inviting staff is switched off in the demo."; return RedirectToPage(); }
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var email = Invite.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = Invite.DisplayName.Trim(), TenantId = tenant.TenantId! };

        // No password yet: the person sets their own through the invite link.
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            // A duplicate email is the usual reason. Say it plainly instead of listing Identity codes.
            ModelState.AddModelError("Invite.Email", "That email already has an account.");
            await LoadAsync();
            return Page();
        }
        await users.AddToRoleAsync(user, Roles.Staff);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        InviteLink = Url.Page("/Account/AcceptInvite", null, new { userId = user.Id, token }, Request.Scheme);
        Message = $"Invited {user.DisplayName}. Send them the link below. It works once and expires in 1 day.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string id)
    {
        if (IsDemoUser) { Error = "Removing staff is switched off in the demo."; return RedirectToPage(); }

        var user = await users.FindByIdAsync(id);
        // Only staff of this business can be removed: never the owner, never another business's people.
        if (user is null || user.TenantId != tenant.TenantId || !await users.IsInRoleAsync(user, Roles.Staff)) return NotFound();

        await users.DeleteAsync(user);
        Message = $"Removed {user.DisplayName}.";
        return RedirectToPage();
    }

    bool IsDemoUser => string.Equals(User.Identity?.Name, DemoSeeder.Email, StringComparison.OrdinalIgnoreCase);

    async Task LoadAsync()
    {
        var team = await users.Users.AsNoTracking().Where(u => u.TenantId == tenant.TenantId).OrderBy(u => u.DisplayName).ToListAsync();
        Members = [];
        foreach (var u in team)
            Members.Add(new Member(u.Id, u.DisplayName, u.Email, u.Id == tenant.TenantId, u.PasswordHash is null));
    }
}
