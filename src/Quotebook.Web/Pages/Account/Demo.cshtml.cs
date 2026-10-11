using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages.Account;

public class DemoModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AppFeatures features) : PageModel
{
    // POST only, like sign-out: no link on another site can start a session.
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        if (!features.DemoEnabled) return NotFound();

        var demo = await users.FindByEmailAsync(DemoSeeder.Email);
        if (demo is null) return NotFound(); // not seeded yet

        await signIn.SignInAsync(demo, isPersistent: false);
        return RedirectToPage("/Dashboard");
    }
}
