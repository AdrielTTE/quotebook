using Quotebook.Core.Entities;

namespace Quotebook.Web.Infrastructure;

/// <summary>Small display helpers shared by several pages.</summary>
public static class Presentation
{
    /// <summary>CSS class for the status dot and word (see .status in ledger.css).</summary>
    public static string Css(this InvoiceStatus status) => status.ToString().ToLowerInvariant();

    public static string Css(this QuoteStatus status) => status.ToString().ToLowerInvariant();

    public static string Label(this InvoiceStatus status) => status == InvoiceStatus.Partial ? "Partly paid" : status.ToString();

    public static string Label(this QuoteStatus status) => status.ToString();

    public static string Rm(this decimal amount) => amount.ToString("N2");
}
