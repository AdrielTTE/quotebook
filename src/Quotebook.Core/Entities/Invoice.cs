namespace Quotebook.Core.Entities;

public enum InvoiceStatus { Unpaid, Partial, Paid, Overdue }

public class Invoice
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Number { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? QuoteId { get; set; }
    public Quote? Quote { get; set; }
    public DateOnly IssuedOn { get; set; }
    public DateOnly DueOn { get; set; }
    public List<LineItem> Lines { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public uint Version { get; set; }

    public decimal Total => Money.Round(Lines.Sum(l => l.Total));
    public decimal Paid => Money.Round(Payments.Sum(p => p.Amount));
    public decimal Balance => Money.Round(Total - Paid);

    /// <summary>Derived from the payments, never stored, so it cannot drift out of date.</summary>
    public InvoiceStatus StatusOn(DateOnly today)
    {
        if (Balance <= 0) return InvoiceStatus.Paid;
        if (today > DueOn) return InvoiceStatus.Overdue;
        return Paid > 0 ? InvoiceStatus.Partial : InvoiceStatus.Unpaid;
    }

    public Payment AddPayment(decimal amount, DateOnly paidOn, string? note = null)
    {
        amount = Money.Round(amount);
        if (amount <= 0) throw new DomainException("A payment must be greater than zero.");
        if (amount > Balance) throw new DomainException($"That is more than the {Balance:0.00} still owed.");

        var payment = new Payment { Amount = amount, PaidOn = paidOn, Note = note };
        Payments.Add(payment);
        return payment;
    }
}
