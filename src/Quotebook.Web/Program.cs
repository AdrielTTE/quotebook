using Microsoft.AspNetCore.Authorization;
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

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, ClaimsTenantProvider>();

// Connection string comes from user-secrets in development and an environment
// variable in production (ConnectionStrings__Default). It is never committed.
var connection = builder.Configuration.GetConnectionString("Default");
var hasDatabase = !string.IsNullOrWhiteSpace(connection);
if (hasDatabase)
{
    builder.Services.AddDbContext<QuotebookDbContext>(o => o.UseNpgsql(connection));
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
        .AddClaimsPrincipalFactory<TenantClaimsFactory>();

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

if (hasDatabase) await RoleSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
