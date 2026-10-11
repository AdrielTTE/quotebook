using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quotebook.Data;

/// <summary>Lets `dotnet ef` build migrations without a running database or the web project.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<QuotebookDbContext>
{
    public QuotebookDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("QUOTEBOOK_DB")
            ?? "Host=localhost;Database=quotebook;Username=postgres;Password=placeholder"; // never used to connect when only adding migrations
        var options = new DbContextOptionsBuilder<QuotebookDbContext>().UseNpgsql(connection).Options;
        return new QuotebookDbContext(options);
    }
}
