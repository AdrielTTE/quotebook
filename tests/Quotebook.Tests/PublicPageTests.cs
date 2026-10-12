using System.Net;

namespace Quotebook.Tests;

/// <summary>What a stranger, a link preview or a search engine gets from the public pages.</summary>
public class PublicPageTests : IClassFixture<WebAppFactory>
{
    readonly WebAppFactory factory;
    public PublicPageTests(WebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task Home_page_has_a_description_and_a_share_image()
    {
        var html = await factory.NewClient().GetStringAsync("/");
        Assert.Contains("<meta name=\"description\" content=\"Write a quotation", html);
        Assert.Contains("property=\"og:image\" content=\"https://localhost/img/og.png\"", html);
    }

    [Fact]
    public async Task Share_image_is_served_without_signing_in()
    {
        var response = await factory.NewClient().GetAsync("/img/og.png");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_missing_page_shows_a_friendly_404_to_a_signed_in_user()
    {
        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await WebAppFactory.RegisterAsync(client, $"owner-{Guid.NewGuid():N}@example.com")).StatusCode);

        var response = await client.GetAsync("/no-such-page");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page not found.", html);
        Assert.DoesNotContain("Development Mode", html);
    }
}
