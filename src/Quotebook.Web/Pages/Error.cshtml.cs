using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Quotebook.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; private set; }

    /// <summary>The status the visitor actually got (404 for a missing page, 500 for a crash).</summary>
    public int Status { get; private set; }

    public bool NotFound => Status == StatusCodes.Status404NotFound;

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        Status = Response.StatusCode is >= 400 ? Response.StatusCode : StatusCodes.Status500InternalServerError;
    }
}
