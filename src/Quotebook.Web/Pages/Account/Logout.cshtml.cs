using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Account;

public class LogoutModel(SignInManager<ApplicationUser> signIn) : PageModel
{
    // POST only, so a link or image on another site cannot sign people out.
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return RedirectToPage("/Index");
    }
}
