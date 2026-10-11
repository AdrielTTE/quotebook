using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Core.Entities;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages.Invoices;

public class DetailsModel(QuotebookDbContext db, MalaysiaTimeProvider clock) : PageModel
{
    public Invoice Invoice { get; private set; } = null!;
    public InvoiceStatus Status { get; private set; }
    public DateOnly Today => clock.Today;

    [BindProperty] public PaymentInput Payment { get; set; } = new();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public class PaymentInput
    {
        [Range(0.01, 100000000, ErrorMessage = "Enter an amount above zero.")] public decimal Amount { get; set; }
        [Required] public DateOnly PaidOn { get; set; }
        [StringLength(500)] public string? Note { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var invoice = await Load(id, track: false);
        if (invoice is null) return NotFound();

        Show(invoice);
        Payment = new PaymentInput { PaidOn = clock.Today, Amount = invoice.Balance > 0 ? invoice.Balance : 0 };
        return Page();
    }

    public async Task<IActionResult> OnPostPaymentAsync(int id)
    {
        var invoice = await Load(id, track: true);
        if (invoice is null) return NotFound();

        if (!ModelState.IsValid)
        {
            Show(invoice);
            return Page();
        }

        try
        {
            invoice.AddPayment(Payment.Amount, Payment.PaidOn, string.IsNullOrWhiteSpace(Payment.Note) ? null : Payment.Note.Trim());
            TouchForConcurrency(invoice);
            await db.SaveChangesAsync();
            Message = $"Recorded RM {Money.Round(Payment.Amount).Rm()}.";
        }
        catch (DomainException e)
        {
            Error = e.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            Error = "This invoice was changed by someone else just now. Check the balance and try again.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeletePaymentAsync(int id, int paymentId)
    {
        // Taking a payment back rewrites the books, so only the owner can do it. Staff can add payments.
        if (!User.IsInRole(Roles.Owner)) return Forbid();

        var invoice = await Load(id, track: true);
        if (invoice is null) return NotFound();

        try
        {
            var removed = invoice.RemovePayment(paymentId);
            db.Payments.Remove(removed);
            TouchForConcurrency(invoice);
            await db.SaveChangesAsync();
            Message = $"Removed the RM {removed.Amount.Rm()} payment.";
        }
        catch (DomainException e)
        {
            Error = e.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            Error = "This invoice was changed by someone else just now. Check the payments and try again.";
        }

        return RedirectToPage(new { id });
    }

    /// <summary>
    /// A payment is a new child row, so the invoice row itself would not change and its version would
    /// not move. Two people paying at the same moment could then both pass the "not more than owed"
    /// check. Touching the invoice row forces an UPDATE; the second one is rejected.
    /// </summary>
    void TouchForConcurrency(Invoice invoice) => db.Entry(invoice).Property(i => i.Number).IsModified = true;

    void Show(Invoice invoice)
    {
        Invoice = invoice;
        Status = invoice.StatusOn(clock.Today);
    }

    Task<Invoice?> Load(int id, bool track)
    {
        var query = db.Invoices
            .Include(i => i.Customer).Include(i => i.Lines).Include(i => i.Payments).Include(i => i.Quote)
            .AsQueryable();
        if (!track) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(i => i.Id == id);
    }
}
