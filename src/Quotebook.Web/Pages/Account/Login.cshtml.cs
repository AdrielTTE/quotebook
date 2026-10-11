using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Account;

public class LoginModel(SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid) return Page();

        var result = await signIn.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Dashboard");

        // One message for wrong email and wrong password, so the form never reveals which accounts exist.
        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "Too many attempts. Try again in a few minutes."
            : "Email or password is incorrect.");
        return Page();
    }
}
