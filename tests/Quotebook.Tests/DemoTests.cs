using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quotebook.Core.Entities;
using Quotebook.Web.Infrastructure;

namespace Quotebook.Tests;

/// <summary>The one-click demo business and the switch that closes open sign-up.</summary>
public class DemoTests : IClassFixture<WebAppFactory>
{
    readonly WebAppFactory factory;
    public DemoTests(WebAppFactory factory) => this.factory = factory;

    async Task<string> DemoTenantAsync() => await factory.TenantOfAsync(DemoSeeder.Email);

    [Fact]
    public async Task The_demo_business_is_seeded_at_startup_with_every_status()
    {
        var tenant = await DemoTenantAsync();

        await factory.WithDbAsync(tenant, async db =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
            var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Payments).ToListAsync();
            var statuses = invoices.Select(i => i.StatusOn(today)).ToHashSet();
            Assert.Equal(new[] { InvoiceStatus.Unpaid, InvoiceStatus.Partial, InvoiceStatus.Paid, InvoiceStatus.Overdue }.ToHashSet(), statuses);

            var quoteStatuses = (await db.Quotes.ToListAsync()).Select(q => q.Status).ToHashSet();
            Assert.Equal(Enum.GetValues<QuoteStatus>().ToHashSet(), quoteStatuses);
        });
    }

    [Fact]
    public async Task Try_the_demo_signs_in_without_a_password_and_shows_the_sample_books()
    {
        var client = factory.NewClient();

        var response = await QuotePagesTests.PostAsync(client, "/", "/Account/Demo", new());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var html = await client.GetStringAsync("/Dashboard");
        Assert.Contains("Demo Owner", html);
        Assert.Contains("RM 2,040.00", html); // owed: 240 + 1,450 + 350
        Assert.Contains("INV-0001", html);    // the overdue one
    }

    [Fact]
    public async Task Resetting_restores_the_sample_data_and_is_repeatable()
    {
        var seeder = factory.Services.GetRequiredService<DemoSeeder>();
        var tenant = await DemoTenantAsync();

        // A visitor adds a customer, then the reset runs twice.
        await factory.WithDbAsync(tenant, async db =>
        {
            db.Customers.Add(new Customer { Name = "Visitor's mess" });
            await db.SaveChangesAsync();
        });
        await seeder.ResetAsync();
        await seeder.ResetAsync();

        await factory.WithDbAsync(tenant, async db =>
        {
            Assert.Equal(4, await db.Customers.CountAsync());
            Assert.Equal(8, await db.Quotes.CountAsync());
            Assert.Equal(4, await db.Invoices.CountAsync());
            Assert.DoesNotContain(await db.Customers.ToListAsync(), c => c.Name == "Visitor's mess");
        });
    }

    [Fact]
    public async Task Resetting_the_demo_never_touches_another_business()
    {
        var client = factory.NewClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await WebAppFactory.RegisterAsync(client, email);
        await QuotePagesTests.AddCustomerAsync(client, "My Real Customer");

        await factory.Services.GetRequiredService<DemoSeeder>().ResetAsync();

        Assert.Contains("My Real Customer", await client.GetStringAsync("/Customers"));
    }

    [Fact]
    public async Task The_demo_account_cannot_be_signed_into_with_a_guessed_password()
    {
        var client = factory.NewClient();
        var response = await WebAppFactory.PostFormAsync(client, "/Account/Login", new()
        {
            ["Input.Email"] = DemoSeeder.Email,
            ["Input.Password"] = "password1234",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // refused, form shown again
    }

    [Fact]
    public async Task Closing_sign_up_hides_the_form_and_refuses_new_accounts()
    {
        using var closed = factory.WithWebHostBuilder(b => b.UseSetting("Registration:Enabled", "false"));
        var client = closed.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        var page = await client.GetStringAsync("/Account/Register");
        Assert.Contains("Sign-up is closed", page);
        Assert.DoesNotContain("Confirm password", page);

        var post = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "Sneaky",
            ["Input.Email"] = "sneaky@example.com",
            ["Input.Password"] = "correct-horse-battery",
            ["Input.ConfirmPassword"] = "correct-horse-battery",
            ["__RequestVerificationToken"] = await WebAppFactory.TokenFromAsync(client, "/Account/Login"),
        }));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    [Fact]
    public async Task The_home_page_offers_the_demo_to_visitors()
    {
        var html = await factory.NewClient().GetStringAsync("/");
        Assert.Contains("Try the demo", html);
        Assert.Contains("wakes up", html); // the free-hosting cold-start note
    }
}
