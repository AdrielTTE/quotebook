using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core.Entities;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Customers;

/// <summary>Adds a customer (no id in the route) or edits one (id in the route).</summary>
public class EditModel(QuotebookDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(200)] public string Name { get; set; } = "";
        [EmailAddress, StringLength(320)] public string? Email { get; set; }
        [StringLength(50)] public string? Phone { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is null) return Page();

        // The tenant filter means another business's customer simply isn't found.
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == Id);
        if (customer is null) return NotFound();

        Input = new InputModel { Name = customer.Name, Email = customer.Email, Phone = customer.Phone };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        Customer? customer;
        if (Id is null)
        {
            customer = new Customer();
            db.Customers.Add(customer); // OwnerId is stamped by the DbContext
        }
        else
        {
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == Id);
            if (customer is null) return NotFound();
        }

        customer.Name = Input.Name.Trim();
        customer.Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        customer.Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim();

        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
