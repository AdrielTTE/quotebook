using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Quotebook.Core;
using Quotebook.Data;

namespace Quotebook.Tests;

public class AuthTests : IClassFixture<WebAppFactory>
{
    // Redirects come back as absolute URLs; compare on the path and query only.
    static string PathOf(HttpResponseMessage r) => r.Headers.Location!.IsAbsoluteUri ? r.Headers.Location.PathAndQuery : r.Headers.Location.OriginalString;

    readonly WebAppFactory factory;
    public AuthTests(WebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task Health_check_is_public()
    {
        var response = await factory.NewClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_page_sends_anonymous_visitors_to_sign_in()
    {
        var response = await factory.NewClient().GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", PathOf(response));
    }

    [Fact]
    public async Task Public_pages_do_not_need_sign_in()
    {
        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
    }

    [Fact]
    public async Task Registering_creates_an_owner_with_their_own_business_and_signs_them_in()
    {
        var client = factory.NewClient();
        var response = await WebAppFactory.RegisterAsync(client, "owner1@example.com");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Dashboard", PathOf(response));

        var dashboard = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("Test Owner", await dashboard.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync("owner1@example.com"))!;
        Assert.Equal(user.Id, user.TenantId); // an owner is their own business
        Assert.True(await users.IsInRoleAsync(user, Roles.Owner));
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var response = await WebAppFactory.RegisterAsync(factory.NewClient(), "weak@example.com", password: "short");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form shown again with errors
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        await WebAppFactory.RegisterAsync(factory.NewClient(), "dupe@example.com");
        var second = await WebAppFactory.RegisterAsync(factory.NewClient(), "dupe@example.com");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_does_not_sign_in()
    {
        await WebAppFactory.RegisterAsync(factory.NewClient(), "login1@example.com");

        var client = factory.NewClient();
        var response = await WebAppFactory.PostFormAsync(client, "/Account/Login", new()
        {
            ["Input.Email"] = "login1@example.com",
            ["Input.Password"] = "not-the-password",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Email or password is incorrect.", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Dashboard")).StatusCode);
    }

    [Fact]
    public async Task Correct_password_signs_in()
    {
        await WebAppFactory.RegisterAsync(factory.NewClient(), "login2@example.com");

        var client = factory.NewClient();
        var response = await WebAppFactory.PostFormAsync(client, "/Account/Login", new()
        {
            ["Input.Email"] = "login2@example.com",
            ["Input.Password"] = "correct-horse-battery",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Dashboard")).StatusCode);
    }

    [Fact]
    public async Task Owner_can_open_settings()
    {
        var client = factory.NewClient();
        await WebAppFactory.RegisterAsync(client, "owner2@example.com");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Settings")).StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_open_owner_only_pages()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var staff = new ApplicationUser { UserName = "staff@example.com", Email = "staff@example.com", DisplayName = "Staff", TenantId = "some-owner" };
            Assert.True((await users.CreateAsync(staff, "correct-horse-battery")).Succeeded);
            await users.AddToRoleAsync(staff, Roles.Staff);
        }

        var client = factory.NewClient();
        await WebAppFactory.PostFormAsync(client, "/Account/Login", new()
        {
            ["Input.Email"] = "staff@example.com",
            ["Input.Password"] = "correct-horse-battery",
        });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Dashboard")).StatusCode);
        var settings = await client.GetAsync("/Settings");
        Assert.Equal(HttpStatusCode.Redirect, settings.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", PathOf(settings));
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var client = factory.NewClient();
        await WebAppFactory.RegisterAsync(client, "logout1@example.com");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Dashboard")).StatusCode);

        // The sign-out button lives in the layout, which only exists on pages, so take its token from the dashboard.
        var token = await WebAppFactory.TokenFromAsync(client, "/Dashboard");
        await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Dashboard")).StatusCode);
    }
}
