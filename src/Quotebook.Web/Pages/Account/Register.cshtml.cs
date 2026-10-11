using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quotebook.Core;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Account;

public class RegisterModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(100)] public string DisplayName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // A new sign-up opens a new business and is its owner, so the business id is the user's own id.
        var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email, DisplayName = Input.DisplayName };
        user.TenantId = user.Id;

        var created = await users.CreateAsync(user, Input.Password);
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        await users.AddToRoleAsync(user, Roles.Owner);
        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Dashboard");
    }
}
