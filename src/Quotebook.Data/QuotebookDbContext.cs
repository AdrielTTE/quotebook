using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Core.Entities;

namespace Quotebook.Data;

public class QuotebookDbContext(DbContextOptions<QuotebookDbContext> options, ITenantProvider tenant)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Read per query, so each request filters on its own tenant. Null matches nothing.
    string? CurrentTenantId => tenant.TenantId;

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.TenantId).HasMaxLength(450).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.HasIndex(x => new { x.OwnerId, x.Name });
            e.HasQueryFilter(x => x.OwnerId == CurrentTenantId);
        });

        b.Entity<LineItem>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Ignore(x => x.Total);
        });

        b.Entity<Quote>(e =>
        {
            e.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Number).HasMaxLength(30).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion(); // Postgres xmin: stale edits throw DbUpdateConcurrencyException
            e.Ignore(x => x.Total);
            e.HasIndex(x => new { x.OwnerId, x.Number }).IsUnique(); // numbers are unique per business
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.OwnerId == CurrentTenantId);
        });

        b.Entity<Invoice>(e =>
        {
            e.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Number).HasMaxLength(30).IsRequired();
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.Total).Ignore(x => x.Paid).Ignore(x => x.Balance);
            e.HasIndex(x => new { x.OwnerId, x.Number }).IsUnique();
            e.HasIndex(x => x.DueOn);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Quote).WithOne(q => q.Invoice).HasForeignKey<Invoice>(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.QuoteId).IsUnique(); // one quote -> at most one invoice, enforced by the database too
            e.HasMany(x => x.Lines).WithOne().OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne().OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.OwnerId == CurrentTenantId);
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(500);
            e.ToTable(t => t.HasCheckConstraint("ck_payment_amount_positive", "\"Amount\" > 0"));
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Second line of defence behind the query filters: new rows are stamped with the
    /// current tenant, and a row for another tenant is refused outright.
    /// </summary>
    void EnforceTenant()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var ownerId = entry.Entity switch
            {
                Customer c => c.OwnerId,
                Quote q => q.OwnerId,
                Invoice i => i.OwnerId,
                _ => null,
            };
            if (entry.Entity is not (Customer or Quote or Invoice)) continue;

            if (CurrentTenantId is null)
                throw new InvalidOperationException("No tenant in scope: refusing to save tenant data.");

            if (entry.State == EntityState.Added && string.IsNullOrEmpty(ownerId))
            {
                entry.Property("OwnerId").CurrentValue = CurrentTenantId;
                continue;
            }

            if (ownerId != CurrentTenantId)
                throw new InvalidOperationException("Refusing to save a row that belongs to another business.");
        }
    }
}
