using Quotebook.Core;

namespace Quotebook.Data;

/// <summary>Used by design-time tooling and anywhere no user is in scope.</summary>
public sealed class NoTenant : ITenantProvider
{
    public string? TenantId => null;
}
