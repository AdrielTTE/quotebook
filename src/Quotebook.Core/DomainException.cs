namespace Quotebook.Core;

/// <summary>A business rule was broken (e.g. accepting a rejected quote).</summary>
public sealed class DomainException(string message) : Exception(message);
