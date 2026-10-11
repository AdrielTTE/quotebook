using Quotebook.Core.Entities;

namespace Quotebook.Web.Infrastructure;

/// <summary>Small display helpers shared by several pages.</summary>
public static class Presentation
{
    public static string BadgeClass(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => "success",
        InvoiceStatus.Partial => "info",
        InvoiceStatus.Overdue => "danger",
        _ => "secondary",
    };

    public static string Rm(this decimal amount) => amount.ToString("N2");
}
