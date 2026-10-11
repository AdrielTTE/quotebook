namespace Quotebook.Core.Entities;

public enum QuoteStatus { Draft, Sent, Accepted, Rejected }

public class Quote
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Number { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public QuoteStatus Status { get; private set; } = QuoteStatus.Draft;
    public DateOnly IssuedOn { get; set; }
    public DateOnly ValidUntil { get; set; }
    public List<LineItem> Lines { get; set; } = [];
    /// <summary>Optimistic concurrency token: a second editor's stale save is rejected, not silently merged.</summary>
    public uint Version { get; set; }

    public decimal Total => Money.Round(Lines.Sum(l => l.Total));

    /// <summary>Set once the quote has become an invoice, so it can only be converted once.</summary>
    public Invoice? Invoice { get; set; }

    public void Send()
    {
        if (Status != QuoteStatus.Draft) throw new DomainException("Only a draft quote can be sent.");
        if (Lines.Count == 0) throw new DomainException("Add at least one line before sending.");
        foreach (var line in Lines) line.Validate();
        Status = QuoteStatus.Sent;
    }

    public void Accept(DateOnly today)
    {
        if (Status != QuoteStatus.Sent) throw new DomainException("Only a sent quote can be accepted.");
        if (today > ValidUntil) throw new DomainException("This quote has expired.");
        Status = QuoteStatus.Accepted;
    }

    public void Reject()
    {
        if (Status != QuoteStatus.Sent) throw new DomainException("Only a sent quote can be rejected.");
        Status = QuoteStatus.Rejected;
    }

    /// <summary>Turns an accepted quote into an invoice with copied lines. One quote, one invoice.</summary>
    public Invoice ConvertToInvoice(string invoiceNumber, DateOnly issuedOn, DateOnly dueOn)
    {
        if (Status != QuoteStatus.Accepted) throw new DomainException("Only an accepted quote can be invoiced.");
        if (Invoice is not null) throw new DomainException("This quote has already been invoiced.");
        if (dueOn < issuedOn) throw new DomainException("The due date cannot be before the issue date.");

        Invoice = new Invoice
        {
            OwnerId = OwnerId,
            Number = invoiceNumber,
            CustomerId = CustomerId,
            Customer = Customer, // keeps the link when the customer is not saved yet (CustomerId would still be 0)
            Quote = this,
            IssuedOn = issuedOn,
            DueOn = dueOn,
            Lines = Lines.Select(l => l.Copy()).ToList(),
        };
        return Invoice;
    }
}
