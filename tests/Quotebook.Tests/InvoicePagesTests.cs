using System.Net;
using System.Text.RegularExpressions;
using Quotebook.Core.Entities;

namespace Quotebook.Tests;

/// <summary>Quotation to invoice, payments, statuses and the dashboard, through the real pages.</summary>
public class InvoicePagesTests : IClassFixture<WebAppFactory>
{
    readonly WebAppFactory factory;
    public InvoicePagesTests(WebAppFactory factory) => this.factory = factory;

    static string MalaysiaToday() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).ToString("yyyy-MM-dd");
    static DateOnly MalaysiaTodayDate() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    // ---- helpers ----

    async Task<(HttpClient Client, string Email)> NewOwnerAsync()
    {
        var client = factory.NewClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Redirect, (await WebAppFactory.RegisterAsync(client, email)).StatusCode);
        return (client, email);
    }

    static string PathOf(HttpResponseMessage r)
        => r.Headers.Location!.IsAbsoluteUri ? r.Headers.Location.AbsolutePath : r.Headers.Location.OriginalString;

    /// <summary>Makes a quotation (2 x RM80 by default = RM160), sends it and records the customer's acceptance.</summary>
    static async Task<string> AcceptedQuoteAsync(HttpClient client, string customerName = "Ali Aircon")
    {
        var customer = await QuotePagesTests.AddCustomerAsync(client, customerName);
        var path = await QuotePagesTests.AddQuoteAsync(client, customer);
        await QuotePagesTests.ActAsync(client, path, "Send");
        await QuotePagesTests.ActAsync(client, path, "Accept");
        return path;
    }

    static Task<HttpResponseMessage> InvoiceFromAsync(HttpClient client, string quotePath, string? dueOn = null)
        => QuotePagesTests.PostAsync(client, quotePath, $"{quotePath}?handler=Invoice", new() { ["dueOn"] = dueOn ?? DateOnly.Parse(MalaysiaToday()).AddDays(30).ToString("yyyy-MM-dd") });

    /// <summary>Quote already invoiced: returns the invoice's details path.</summary>
    static async Task<string> InvoiceAsync(HttpClient client)
    {
        var quote = await AcceptedQuoteAsync(client);
        var response = await InvoiceFromAsync(client, quote);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return PathOf(response);
    }

    static Task<HttpResponseMessage> PayAsync(HttpClient client, string invoicePath, string amount, string? note = null)
        => QuotePagesTests.PostAsync(client, invoicePath, $"{invoicePath}?handler=Payment", new()
        {
            ["Payment.Amount"] = amount,
            ["Payment.PaidOn"] = MalaysiaToday(),
            ["Payment.Note"] = note ?? "",
        });

    // ---- quotation to invoice ----

    [Fact]
    public async Task An_accepted_quotation_becomes_an_invoice_with_the_same_lines_and_total()
    {
        var (client, _) = await NewOwnerAsync();
        var quote = await AcceptedQuoteAsync(client);

        var response = await InvoiceFromAsync(client, quote);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await client.GetStringAsync(PathOf(response));
        Assert.Contains("INV-0001", html);
        Assert.Contains("Aircon service", html);
        Assert.Contains("160.00", html); // 2 x 80
        Assert.Contains("Unpaid", html);
        Assert.Contains("Q-0001", html); // links back to the quotation
        Assert.Contains("Invoiced as", await client.GetStringAsync(quote));
    }

    [Fact]
    public async Task A_quotation_that_is_not_accepted_cannot_be_invoiced()
    {
        var (client, _) = await NewOwnerAsync();
        var customer = await QuotePagesTests.AddCustomerAsync(client, "Ali");
        var draft = await QuotePagesTests.AddQuoteAsync(client, customer);

        await InvoiceFromAsync(client, draft);

        Assert.Contains("Only an accepted quote can be invoiced.", await client.GetStringAsync(draft));
        Assert.DoesNotContain("INV-0001", await client.GetStringAsync("/Invoices"));
    }

    [Fact]
    public async Task A_quotation_can_only_be_invoiced_once()
    {
        var (client, _) = await NewOwnerAsync();
        var quote = await AcceptedQuoteAsync(client);
        await InvoiceFromAsync(client, quote);

        await InvoiceFromAsync(client, quote); // second click

        var list = await client.GetStringAsync("/Invoices");
        Assert.Contains("INV-0001", list);
        Assert.DoesNotContain("INV-0002", list);
    }

    // ---- payments ----

    [Fact]
    public async Task Partial_then_full_payment_moves_the_invoice_to_paid()
    {
        var (client, _) = await NewOwnerAsync();
        var invoice = await InvoiceAsync(client);

        await PayAsync(client, invoice, "60.00", "deposit");
        var partial = await client.GetStringAsync(invoice);
        Assert.Contains("Partly paid", partial);
        Assert.Contains("deposit", partial);
        Assert.Contains("100.00", partial); // balance left: 160 - 60

        await PayAsync(client, invoice, "100.00");
        var paid = await client.GetStringAsync(invoice);
        Assert.Contains("Paid", paid);
        Assert.Contains("Fully paid", paid);
    }

    [Fact]
    public async Task Paying_more_than_is_owed_is_refused()
    {
        var (client, _) = await NewOwnerAsync();
        var invoice = await InvoiceAsync(client);

        await PayAsync(client, invoice, "160.01");

        var html = await client.GetStringAsync(invoice);
        Assert.Contains("more than the", html);
        Assert.Contains("Unpaid", html); // nothing was recorded
    }

    [Fact]
    public async Task A_payment_must_be_above_zero()
    {
        var (client, _) = await NewOwnerAsync();
        var invoice = await InvoiceAsync(client);

        var response = await PayAsync(client, invoice, "0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form shown again with the error
        Assert.Contains("Enter an amount above zero.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Businesses_cannot_see_or_pay_each_others_invoices()
    {
        var (a, _) = await NewOwnerAsync();
        var invoice = await InvoiceAsync(a);

        var (b, _) = await NewOwnerAsync();
        await QuotePagesTests.AddCustomerAsync(b, "Customer of B"); // gives B a page with a token to post from
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(invoice)).StatusCode);

        var pay = await QuotePagesTests.PostAsync(b, "/Customers", $"{invoice}?handler=Payment", new()
        {
            ["Payment.Amount"] = "10",
            ["Payment.PaidOn"] = MalaysiaToday(),
        });
        Assert.Equal(HttpStatusCode.NotFound, pay.StatusCode);
        Assert.Contains("Unpaid", await a.GetStringAsync(invoice)); // untouched
    }

    // ---- roles inside one business ----

    [Fact]
    public async Task Staff_can_record_payments_but_only_the_owner_can_remove_them()
    {
        var (owner, email) = await NewOwnerAsync();
        var invoice = await InvoiceAsync(owner);
        var staff = await factory.StaffClientAsync(await factory.TenantOfAsync(email));

        // Staff work inside the owner's business, so they see its invoice and can take a payment.
        await PayAsync(staff, invoice, "50.00", "cash at counter");
        var page = await owner.GetStringAsync(invoice);
        Assert.Contains("cash at counter", page);
        var paymentId = Regex.Match(page, @"paymentId=(\d+)").Groups[1].Value;

        // ...but they have no Remove button, and a forged request is refused.
        Assert.DoesNotContain("handler=DeletePayment", await staff.GetStringAsync(invoice));
        var forged = await QuotePagesTests.PostAsync(staff, invoice, $"{invoice}?handler=DeletePayment&paymentId={paymentId}", new());
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", PathAndQuery(forged));
        Assert.Contains("cash at counter", await owner.GetStringAsync(invoice)); // still there

        // The owner can.
        await QuotePagesTests.PostAsync(owner, invoice, $"{invoice}?handler=DeletePayment&paymentId={paymentId}", new());
        Assert.DoesNotContain("cash at counter", await owner.GetStringAsync(invoice));
    }

    static string PathAndQuery(HttpResponseMessage r)
        => r.Headers.Location!.IsAbsoluteUri ? r.Headers.Location.PathAndQuery : r.Headers.Location.OriginalString;

    // ---- overdue, filters and the dashboard (dates in the past can't be made through the pages) ----

    async Task<(HttpClient Client, string Tenant)> OwnerWithSeededBooksAsync()
    {
        var (client, email) = await NewOwnerAsync();
        var tenant = await factory.TenantOfAsync(email);
        var today = MalaysiaTodayDate();

        await factory.WithDbAsync(tenant, async db =>
        {
            var customer = new Customer { Name = "Seeded Customer" };
            db.Customers.Add(customer);

            // RM100, due last month, nothing paid: overdue.
            db.Invoices.Add(new Invoice
            {
                Number = "INV-0001", Customer = customer, IssuedOn = today.AddDays(-60), DueOn = today.AddDays(-30),
                Lines = [new LineItem { Description = "Old job", Quantity = 1, UnitPrice = 100m }],
            });

            // RM200, due next month, RM50 paid today: partly paid, not overdue.
            var recent = new Invoice
            {
                Number = "INV-0002", Customer = customer, IssuedOn = today, DueOn = today.AddDays(30),
                Lines = [new LineItem { Description = "New job", Quantity = 1, UnitPrice = 200m }],
            };
            recent.AddPayment(50m, today);
            db.Invoices.Add(recent);

            // RM75 sent and waiting for an answer.
            var quote = new Quote
            {
                Number = "Q-0001", Customer = customer, IssuedOn = today, ValidUntil = today.AddDays(14),
                Lines = [new LineItem { Description = "Quoted job", Quantity = 1, UnitPrice = 75m }],
            };
            quote.Send();
            db.Quotes.Add(quote);

            await db.SaveChangesAsync();
        });

        return (client, tenant);
    }

    [Fact]
    public async Task The_dashboard_adds_up_what_is_owed_overdue_waiting_and_collected()
    {
        var (client, _) = await OwnerWithSeededBooksAsync();

        var html = WebAppFactory.TextOf(await client.GetStringAsync("/Dashboard"));

        Assert.Contains("RM 250.00", html); // owed: 100 + (200 - 50)
        Assert.Contains("RM 100.00", html); // overdue
        Assert.Contains("RM 75.00", html);  // sent quotation waiting
        Assert.Contains("RM 50.00", html);  // collected this month
        Assert.Contains("INV-0001", html);  // listed under overdue
    }

    [Fact]
    public async Task The_invoice_list_can_be_filtered_by_status()
    {
        var (client, _) = await OwnerWithSeededBooksAsync();

        var overdue = await client.GetStringAsync("/Invoices?status=Overdue");
        Assert.Contains("INV-0001", overdue);
        Assert.DoesNotContain("INV-0002", overdue);

        var partial = await client.GetStringAsync("/Invoices?status=Partial");
        Assert.Contains("INV-0002", partial);
        Assert.DoesNotContain("INV-0001", partial);

        Assert.DoesNotContain("INV-000", await client.GetStringAsync("/Invoices?status=Paid"));
    }

    [Fact]
    public async Task One_business_dashboard_never_includes_another_businesss_money()
    {
        await OwnerWithSeededBooksAsync();

        var (other, _) = await NewOwnerAsync();
        var html = WebAppFactory.TextOf(await other.GetStringAsync("/Dashboard"));

        Assert.DoesNotContain("RM 250.00", html);
        Assert.DoesNotContain("INV-0001", html);
    }
}
