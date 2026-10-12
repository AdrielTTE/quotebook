using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Account;

/// <summary>Where an invited staff member sets their password. The link carries a one-time token.</summary>
public class AcceptInviteModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    public string? DisplayName { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await FindAsync();
        if (user is null) return NotFound();
        DisplayName = user.DisplayName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await FindAsync();
        if (user is null) return NotFound();
        DisplayName = user.DisplayName;
        if (!ModelState.IsValid) return Page();

        var result = await users.ResetPasswordAsync(user, Token!, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Code == "InvalidToken" ? "This invite link has expired or was already used. Ask for a new one." : e.Description);
            return Page();
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Dashboard");
    }

    // An unknown user and a user who already has a password look the same, so the link reveals nothing.
    async Task<ApplicationUser?> FindAsync()
    {
        if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token)) return null;
        var user = await users.FindByIdAsync(UserId);
        return user is { PasswordHash: null } ? user : null;
    }
}
