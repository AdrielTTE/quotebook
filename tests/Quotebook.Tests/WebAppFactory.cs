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
    readonly string dbName = Guid.NewGuid().ToString();

    // Program.cs reads the connection string while it builds the host, before test configuration is applied,
    // so it has to come from the environment. Any value turns the database-backed setup on; it never connects.
    static WebAppFactory() => Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Host=unused;Database=unused");

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
            services.AddDbContext<QuotebookDbContext>(o => o.UseInMemoryDatabase(dbName));
        });
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"), // the auth cookie is Secure-only
    });

    static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

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
            ["Input.DisplayName"] = name,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });
}
