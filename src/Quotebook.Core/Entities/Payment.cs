namespace Quotebook.Core.Entities;

/// <summary>A payment is its own row (not a "paid" flag) so partial payments and history are kept.</summary>
public class Payment
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public string? Note { get; set; }
}
