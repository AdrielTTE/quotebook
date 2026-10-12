using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Quotebook.Core;
using Quotebook.Data;
using Quotebook.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Every page needs a signed-in user unless it opts out here.
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AllowAnonymousToPage("/Index");
    o.Conventions.AllowAnonymousToPage("/Privacy");
    o.Conventions.AllowAnonymousToPage("/Error");
    o.Conventions.AllowAnonymousToFolder("/Account");
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("OwnerOnly", p => p.RequireRole(Roles.Owner));

// Behind a host like Render, TLS ends at the proxy and the app sees plain http. Trust its forwarded
// headers so redirects, the Secure sign-in cookie and client addresses are right.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<MalaysiaTimeProvider>();
builder.Services.AddSingleton<AppFeatures>();

// Behind the host's HTTPS proxy the antiforgery cookie must be Secure too (the sign-in cookie already is).
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
builder.Services.AddScoped<ITenantProvider, ClaimsTenantProvider>();

// Connection string comes from user-secrets in development and an environment
// variable in production (ConnectionStrings__Default). It is never committed.
var connection = builder.Configuration.GetConnectionString("Default");
var hasDatabase = !string.IsNullOrWhiteSpace(connection);
if (hasDatabase)
{
    // Retry briefly on transient failures: Neon's free database sleeps when idle and the first query can fail while it wakes.
    builder.Services.AddDbContext<QuotebookDbContext>(o => o.UseNpgsql(connection, n => n.EnableRetryOnFailure(maxRetryCount: 4, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null)));
    builder.Services.AddHealthChecks().AddDbContextCheck<QuotebookDbContext>("database");

    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.Password.RequiredLength = 10;
            // Length matters more than character rules (NIST 800-63B), so require 10+ and nothing else.
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireDigit = false;
            o.Password.RequireUppercase = false;
            o.Password.RequireLowercase = false;
            o.User.RequireUniqueEmail = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        })
        .AddEntityFrameworkStores<QuotebookDbContext>()
        .AddDefaultTokenProviders() // one-time tokens for staff invite links
        .AddClaimsPrincipalFactory<TenantClaimsFactory>();

    if (builder.Configuration.GetValue<bool>("Demo:Enabled"))
    {
        builder.Services.AddSingleton<DemoSeeder>();
        builder.Services.AddHostedService<DemoResetService>();
    }

    builder.Services.ConfigureApplicationCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
    });
}
else
{
    builder.Services.AddHealthChecks();
    // No database: keep a scheme so the fallback policy can still challenge. Pages that
    // need data are unreachable until a connection string is configured.
    builder.Services.AddAuthentication().AddCookie();
}

var app = builder.Build();

if (hasDatabase)
{
    // Opt-in (MigrateOnStart=true) so a deploy brings the database up to date. Fine for a one-instance demo;
    // a larger system would run migrations as a separate release step.
    if (app.Configuration.GetValue<bool>("MigrateOnStart"))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<QuotebookDbContext>().Database.MigrateAsync();
    }
    await RoleSeeder.SeedAsync(app.Services);

    if (app.Configuration.GetValue<bool>("Demo:Enabled"))
    {
        // Fill the demo business now. If the database is asleep or unreachable, log it and still start;
        // the six-hourly reset will fill it later.
        try { await app.Services.GetRequiredService<DemoSeeder>().ResetAsync(); }
        catch (Exception e) { app.Logger.LogError(e, "Could not seed the demo business at startup."); }
    }
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// A missing page gets the same friendly page as an error, with the real status code kept.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
// Static files are endpoints too, so the require-sign-in fallback policy would send signed-out visitors
// a redirect to the login page instead of the stylesheet, leaving every public page unstyled.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
