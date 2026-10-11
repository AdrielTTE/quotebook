namespace Quotebook.Core;

/// <summary>
/// Which business ("tenant") the current request acts for. It is the owner's user id.
/// Null means nobody is signed in, and every tenant-scoped query then returns nothing.
/// </summary>
public interface ITenantProvider
{
    string? TenantId { get; }
}
