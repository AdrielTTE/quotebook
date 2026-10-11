using Quotebook.Core;

namespace Quotebook.Data;

/// <summary>Acts for one specific business, for background work that runs outside a web request.</summary>
public sealed class FixedTenant(string tenantId) : ITenantProvider
{
    public string? TenantId { get; } = tenantId;
}
