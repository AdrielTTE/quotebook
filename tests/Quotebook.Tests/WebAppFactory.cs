using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quotebook.Data;

namespace Quotebook.Tests;

/// <summary>Runs the real web app in memory, with the Postgres database swapped for an in-memory one.</summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    readonly TestDatabase testDb = new();

    // Program.cs reads the connection string while it builds the host, before test configuration is applied,
    // so it has to come from the environment. Any value turns the database-backed setup on; it never connects.
    static WebAppFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Host=unused;Database=unused");
        Environment.SetEnvironmentVariable("Demo__Enabled", "true"); // seeds the demo business at startup
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<QuotebookDbContext>>();
            // EF registers its provider setup as an internal generic service; remove every one tied to our context.
            foreach (var d in services.Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                && d.ServiceType.GenericTypeArguments.Contains(typeof(QuotebookDbContext))).ToList())
                services.Remove(d);
            services.AddDbContext<QuotebookDbContext>(o => testDb.Configure(o));
        });
    }

    sealed class FixedTenant(string id) : Quotebook.Core.ITenantProvider
    {
        public string? TenantId { get; } = id;
    }

    /// <summary>Opens the app's in-memory database acting as one business, for seeding data the pages can't create.</summary>
    public async Task WithDbAsync(string tenantId, Func<QuotebookDbContext, Task> work)
    {
        using var db = new QuotebookDbContext(
            testDb.Options,
            new FixedTenant(tenantId));
        await work(db);
    }

    /// <summary>The business id of a registered owner (an owner's business id is their own user id).</summary>
    public async Task<string> TenantOfAsync(string email)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.TenantId;
    }

    /// <summary>Creates a Staff user inside an existing business and returns a client signed in as them.</summary>
    public async Task<HttpClient> StaffClientAsync(string tenantId)
    {
        var email = $"staff-{Guid.NewGuid():N}@example.com";
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var staff = new ApplicationUser { UserName = email, Email = email, DisplayName = "Staff", TenantId = tenantId };
            Assert.True((await users.CreateAsync(staff, "correct-horse-battery")).Succeeded);
            await users.AddToRoleAsync(staff, Quotebook.Core.Roles.Staff);
        }

        var client = NewClient();
        var response = await PostFormAsync(client, "/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = "correct-horse-battery" });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) testDb.Dispose();
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"), // the auth cookie is Secure-only
    });

    /// <summary>The text a visitor sees: tags removed, entities decoded, whitespace collapsed.</summary>
    public static string TextOf(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(System.Net.WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    static readonly Regex Token =new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>Loads a page and returns its anti-forgery token, as a browser form would send it back.</summary>
    public static async Task<string> TokenFromAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        return Token.Match(html).Groups[1].Value;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenFromAsync(client, path);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = "correct-horse-battery", string name = "Test Owner")
        => PostFormAsync(client, "/Account/Register", new()
        {
            ["Input.BusinessName"] = name + " Co",
            ["Input.DisplayName"] = name,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });
}
