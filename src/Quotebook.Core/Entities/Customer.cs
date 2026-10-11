namespace Quotebook.Core.Entities;

public class Customer
{
    public int Id { get; set; }
    /// <summary>Identity user id of the business owner this customer belongs to.</summary>
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
