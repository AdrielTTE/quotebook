namespace Quotebook.Core.Entities;

/// <summary>One priced row on a quote or invoice. Copied (not linked) when a quote becomes an invoice.</summary>
public class LineItem
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal Total => Money.Round(Quantity * UnitPrice);

    public LineItem Copy() => new() { Description = Description, Quantity = Quantity, UnitPrice = UnitPrice };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Description)) throw new DomainException("A line needs a description.");
        if (Quantity <= 0) throw new DomainException("Quantity must be greater than zero.");
        if (UnitPrice < 0) throw new DomainException("Unit price cannot be negative.");
    }
}
