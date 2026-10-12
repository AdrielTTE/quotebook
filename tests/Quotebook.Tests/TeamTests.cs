using System.Net;
using System.Text.RegularExpressions;

namespace Quotebook.Tests;

/// <summary>Inviting staff by one-time link, and what staff can and cannot reach.</summary>
public class TeamTests : IClassFixture<WebAppFactory>
{
    readonly WebAppFactory factory;
    public TeamTests(WebAppFactory factory) => this.factory = factory;

    async Task<HttpClient> NewOwnerAsync()
    {
        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await WebAppFactory.RegisterAsync(client, $"owner-{Guid.NewGuid():N}@example.com")).StatusCode);
        return client;
    }

    /// <summary>Owner invites someone; returns the invite link's path and query, as shown once on the Team page.</summary>
    static async Task<string> InviteAsync(HttpClient owner, string name, string email)
    {
        var post = await QuotePagesTests.PostAsync(owner, "/Team", "/Team?handler=Invite", new() { ["Invite.DisplayName"] = name, ["Invite.Email"] = email });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var page = await owner.GetStringAsync("/Team");
        var link = Regex.Match(page, "id=\"invite-link\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(link);
        var uri = new Uri(WebUtility.HtmlDecode(link));
        return uri.PathAndQuery;
    }

    /// <summary>The id in the first Remove button's form action (query order is not fixed).</summary>
    static string RemoveIdOf(string page)
    {
        var action = Regex.Match(page, "action=\"([^\"]*handler=Remove[^\"]*)\"").Groups[1].Value;
        var id = System.Web.HttpUtility.ParseQueryString(new Uri("https://localhost" + WebUtility.HtmlDecode(action)).Query)["id"];
        Assert.False(string.IsNullOrEmpty(id));
        return id!;
    }

    static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string link, string password = "correct-horse-battery")
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri("https://localhost" + link).Query);
        return QuotePagesTests.PostAsync(client, link, link, new()
        {
            ["UserId"] = query["userId"]!,
            ["Token"] = query["token"]!,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });
    }

    [Fact]
    public async Task Invited_staff_set_a_password_and_work_inside_the_owners_business()
    {
        var owner = await NewOwnerAsync();
        await QuotePagesTests.AddCustomerAsync(owner, "Zulu Traders");
        var link = await InviteAsync(owner, "Siti", "siti@example.com");

        var staff = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(link)).StatusCode);
        var accepted = await AcceptAsync(staff, link);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);

        Assert.Contains("Zulu Traders", await staff.GetStringAsync("/Customers"));

        // Staff are not owners.
        var team = await staff.GetAsync("/Team");
        Assert.Equal(HttpStatusCode.Redirect, team.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", team.Headers.Location!.IsAbsoluteUri ? team.Headers.Location.AbsolutePath : team.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task An_invite_link_works_once()
    {
        var owner = await NewOwnerAsync();
        var link = await InviteAsync(owner, "Siti", $"siti-{Guid.NewGuid():N}@example.com");

        Assert.Equal(HttpStatusCode.Redirect, (await AcceptAsync(factory.NewClient(), link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.NewClient().GetAsync(link)).StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var owner = await NewOwnerAsync();
        var link = await InviteAsync(owner, "Siti", $"siti-{Guid.NewGuid():N}@example.com");

        var response = await AcceptAsync(factory.NewClient(), link[..^4] + "AAAA");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("expired or was already used", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inviting_an_email_that_already_has_an_account_says_so()
    {
        var owner = await NewOwnerAsync();
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await InviteAsync(owner, "First", email);

        var again = await QuotePagesTests.PostAsync(owner, "/Team", "/Team?handler=Invite", new() { ["Invite.DisplayName"] = "Second", ["Invite.Email"] = email });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("already has an account", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Owner_can_remove_staff_and_they_can_no_longer_sign_in()
    {
        var owner = await NewOwnerAsync();
        var email = $"gone-{Guid.NewGuid():N}@example.com";
        var link = await InviteAsync(owner, "Gone Soon", email);
        await AcceptAsync(factory.NewClient(), link);

        var page = await owner.GetStringAsync("/Team");
        var id = RemoveIdOf(page);
        Assert.NotEmpty(id);
        Assert.Equal(HttpStatusCode.Redirect, (await QuotePagesTests.PostAsync(owner, "/Team", $"/Team?handler=Remove&id={id}", new())).StatusCode);

        var login = await WebAppFactory.PostFormAsync(factory.NewClient(), "/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); // form shown again with an error
    }

    [Fact]
    public async Task One_business_cannot_remove_another_businesss_staff()
    {
        var ownerA = await NewOwnerAsync();
        var ownerB = await NewOwnerAsync();
        var link = await InviteAsync(ownerA, "Siti", $"siti-{Guid.NewGuid():N}@example.com");
        await AcceptAsync(factory.NewClient(), link);

        var id = RemoveIdOf(await ownerA.GetStringAsync("/Team"));
        var attempt = await QuotePagesTests.PostAsync(ownerB, "/Team", $"/Team?handler=Remove&id={id}", new());
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
    }

    [Fact]
    public async Task The_demo_account_cannot_invite_staff()
    {
        var demo = factory.NewClient();
        await QuotePagesTests.PostAsync(demo, "/Account/Login", "/Account/Demo", new());

        var email = $"nope-{Guid.NewGuid():N}@example.com";
        var post = await QuotePagesTests.PostAsync(demo, "/Team", "/Team?handler=Invite", new() { ["Invite.DisplayName"] = "Nope", ["Invite.Email"] = email });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("switched off in the demo", await demo.GetStringAsync("/Team"));

        var login = await WebAppFactory.PostFormAsync(factory.NewClient(), "/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.DoesNotContain("Nope", await demo.GetStringAsync("/Team"));
    }
}
