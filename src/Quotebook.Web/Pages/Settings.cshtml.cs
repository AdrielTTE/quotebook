using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Core.Entities;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages;

[Authorize(Policy = "OwnerOnly")]
public class SettingsModel(QuotebookDbContext db, ITenantProvider tenant) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    [TempData] public string? Message { get; set; }

    public class InputModel
    {
        [Required, StringLength(200)] public string Name { get; set; } = "";
        [StringLength(500)] public string? Address { get; set; }
        [StringLength(50)] public string? Phone { get; set; }
        [EmailAddress, StringLength(320)] public string? Email { get; set; }
    }

    public async Task OnGetAsync()
    {
        var business = await db.Businesses.AsNoTracking().FirstOrDefaultAsync();
        if (business is not null)
            Input = new InputModel { Name = business.Name, Address = business.Address, Phone = business.Phone, Email = business.Email };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Businesses that signed up before this existed have no row yet; create it on first save.
        var business = await db.Businesses.FirstOrDefaultAsync();
        if (business is null)
        {
            business = new Business { Id = tenant.TenantId! };
            db.Businesses.Add(business);
        }

        business.Name = Input.Name.Trim();
        business.Address = Clean(Input.Address);
        business.Phone = Clean(Input.Phone);
        business.Email = Clean(Input.Email);
        await db.SaveChangesAsync();

        Message = "Saved. New and reprinted documents use these details.";
        return RedirectToPage();
    }

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
