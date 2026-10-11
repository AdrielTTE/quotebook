using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Core.Entities;
using Quotebook.Data;

namespace Quotebook.Tests;

/// <summary>One business must never see or change another's data.</summary>
public class TenantIsolationTests
{
    sealed class FixedTenant(string? id) : ITenantProvider
    {
        public string? TenantId { get; } = id;
    }

    // One in-memory database shared by several contexts, each acting for a different tenant.
    sealed class Db
    {
        readonly string name = Guid.NewGuid().ToString();
        public QuotebookDbContext As(string? tenant) => new(
            new DbContextOptionsBuilder<QuotebookDbContext>().UseInMemoryDatabase(name).Options,
            new FixedTenant(tenant));
    }

    [Fact]
    public async Task Each_business_sees_only_its_own_customers()
    {
        var db = new Db();
        using (var a = db.As("A")) { a.Customers.Add(new Customer { Name = "Ali" }); await a.SaveChangesAsync(); }
        using (var b = db.As("B")) { b.Customers.Add(new Customer { Name = "Bala" }); await b.SaveChangesAsync(); }

        using var asA = db.As("A");
        var names = await asA.Customers.Select(c => c.Name).ToListAsync();
        Assert.Equal(["Ali"], names);
    }

    [Fact]
    public async Task Nobody_signed_in_sees_nothing()
    {
        var db = new Db();
        using (var a = db.As("A")) { a.Customers.Add(new Customer { Name = "Ali" }); await a.SaveChangesAsync(); }

        using var anon = db.As(null);
        Assert.Empty(await anon.Customers.ToListAsync());
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_current_tenant()
    {
        var db = new Db();
        using (var a = db.As("A")) { a.Customers.Add(new Customer { Name = "Ali" }); await a.SaveChangesAsync(); }

        using var verify = db.As("A");
        Assert.Equal("A", (await verify.Customers.SingleAsync()).OwnerId);
    }

    [Fact]
    public async Task Saving_a_row_for_another_business_is_refused()
    {
        using var a = new Db().As("A");
        a.Customers.Add(new Customer { Name = "Sneaky", OwnerId = "B" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public async Task Saving_tenant_data_with_no_tenant_is_refused()
    {
        using var anon = new Db().As(null);
        anon.Customers.Add(new Customer { Name = "Nobody" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => anon.SaveChangesAsync());
    }

    [Fact]
    public async Task Quotes_and_invoices_are_isolated_too()
    {
        var db = new Db();
        using (var a = db.As("A"))
        {
            var customer = new Customer { Name = "Ali" };
            a.Customers.Add(customer);
            a.Quotes.Add(new Quote { Number = "Q-1", Customer = customer, IssuedOn = new(2026, 10, 12), ValidUntil = new(2026, 10, 26) });
            a.Invoices.Add(new Invoice { Number = "INV-1", Customer = customer, IssuedOn = new(2026, 10, 12), DueOn = new(2026, 11, 11) });
            await a.SaveChangesAsync();
        }

        using var b = db.As("B");
        Assert.Empty(await b.Quotes.ToListAsync());
        Assert.Empty(await b.Invoices.ToListAsync());
    }
}
