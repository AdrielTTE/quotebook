using Quotebook.Core;
using Quotebook.Core.Entities;

namespace Quotebook.Tests;

public class InvoiceTests
{
    static readonly DateOnly Issued = new(2026, 10, 12);
    static readonly DateOnly Due = new(2026, 11, 11);

    static Invoice Invoice100() => new()
    {
        OwnerId = "owner-1",
        Number = "INV-0001",
        IssuedOn = Issued,
        DueOn = Due,
        Lines = [new LineItem { Description = "Work", Quantity = 1, UnitPrice = 100m }],
    };

    [Fact]
    public void New_invoice_is_unpaid()
    {
        var inv = Invoice100();
        Assert.Equal(InvoiceStatus.Unpaid, inv.StatusOn(Issued));
        Assert.Equal(100m, inv.Balance);
    }

    [Fact]
    public void Partial_payment_reduces_balance_and_marks_partial()
    {
        var inv = Invoice100();
        inv.AddPayment(40m, Issued);
        Assert.Equal(60m, inv.Balance);
        Assert.Equal(InvoiceStatus.Partial, inv.StatusOn(Issued));
    }

    [Fact]
    public void Paying_the_balance_in_full_marks_paid_even_when_late()
    {
        var inv = Invoice100();
        inv.AddPayment(40m, Issued);
        inv.AddPayment(60m, Due.AddDays(5));
        Assert.Equal(InvoiceStatus.Paid, inv.StatusOn(Due.AddDays(10)));
    }

    [Fact]
    public void Unpaid_after_due_date_is_overdue()
        => Assert.Equal(InvoiceStatus.Overdue, Invoice100().StatusOn(Due.AddDays(1)));

    [Fact]
    public void Due_day_itself_is_not_overdue()
        => Assert.Equal(InvoiceStatus.Unpaid, Invoice100().StatusOn(Due));

    [Fact]
    public void Partially_paid_after_due_date_is_overdue()
    {
        var inv = Invoice100();
        inv.AddPayment(10m, Issued);
        Assert.Equal(InvoiceStatus.Overdue, inv.StatusOn(Due.AddDays(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Payment_must_be_positive(decimal amount)
        => Assert.Throws<DomainException>(() => Invoice100().AddPayment(amount, Issued));

    [Fact]
    public void Cannot_overpay()
    {
        var inv = Invoice100();
        inv.AddPayment(70m, Issued);
        Assert.Throws<DomainException>(() => inv.AddPayment(30.01m, Issued));
    }

    [Fact]
    public void Payments_are_kept_as_history()
    {
        var inv = Invoice100();
        inv.AddPayment(25m, Issued, "deposit");
        inv.AddPayment(25m, Issued.AddDays(7));
        Assert.Equal(2, inv.Payments.Count);
        Assert.Equal(50m, inv.Paid);
    }

    [Fact]
    public void Payment_amounts_are_rounded_to_cents()
    {
        var inv = Invoice100();
        inv.AddPayment(33.333m, Issued);
        Assert.Equal(33.33m, inv.Paid);
    }

    [Fact]
    public void Removing_a_payment_restores_the_balance_and_status()
    {
        var inv = Invoice100();
        var payment = inv.AddPayment(100m, Issued);
        Assert.Equal(InvoiceStatus.Paid, inv.StatusOn(Issued));

        inv.RemovePayment(payment.Id);

        Assert.Equal(100m, inv.Balance);
        Assert.Equal(InvoiceStatus.Unpaid, inv.StatusOn(Issued));
    }

    [Fact]
    public void Removing_a_payment_that_is_not_on_the_invoice_fails()
        => Assert.Throws<DomainException>(() => Invoice100().RemovePayment(999));
}
