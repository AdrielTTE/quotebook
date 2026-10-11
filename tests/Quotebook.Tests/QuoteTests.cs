using Quotebook.Core;
using Quotebook.Core.Entities;

namespace Quotebook.Tests;

public class QuoteTests
{
    static readonly DateOnly Today = new(2026, 10, 12);

    static Quote DraftQuote() => new()
    {
        OwnerId = "owner-1",
        Number = "Q-0001",
        CustomerId = 1,
        IssuedOn = Today,
        ValidUntil = Today.AddDays(14),
        Lines = [new LineItem { Description = "Aircon service", Quantity = 2, UnitPrice = 80m }],
    };

    static Quote SentQuote() { var q = DraftQuote(); q.Send(); return q; }

    [Fact]
    public void Total_sums_lines_and_rounds_to_two_places()
    {
        var q = DraftQuote();
        q.Lines = [
            new LineItem { Description = "A", Quantity = 3, UnitPrice = 3.333m },
            new LineItem { Description = "B", Quantity = 1, UnitPrice = 0.005m },
        ];
        Assert.Equal(10.01m, q.Total); // 9.999 -> 10.00, 0.005 -> 0.01
    }

    [Fact]
    public void New_quote_is_a_draft()
        => Assert.Equal(QuoteStatus.Draft, DraftQuote().Status);

    [Fact]
    public void Send_moves_draft_to_sent()
    {
        var q = DraftQuote();
        q.Send();
        Assert.Equal(QuoteStatus.Sent, q.Status);
    }

    [Fact]
    public void Send_requires_at_least_one_line()
    {
        var q = DraftQuote();
        q.Lines.Clear();
        Assert.Throws<DomainException>(q.Send);
    }

    [Theory]
    [InlineData("", 1, 10)]
    [InlineData("Item", 0, 10)]
    [InlineData("Item", 1, -1)]
    public void Send_rejects_invalid_lines(string description, decimal qty, decimal price)
    {
        var q = DraftQuote();
        q.Lines = [new LineItem { Description = description, Quantity = qty, UnitPrice = price }];
        Assert.Throws<DomainException>(q.Send);
    }

    [Fact]
    public void Cannot_send_twice()
    {
        var q = SentQuote();
        Assert.Throws<DomainException>(q.Send);
    }

    [Fact]
    public void Accept_works_on_a_sent_quote_before_it_expires()
    {
        var q = SentQuote();
        q.Accept(Today.AddDays(14)); // last valid day
        Assert.Equal(QuoteStatus.Accepted, q.Status);
    }

    [Fact]
    public void Accept_fails_after_expiry()
    {
        var q = SentQuote();
        Assert.Throws<DomainException>(() => q.Accept(Today.AddDays(15)));
    }

    [Fact]
    public void Accept_fails_on_a_draft()
        => Assert.Throws<DomainException>(() => DraftQuote().Accept(Today));

    [Fact]
    public void Reject_works_on_sent_only()
    {
        var sent = SentQuote();
        sent.Reject();
        Assert.Equal(QuoteStatus.Rejected, sent.Status);
        Assert.Throws<DomainException>(() => DraftQuote().Reject());
    }

    [Fact]
    public void Rejected_quote_cannot_be_accepted()
    {
        var q = SentQuote();
        q.Reject();
        Assert.Throws<DomainException>(() => q.Accept(Today));
    }

    [Fact]
    public void Only_an_accepted_quote_can_be_invoiced()
        => Assert.Throws<DomainException>(() => SentQuote().ConvertToInvoice("INV-1", Today, Today.AddDays(30)));

    [Fact]
    public void Conversion_copies_lines_customer_and_owner()
    {
        var q = SentQuote();
        q.Accept(Today);

        var inv = q.ConvertToInvoice("INV-0001", Today, Today.AddDays(30));

        Assert.Equal("owner-1", inv.OwnerId);
        Assert.Equal(q.CustomerId, inv.CustomerId);
        Assert.Equal(q.Total, inv.Total);
        Assert.Same(q, inv.Quote);
        Assert.NotSame(q.Lines[0], inv.Lines[0]); // copied, so editing one never changes the other
    }

    [Fact]
    public void A_quote_can_only_be_converted_once()
    {
        var q = SentQuote();
        q.Accept(Today);
        q.ConvertToInvoice("INV-0001", Today, Today.AddDays(30));
        Assert.Throws<DomainException>(() => q.ConvertToInvoice("INV-0002", Today, Today.AddDays(30)));
    }

    [Fact]
    public void Due_date_cannot_precede_issue_date()
    {
        var q = SentQuote();
        q.Accept(Today);
        Assert.Throws<DomainException>(() => q.ConvertToInvoice("INV-1", Today, Today.AddDays(-1)));
    }
}
