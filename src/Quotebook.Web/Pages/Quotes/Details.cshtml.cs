using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Core.Entities;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages.Quotes;

public class DetailsModel(QuotebookDbContext db, MalaysiaTimeProvider clock) : PageModel
{
    public Quote Quote { get; private set; } = null!;
    public Business? Business { get; private set; }

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var quote = await Load(id, track: false);
        if (quote is null) return NotFound();
        Quote = quote;
        Business = await db.Businesses.AsNoTracking().FirstOrDefaultAsync();
        return Page();
    }

    public Task<IActionResult> OnPostSendAsync(int id) => Transition(id, q => q.Send(), "Marked as sent.");
    public Task<IActionResult> OnPostAcceptAsync(int id) => Transition(id, q => q.Accept(clock.Today), "Quotation accepted.");
    public Task<IActionResult> OnPostRejectAsync(int id) => Transition(id, q => q.Reject(), "Quotation rejected.");

    /// <summary>Turns an accepted quotation into an invoice and opens it.</summary>
    public async Task<IActionResult> OnPostInvoiceAsync(int id, DateOnly? dueOn)
    {
        var quote = await Load(id, track: true);
        if (quote is null) return NotFound();

        try
        {
            var invoiceNumber = await DocumentNumbers.NextAsync(db.Invoices.Select(i => i.Number), "INV");
            var invoice = quote.ConvertToInvoice(invoiceNumber, clock.Today, dueOn ?? clock.Today.AddDays(30));
            db.Invoices.Add(invoice); // OwnerId comes from the quote; the DbContext checks it matches the business
            await db.SaveChangesAsync();
            TempData["Message"] = $"{invoice.Number} created from {quote.Number}.";
            return RedirectToPage("/Invoices/Details", new { id = invoice.Id });
        }
        catch (DomainException e)
        {
            Error = e.Message;
        }
        catch (DbUpdateException)
        {
            // The unique index on Invoices.QuoteId: a second click, or a second tab, got there first.
            Error = "This quotation has already been invoiced.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var quote = await Load(id, track: true);
        if (quote is null) return NotFound();

        // Only drafts can go. Anything sent is a record the customer may hold.
        if (quote.Status != QuoteStatus.Draft)
        {
            Error = "Only draft quotations can be deleted.";
            return RedirectToPage(new { id });
        }

        db.Quotes.Remove(quote);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Deleted {quote.Number}.";
        return RedirectToPage("Index");
    }

    async Task<IActionResult> Transition(int id, Action<Quote> change, string success)
    {
        var quote = await Load(id, track: true);
        if (quote is null) return NotFound();

        try
        {
            change(quote);
            await db.SaveChangesAsync();
            Message = success;
        }
        catch (DomainException e)
        {
            Error = e.Message; // the rule that was broken, in plain words
        }
        catch (DbUpdateConcurrencyException)
        {
            Error = "Someone else changed this quotation. Reloaded the latest, please check it and try again.";
        }

        return RedirectToPage(new { id });
    }

    Task<Quote?> Load(int id, bool track)
    {
        var query = db.Quotes.Include(q => q.Customer).Include(q => q.Lines).Include(q => q.Invoice).AsQueryable();
        if (!track) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(q => q.Id == id);
    }
}
