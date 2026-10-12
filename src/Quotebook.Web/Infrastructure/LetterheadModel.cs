using Quotebook.Core.Entities;

namespace Quotebook.Web.Infrastructure;

/// <summary>What the letterhead shows. A business that never filled in its details still gets its name.</summary>
public sealed record LetterheadModel(string Name, string? Address, string? Phone, string? Email, string Type, string Number)
{
    public static LetterheadModel For(Business? business, string fallbackName, string type, string number)
        => new(string.IsNullOrWhiteSpace(business?.Name) ? fallbackName : business!.Name,
               business?.Address, business?.Phone, business?.Email, type, number);
}
