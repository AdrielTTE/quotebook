using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Core.Entities;
using Quotebook.Data;

namespace Quotebook.Web.Infrastructure;

/// <summary>
/// Keeps a demo business filled with realistic sample data covering every status, so a visitor can
/// look around in one click. Re-running wipes that business and starts again, so nothing a visitor
/// does lasts. Other businesses are never touched.
/// </summary>
public sealed class DemoSeeder(IServiceScopeFactory scopes, MalaysiaTimeProvider clock)
{
    public const string Email = "demo@quotebook.demo";

    public async Task ResetAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var user = await EnsureUserAsync(sp.GetRequiredService<UserManager<ApplicationUser>>());

        // A context locked to the demo business, so the tenant filters and save checks apply as normal.
        using var db = new QuotebookDbContext(sp.GetRequiredService<DbContextOptions<QuotebookDbContext>>(), new FixedTenant(user.TenantId));

        await WipeAsync(db, ct);
        var business = await db.Businesses.FirstOrDefaultAsync(ct);
        if (business is null) db.Businesses.Add(business = new Business { Id = user.TenantId });
        business.Name = "Demo Fit-Out & Services";
        business.Address = "12 Jalan Contoh, Taman Demo\n50000 Kuala Lumpur";
        business.Phone = "03-1234 5678";
        business.Email = "hello@demo-fitout.example";
        Seed(db, clock.Today);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The demo owner. Nobody signs in with a password: the Try-the-demo button signs in for them.</summary>
    static async Task<ApplicationUser> EnsureUserAsync(UserManager<ApplicationUser> users)
    {
        var existing = await users.FindByEmailAsync(Email);
        if (existing is not null) return existing;

        var user = new ApplicationUser { UserName = Email, Email = Email, DisplayName = "Demo Owner" };
        user.TenantId = user.Id;
        var created = await users.CreateAsync(user, RandomPassword());
        if (!created.Succeeded)
            throw new InvalidOperationException("Could not create the demo user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, Roles.Owner);
        return user;
    }

    static string RandomPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    static async Task WipeAsync(QuotebookDbContext db, CancellationToken ct)
    {
        // Children first: invoices and quotes point at customers with restricted foreign keys.
        db.Invoices.RemoveRange(await db.Invoices.Include(i => i.Lines).Include(i => i.Payments).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        db.Quotes.RemoveRange(await db.Quotes.Include(q => q.Lines).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        db.Customers.RemoveRange(await db.Customers.ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    static void Seed(QuotebookDbContext db, DateOnly today)
    {
        var ali = new Customer { Name = "Ali Aircon Services", Email = "ali@example.com", Phone = "012-345 6789" };
        var bala = new Customer { Name = "Bala Renovation", Email = "bala@example.com", Phone = "013-222 3344" };
        var chong = new Customer { Name = "Chong Photography", Email = "chong@example.com" };
        var dewi = new Customer { Name = "Dewi Catering", Phone = "016-777 8899" };
        db.Customers.AddRange(ali, bala, chong, dewi);

        // Overdue: RM240, due 15 days ago, nothing paid.
        var q1 = Accepted(db, "Q-0001", ali, today.AddDays(-50), today, [("Aircon servicing (per unit)", 3, 80m)]);
        db.Invoices.Add(q1.ConvertToInvoice("INV-0001", today.AddDays(-45), today.AddDays(-15)));

        // Partly paid: RM2,450 with RM1,000 received.
        var q2 = Accepted(db, "Q-0002", bala, today.AddDays(-25), today, [("Bathroom tile repair", 1, 1800m), ("Materials", 1, 650m)]);
        var inv2 = q2.ConvertToInvoice("INV-0002", today.AddDays(-20), today.AddDays(10));
        inv2.AddPayment(1000m, today.AddDays(-12), "Deposit");
        db.Invoices.Add(inv2);

        // Paid in full in two instalments.
        var q3 = Accepted(db, "Q-0003", chong, today.AddDays(-40), today, [("Wedding photography package", 1, 2200m)]);
        var inv3 = q3.ConvertToInvoice("INV-0003", today.AddDays(-35), today.AddDays(-5));
        inv3.AddPayment(1100m, today.AddDays(-34), "50% deposit");
        inv3.AddPayment(1100m, today.AddDays(-10), "Balance");
        db.Invoices.Add(inv3);

        // Unpaid and not yet due.
        var q4 = Accepted(db, "Q-0004", dewi, today.AddDays(-6), today, [("Menu tasting session", 1, 350m)]);
        db.Invoices.Add(q4.ConvertToInvoice("INV-0004", today.AddDays(-2), today.AddDays(28)));

        // Accepted, ready to invoice.
        Accepted(db, "Q-0005", dewi, today.AddDays(-3), today.AddDays(11), [("Catering for 40 guests", 40, 28m)]);

        // Sent, waiting for an answer.
        var sent = NewQuote("Q-0006", ali, today.AddDays(-2), today.AddDays(12), [("Gas top-up", 2, 120m)]);
        sent.Send();
        db.Quotes.Add(sent);

        // Draft.
        db.Quotes.Add(NewQuote("Q-0007", bala, today, today.AddDays(14), [("Bathroom renovation", 1, 4500m)]));

        // Rejected.
        var rejected = NewQuote("Q-0008", chong, today.AddDays(-9), today.AddDays(5), [("Album printing", 1, 900m)]);
        rejected.Send();
        rejected.Reject();
        db.Quotes.Add(rejected);
    }

    static Quote NewQuote(string number, Customer customer, DateOnly issued, DateOnly validUntil, (string Description, decimal Qty, decimal Price)[] lines) => new()
    {
        Number = number,
        Customer = customer,
        IssuedOn = issued,
        ValidUntil = validUntil,
        Lines = lines.Select(l => new LineItem { Description = l.Description, Quantity = l.Qty, UnitPrice = l.Price }).ToList(),
    };

    /// <summary>A quotation taken through Draft, Sent and Accepted using the real domain rules.</summary>
    static Quote Accepted(QuotebookDbContext db, string number, Customer customer, DateOnly issued, DateOnly acceptedOn, (string Description, decimal Qty, decimal Price)[] lines)
    {
        // Valid until a month after acceptance, so accepting it today is allowed.
        var quote = NewQuote(number, customer, issued, acceptedOn.AddDays(30), lines);
        quote.Send();
        quote.Accept(acceptedOn);
        db.Quotes.Add(quote);
        return quote;
    }
}

/// <summary>Resets the demo business every few hours, so tidy sample data is always waiting.</summary>
public sealed class DemoResetService(DemoSeeder seeder, ILogger<DemoResetService> log) : BackgroundService
{
    static readonly TimeSpan Every = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await seeder.ResetAsync(stoppingToken); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Demo reset failed; will try again next time.");
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
