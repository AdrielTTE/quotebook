using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core.Entities;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Customers;

public class IndexModel(QuotebookDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<Customer> Customers { get; private set; } = [];

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term)
                || (c.Email != null && c.Email.ToLower().Contains(term))
                || (c.Phone != null && c.Phone.Contains(term)));
        }
        Customers = await query.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();

        // Quotes and invoices point at the customer with a restricted foreign key. Check first so
        // the owner gets a plain explanation instead of a database error.
        if (await db.Quotes.AnyAsync(q => q.CustomerId == id) || await db.Invoices.AnyAsync(i => i.CustomerId == id))
        {
            Error = $"{customer.Name} has quotes or invoices, so they can't be deleted.";
            return RedirectToPage();
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync();
        Message = $"Deleted {customer.Name}.";
        return RedirectToPage();
    }
}
