using Microsoft.EntityFrameworkCore;
using Quotebook.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Connection string comes from user-secrets in development and an environment
// variable in production (ConnectionStrings__Default). It is never committed.
var connection = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connection))
{
    builder.Services.AddDbContext<QuotebookDbContext>(o => o.UseNpgsql(connection));
    builder.Services.AddHealthChecks().AddDbContextCheck<QuotebookDbContext>("database");
}
else
{
    builder.Services.AddHealthChecks();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
