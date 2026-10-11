namespace Quotebook.Core;

public static class Roles
{
    /// <summary>Runs the business: sees everything and manages settings.</summary>
    public const string Owner = "Owner";
    /// <summary>Works inside the owner's business: quotes, invoices and payments.</summary>
    public const string Staff = "Staff";
}
