namespace Quotebook.Web.Infrastructure;

public static class DocumentNumbers
{
    /// <summary>
    /// Next number in a "Q-0001" style series, one past the highest already used by this business.
    /// Callers pass an already tenant-filtered query. A unique index on (OwnerId, Number) is the
    /// real guard if two requests ever race for the same number.
    /// </summary>
    public static async Task<string> NextAsync(IQueryable<string> existing, string prefix, CancellationToken ct = default)
    {
        var numbers = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(existing, ct);
        var max = numbers
            .Select(n => n.StartsWith(prefix + "-", StringComparison.Ordinal) && int.TryParse(n[(prefix.Length + 1)..], out var v) ? v : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}-{max + 1:0000}";
    }
}
