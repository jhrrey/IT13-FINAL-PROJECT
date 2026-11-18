namespace VetCare.Models.Entities;

/// <summary>A row of the fixed, read-only <c>Role</c> table.</summary>
public sealed class Role
{
    public int RoleId { get; set; }
    public string RoleName { get; set; }
}
