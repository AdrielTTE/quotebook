using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core.Entities;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Quotes;

public class IndexModel(QuotebookDbContext db) : PageModel
{
    public List<Quote> Quotes { get; private set; } = [];

    [Microsoft.AspNetCore.Mvc.TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
        => Quotes = await db.Quotes
            .AsNoTracking()
            .Include(q => q.Customer)
            .Include(q => q.Lines)
            .OrderByDescending(q => q.IssuedOn).ThenByDescending(q => q.Id)
            .ToListAsync();
}
