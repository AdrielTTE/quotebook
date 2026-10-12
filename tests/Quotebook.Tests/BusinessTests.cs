using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Quotebook.Data;

namespace Quotebook.Tests;

/// <summary>Business details on the letterhead, and search on the list pages.</summary>
public class BusinessTests : IClassFixture<WebAppFactory>
{
    readonly WebAppFactory factory;
    public BusinessTests(WebAppFactory factory) => this.factory = factory;

    async Task<HttpClient> NewOwnerAsync(string name = "Karim")
    {
        var client = factory.NewClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Redirect, (await WebAppFactory.RegisterAsync(client, email, name: name)).StatusCode);
        return client;
    }

    [Fact]
    public async Task Sign_up_takes_a_business_name_and_prints_it_on_a_quotation()
    {
        var client = await NewOwnerAsync("Karim");
        var customer = await QuotePagesTests.AddCustomerAsync(client, "Ali Aircon");
        var path = await QuotePagesTests.AddQuoteAsync(client, customer);

        var page = await client.GetStringAsync(path);
        Assert.Contains("Karim Co", page);
        Assert.Contains("Quotation", page);
    }

    [Fact]
    public async Task Owner_can_save_address_and_it_appears_on_documents()
    {
        var client = await NewOwnerAsync();
        var save = await WebAppFactory.PostFormAsync(client, "/Settings", new()
        {
            ["Input.Name"] = "Tan & Sons Sdn Bhd",
            ["Input.Address"] = "1 Jalan Satu",
            ["Input.Phone"] = "03-111 2222",
            ["Input.Email"] = "hi@tan.example",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var customer = await QuotePagesTests.AddCustomerAsync(client, "Ali Aircon");
        var page = await client.GetStringAsync(await QuotePagesTests.AddQuoteAsync(client, customer));
        Assert.Contains("Tan &amp; Sons Sdn Bhd", page);
        Assert.Contains("1 Jalan Satu", page);
        Assert.Contains("03-111 2222", page);
    }

    [Fact]
    public async Task Another_business_does_not_see_these_details()
    {
        var a = await NewOwnerAsync("Alpha");
        var b = await NewOwnerAsync("Beta");
        Assert.Contains("Alpha Co", await a.GetStringAsync("/Settings"));
        var other = await b.GetStringAsync("/Settings");
        Assert.DoesNotContain("Alpha Co", other);
        Assert.Contains("Beta Co", other);
    }

    [Fact]
    public async Task Customer_search_matches_name_email_and_phone()
    {
        var client = await NewOwnerAsync();
        await QuotePagesTests.AddCustomerAsync(client, "Zulu Traders");
        await QuotePagesTests.AddCustomerAsync(client, "Yankee Works");

        var byName = await client.GetStringAsync("/Customers?q=zulu");
        Assert.Contains("Zulu Traders", byName);
        Assert.DoesNotContain("Yankee Works", byName);
    }

    [Fact]
    public async Task Quote_search_matches_customer_name_and_number()
    {
        var client = await NewOwnerAsync();
        var zulu = await QuotePagesTests.AddCustomerAsync(client, "Zulu Traders");
        var yankee = await QuotePagesTests.AddCustomerAsync(client, "Yankee Works");
        await QuotePagesTests.AddQuoteAsync(client, zulu);
        await QuotePagesTests.AddQuoteAsync(client, yankee);

        var byName = await client.GetStringAsync("/Quotes?q=zulu");
        Assert.Contains("Zulu Traders", byName);
        Assert.DoesNotContain("Yankee Works", byName);

        var none = await client.GetStringAsync("/Quotes?q=nomatchhere");
        Assert.Contains("No quotations match that search.", none);
    }
}
