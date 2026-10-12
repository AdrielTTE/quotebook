namespace Quotebook.Core.Entities;

/// <summary>
/// The business behind a tenant: the name and contact details printed at the top of its quotations
/// and invoices. The id is the tenant id (the owner's user id), so there is at most one per business.
/// </summary>
public class Business
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
