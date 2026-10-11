using Microsoft.EntityFrameworkCore;
using Quotebook.Core.Entities;

namespace Quotebook.Data;

public class QuotebookDbContext(DbContextOptions<QuotebookDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Customer>(e =>
        {
            e.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.HasIndex(x => new { x.OwnerId, x.Name });
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
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(500);
            e.ToTable(t => t.HasCheckConstraint("ck_payment_amount_positive", "\"Amount\" > 0"));
        });
    }
}
