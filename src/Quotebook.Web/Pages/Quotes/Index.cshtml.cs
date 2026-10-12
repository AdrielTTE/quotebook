using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core.Entities;
using Quotebook.Data;

namespace Quotebook.Web.Pages.Quotes;

public class IndexModel(QuotebookDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<Quote> Quotes { get; private set; } = [];

    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        var query = db.Quotes.AsNoTracking().Include(q => q.Customer).Include(q => q.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim().ToLower();
            query = query.Where(q => q.Number.ToLower().Contains(term) || q.Customer!.Name.ToLower().Contains(term));
        }
        Quotes = await query.OrderByDescending(q => q.IssuedOn).ThenByDescending(q => q.Id).ToListAsync();
    }
}
