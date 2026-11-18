using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>Reads the fixed, read-only <c>Role</c> table.</summary>
public sealed class RoleRepository
{
    public async Task<IReadOnlyList<Role>> GetAllAsync()
    {
        var roles = new List<Role>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand("SELECT RoleID, RoleName FROM Role ORDER BY RoleID;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            roles.Add(new Role { RoleId = reader.GetInt32(0), RoleName = reader.GetString(1) });
        return roles;
    }
}
