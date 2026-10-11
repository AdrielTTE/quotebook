using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Core.Entities;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Web.Pages.Quotes;

/// <summary>Creates a quotation (no id) or edits a draft one (id in the route).</summary>
public class EditModel(QuotebookDbContext db, MalaysiaTimeProvider clock) : PageModel
{
    const int BlankRows = 3;

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> Customers { get; private set; } = [];
    public string? Number { get; private set; }

    public class InputModel
    {
        [Range(1, int.MaxValue, ErrorMessage = "Choose a customer.")] public int CustomerId { get; set; }
        [Required] public DateOnly IssuedOn { get; set; }
        [Required] public DateOnly ValidUntil { get; set; }
        /// <summary>Concurrency token read when the page loaded. A different value at save time means someone else edited it.</summary>
        public uint Version { get; set; }
        public List<LineInput> Lines { get; set; } = [];
    }

    public class LineInput
    {
        public string? Description { get; set; }
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is null)
        {
            Input = new InputModel { IssuedOn = clock.Today, ValidUntil = clock.Today.AddDays(14) };
        }
        else
        {
            var quote = await db.Quotes.AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == Id);
            if (quote is null) return NotFound();
            if (quote.Status != QuoteStatus.Draft) return RedirectToPage("Details", new { id = Id });

            Number = quote.Number;
            Input = new InputModel
            {
                CustomerId = quote.CustomerId,
                IssuedOn = quote.IssuedOn,
                ValidUntil = quote.ValidUntil,
                Version = quote.Version,
                Lines = quote.Lines.Select(l => new LineInput { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList(),
            };
        }

        PadBlankRows();
        await LoadCustomersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Rows left empty in the form are not lines.
        Input.Lines = Input.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)).ToList();

        if (Input.ValidUntil < Input.IssuedOn)
            ModelState.AddModelError("Input.ValidUntil", "Valid until cannot be before the issue date.");
        if (Input.Lines.Count == 0)
            ModelState.AddModelError(string.Empty, "Add at least one line with a description.");

        var lines = Input.Lines.Select(l => new LineItem { Description = l.Description!.Trim(), Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList();
        foreach (var line in lines)
        {
            try { line.Validate(); }
            catch (DomainException e) { ModelState.AddModelError(string.Empty, $"\"{line.Description}\": {e.Message}"); }
        }

        // The tenant filter hides other businesses' customers, so a forged CustomerId is rejected here.
        if (!await db.Customers.AnyAsync(c => c.Id == Input.CustomerId))
            ModelState.AddModelError("Input.CustomerId", "Choose a customer.");

        if (!ModelState.IsValid) return await ReshowAsync();

        Quote quote;
        if (Id is null)
        {
            quote = new Quote { Number = await DocumentNumbers.NextAsync(db.Quotes.Select(q => q.Number), "Q") };
            db.Quotes.Add(quote); // OwnerId is stamped by the DbContext
        }
        else
        {
            var existing = await db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == Id);
            if (existing is null) return NotFound();
            if (existing.Status != QuoteStatus.Draft) return RedirectToPage("Details", new { id = Id });
            quote = existing;
            // Compare against the version the user started from, not the one just loaded.
            db.Entry(quote).Property(q => q.Version).OriginalValue = Input.Version;
            // Editing lines alone would leave this row untouched, so its version would never move and a stale
            // second editor would slip through. Marking a column modified forces an UPDATE, which bumps xmin.
            db.Entry(quote).Property(q => q.Number).IsModified = true;
            db.RemoveRange(quote.Lines);
            quote.Lines.Clear();
        }

        quote.CustomerId = Input.CustomerId;
        quote.IssuedOn = Input.IssuedOn;
        quote.ValidUntil = Input.ValidUntil;
        quote.Lines.AddRange(lines);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty, "Someone else changed this quotation while you were editing. Reload the page and try again.");
            return await ReshowAsync();
        }

        return RedirectToPage("Details", new { id = quote.Id });
    }

    async Task<IActionResult> ReshowAsync()
    {
        if (Id is not null)
            Number = (await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == Id))?.Number;
        PadBlankRows();
        await LoadCustomersAsync();
        return Page();
    }

    void PadBlankRows()
    {
        while (Input.Lines.Count < BlankRows) Input.Lines.Add(new LineInput());
        Input.Lines.Add(new LineInput()); // one spare row to type into
    }

    async Task LoadCustomersAsync()
        => Customers = await db.Customers.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
}
