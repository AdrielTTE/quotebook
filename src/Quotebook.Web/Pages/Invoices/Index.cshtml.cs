using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core.Entities;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages.Invoices;

public class IndexModel(QuotebookDbContext db, MalaysiaTimeProvider clock) : PageModel
{
    public record Row(Invoice Invoice, InvoiceStatus Status);

    [BindProperty(SupportsGet = true)] public InvoiceStatus? Status { get; set; }
    public List<Row> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Customer).Include(i => i.Lines).Include(i => i.Payments)
            .OrderByDescending(i => i.IssuedOn).ThenByDescending(i => i.Id)
            .ToListAsync();

        // Status depends on today's date and the payments, so it is worked out here, not stored.
        var today = clock.Today;
        Rows = invoices
            .Select(i => new Row(i, i.StatusOn(today)))
            .Where(r => Status is null || r.Status == Status)
            .ToList();
    }
}
