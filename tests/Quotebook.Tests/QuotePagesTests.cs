using System.Net;
using System.Text.RegularExpressions;

namespace Quotebook.Tests;

/// <summary>Customers and quotations through the real pages, signed in as real (in-memory) users.</summary>
public class QuotePagesTests : IClassFixture<WebAppFactory>
{
    readonly WebAppFactory factory;
    public QuotePagesTests(WebAppFactory factory) => this.factory = factory;

    // ---- helpers ----

    /// <summary>A browser-like client signed in as a brand-new business owner.</summary>
    async Task<HttpClient> NewOwnerAsync()
    {
        var client = factory.NewClient();
        var response = await WebAppFactory.RegisterAsync(client, $"owner-{Guid.NewGuid():N}@example.com");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string url, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await WebAppFactory.TokenFromAsync(client, tokenPage);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    internal static async Task<int> AddCustomerAsync(HttpClient client, string name)
    {
        var response = await WebAppFactory.PostFormAsync(client, "/Customers/Edit", new() { ["Input.Name"] = name });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var html = await client.GetStringAsync("/Customers");
        var row = Regex.Match(html, $@"<td>{Regex.Escape(name)}</td>[\s\S]*?/Customers/Edit/(\d+)");
        return int.Parse(row.Groups[1].Value);
    }

    internal static Dictionary<string, string> QuoteForm(int customerId, params (string Description, decimal Qty, decimal Price)[] lines)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.CustomerId"] = customerId.ToString(),
            ["Input.IssuedOn"] = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            ["Input.ValidUntil"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd"),
            ["Input.Version"] = "0",
        };
        for (var i = 0; i < lines.Length; i++)
        {
            fields[$"Input.Lines[{i}].Description"] = lines[i].Description;
            fields[$"Input.Lines[{i}].Quantity"] = lines[i].Qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields[$"Input.Lines[{i}].UnitPrice"] = lines[i].Price.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return fields;
    }

    /// <summary>Creates a quotation and returns its details-page path (e.g. /Quotes/Details/7).</summary>
    internal static async Task<string> AddQuoteAsync(HttpClient client, int customerId, params (string, decimal, decimal)[] lines)
    {
        lines = lines.Length == 0 ? [("Aircon service", 2m, 80m)] : lines;
        var response = await WebAppFactory.PostFormAsync(client, "/Quotes/Edit", QuoteForm(customerId, lines));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
    }

    internal static Task<HttpResponseMessage> ActAsync(HttpClient client, string detailsPath, string handler)
        => PostAsync(client, detailsPath, $"{detailsPath}?handler={handler}", new());

    // ---- customers ----

    [Fact]
    public async Task A_customer_can_be_added_and_found_by_search()
    {
        var client = await NewOwnerAsync();
        await AddCustomerAsync(client, "Ali Aircon Sdn Bhd");
        await AddCustomerAsync(client, "Bala Renovation");

        var all = await client.GetStringAsync("/Customers");
        Assert.Contains("Ali Aircon Sdn Bhd", all);
        Assert.Contains("Bala Renovation", all);

        var found = await client.GetStringAsync("/Customers?q=renov");
        Assert.Contains("Bala Renovation", found);
        Assert.DoesNotContain("Ali Aircon", found);
    }

    [Fact]
    public async Task A_customer_needs_a_name()
    {
        var client = await NewOwnerAsync();
        var response = await WebAppFactory.PostFormAsync(client, "/Customers/Edit", new() { ["Input.Name"] = "" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form shown again
    }

    [Fact]
    public async Task Businesses_cannot_see_or_open_each_others_customers()
    {
        var a = await NewOwnerAsync();
        var idA = await AddCustomerAsync(a, "Secret Customer of A");

        var b = await NewOwnerAsync();
        Assert.DoesNotContain("Secret Customer of A", await b.GetStringAsync("/Customers"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/Customers/Edit/{idA}")).StatusCode);
    }

    [Fact]
    public async Task A_customer_without_documents_can_be_deleted()
    {
        var client = await NewOwnerAsync();
        var id = await AddCustomerAsync(client, "Temporary Customer");

        var response = await PostAsync(client, "/Customers", $"/Customers?handler=Delete&id={id}", new());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // The success banner repeats the name, so look for the table cell.
        Assert.DoesNotContain("<td>Temporary Customer</td>", await client.GetStringAsync("/Customers"));
    }

    [Fact]
    public async Task A_customer_with_a_quotation_cannot_be_deleted()
    {
        var client = await NewOwnerAsync();
        var id = await AddCustomerAsync(client, "Busy Customer");
        await AddQuoteAsync(client, id);

        await PostAsync(client, "/Customers", $"/Customers?handler=Delete&id={id}", new());

        var page = await client.GetStringAsync("/Customers");
        Assert.Contains("Busy Customer", page);
    }

    // ---- quotations ----

    [Fact]
    public async Task A_quotation_is_created_numbered_and_totalled()
    {
        var client = await NewOwnerAsync();
        var customer = await AddCustomerAsync(client, "Ali");

        var path = await AddQuoteAsync(client, customer, ("Aircon service", 2m, 80m), ("Gas top-up", 1m, 45.5m));

        var html = await client.GetStringAsync(path);
        Assert.Contains("Q-0001", html);
        Assert.Contains("205.50", html); // 2 x 80 + 45.50
        Assert.Contains("Draft", html);
    }

    [Fact]
    public async Task Quotation_numbers_count_up_per_business()
    {
        var a = await NewOwnerAsync();
        var customerA = await AddCustomerAsync(a, "Customer A");
        await AddQuoteAsync(a, customerA);
        var second = await AddQuoteAsync(a, customerA);
        Assert.Contains("Q-0002", await a.GetStringAsync(second));

        var b = await NewOwnerAsync();
        var customerB = await AddCustomerAsync(b, "Customer B");
        var first = await AddQuoteAsync(b, customerB);
        Assert.Contains("Q-0001", await b.GetStringAsync(first)); // B starts its own series
    }

    [Fact]
    public async Task A_quotation_needs_at_least_one_line()
    {
        var client = await NewOwnerAsync();
        var customer = await AddCustomerAsync(client, "Ali");

        var form = QuoteForm(customer);
        form["Input.Lines[0].Description"] = "   ";
        var response = await WebAppFactory.PostFormAsync(client, "/Quotes/Edit", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Add at least one line", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_quotation_cannot_use_another_businesss_customer()
    {
        var a = await NewOwnerAsync();
        var customerOfA = await AddCustomerAsync(a, "Customer of A");

        var b = await NewOwnerAsync();
        var response = await WebAppFactory.PostFormAsync(b, "/Quotes/Edit", QuoteForm(customerOfA, ("Sneaky", 1m, 1m)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // refused, form shown again
        Assert.DoesNotContain("Q-0001", await b.GetStringAsync("/Quotes"));
    }

    [Fact]
    public async Task Businesses_cannot_open_or_change_each_others_quotations()
    {
        var a = await NewOwnerAsync();
        var customerA = await AddCustomerAsync(a, "Customer of A");
        var path = await AddQuoteAsync(a, customerA);

        var b = await NewOwnerAsync();
        await AddCustomerAsync(b, "Customer of B"); // gives B a page with a token to post from
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(path)).StatusCode);

        var send = await PostAsync(b, "/Quotes", $"{path}?handler=Send", new());
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        Assert.Contains("Draft", await a.GetStringAsync(path)); // untouched
    }

    [Fact]
    public async Task A_quotation_goes_draft_to_sent_to_accepted()
    {
        var client = await NewOwnerAsync();
        var customer = await AddCustomerAsync(client, "Ali");
        var path = await AddQuoteAsync(client, customer);

        await ActAsync(client, path, "Send");
        Assert.Contains("Sent", await client.GetStringAsync(path));

        await ActAsync(client, path, "Accept");
        var html = await client.GetStringAsync(path);
        Assert.Contains("Accepted", html);
        Assert.DoesNotContain("Customer accepted", html); // the action buttons are gone
    }

    [Fact]
    public async Task A_rejected_quotation_cannot_be_accepted_later()
    {
        var client = await NewOwnerAsync();
        var customer = await AddCustomerAsync(client, "Ali");
        var path = await AddQuoteAsync(client, customer);

        await ActAsync(client, path, "Send");
        await ActAsync(client, path, "Reject");
        await ActAsync(client, path, "Accept"); // not allowed from Rejected

        var html = await client.GetStringAsync(path);
        Assert.Contains("Rejected", html);
        Assert.Contains("Only a sent quote can be accepted.", html);
    }

    [Fact]
    public async Task A_sent_quotation_can_no_longer_be_edited()
    {
        var client = await NewOwnerAsync();
        var customer = await AddCustomerAsync(client, "Ali");
        var path = await AddQuoteAsync(client, customer);
        await ActAsync(client, path, "Send");

        var id = path.Split('/').Last();
        var edit = await client.GetAsync($"/Quotes/Edit/{id}");
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode); // sent back to the read-only page
    }

    [Fact]
    public async Task Only_drafts_can_be_deleted()
    {
        var client = await NewOwnerAsync();
        var customer = await AddCustomerAsync(client, "Ali");

        var draft = await AddQuoteAsync(client, customer);
        var deleted = await ActAsync(client, draft, "Delete");
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(draft)).StatusCode);

        var sent = await AddQuoteAsync(client, customer);
        await ActAsync(client, sent, "Send");
        await ActAsync(client, sent, "Delete");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(sent)).StatusCode); // still there
    }
}
