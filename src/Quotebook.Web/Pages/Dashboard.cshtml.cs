using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core.Entities;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages;

public class DashboardModel(QuotebookDbContext db, MalaysiaTimeProvider clock) : PageModel
{
    public record InvoiceRow(Invoice Invoice, InvoiceStatus Status);

    public DateOnly Today { get; private set; }

    /// <summary>Money still owed on invoices that are not fully paid.</summary>
    public decimal Outstanding { get; private set; }
    public int OutstandingCount { get; private set; }

    public decimal OverdueAmount { get; private set; }
    public int OverdueCount { get; private set; }

    /// <summary>Quotations sent and waiting for the customer to answer.</summary>
    public decimal AwaitingReplyAmount { get; private set; }
    public int AwaitingReplyCount { get; private set; }

    public decimal CollectedThisMonth { get; private set; }

    public List<Invoice> OverdueInvoices { get; private set; } = [];

    /// <summary>Accepted quotations that have not been turned into an invoice yet.</summary>
    public List<Quote> ReadyToInvoice { get; private set; } = [];

    public List<InvoiceRow> Recent { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var today = Today = clock.Today;

        // A small business has hundreds of invoices, not millions, so totals are worked out in memory
        // with the same domain rules the rest of the app uses. No SQL copy of those rules to drift.
        var invoices = await db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.Payments).Include(i => i.Customer).ToListAsync();
        var open = invoices.Where(i => i.StatusOn(today) != InvoiceStatus.Paid).ToList();
        var overdue = open.Where(i => i.StatusOn(today) == InvoiceStatus.Overdue).ToList();

        Outstanding = open.Sum(i => i.Balance);
        OutstandingCount = open.Count;
        OverdueAmount = overdue.Sum(i => i.Balance);
        OverdueCount = overdue.Count;
        OverdueInvoices = overdue.OrderBy(i => i.DueOn).Take(5).ToList();

        CollectedThisMonth = invoices
            .SelectMany(i => i.Payments)
            .Where(p => p.PaidOn.Year == today.Year && p.PaidOn.Month == today.Month)
            .Sum(p => p.Amount);

        Recent = invoices
            .OrderByDescending(i => i.IssuedOn).ThenByDescending(i => i.Id)
            .Take(5)
            .Select(i => new InvoiceRow(i, i.StatusOn(today)))
            .ToList();

        var sent = await db.Quotes.AsNoTracking().Include(q => q.Lines).Where(q => q.Status == QuoteStatus.Sent).ToListAsync();
        AwaitingReplyCount = sent.Count;
        AwaitingReplyAmount = sent.Sum(q => q.Total);

        ReadyToInvoice = await db.Quotes.AsNoTracking()
            .Include(q => q.Lines).Include(q => q.Customer)
            .Where(q => q.Status == QuoteStatus.Accepted && q.Invoice == null)
            .OrderBy(q => q.IssuedOn)
            .ToListAsync();
    }
}
