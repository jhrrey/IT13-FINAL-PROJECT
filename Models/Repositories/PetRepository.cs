using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>
/// All SQL for the <c>Pet</c> table. The Pet table has no ClinicID, so every query reaches the
/// clinic through the owner (<c>Owner.ClinicID = @ClinicID</c>). Parameterized, ignores archived
/// pets (IsDeleted = 1), written for SQL Server 2008 (ROW_NUMBER paging).
/// </summary>
public sealed class PetRepository
{
    private const string SelectColumns = @"
p.PetID, p.OwnerID, p.PetName, p.Species, p.Breed, p.Sex, p.BirthDate, p.Color, p.Weight,
p.CreatedAt, p.UpdatedAt, p.IsDeleted, p.SyncStatus,
o.FirstName AS OwnerFirstName, o.LastName AS OwnerLastName, o.ContactNo AS OwnerContactNo";

    /// <summary>Pets of the clinic whose owner is active (archived owners' pets stay out of the list).</summary>
    private const string ListScope = "o.ClinicID = @ClinicID AND p.IsDeleted = 0 AND o.IsDeleted = 0";

    /// <summary>One page of the clinic's pets, newest first.</summary>
    public async Task<PagedResult<Pet>> SearchAsync(PetQuery query)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Max(1, query.PageSize);

        string where = ListScope;
        bool hasSearch = !string.IsNullOrWhiteSpace(query.Search);
        if (hasSearch)
            where += @" AND (p.PetName LIKE @Search
                OR o.FirstName + ' ' + o.LastName LIKE @Search
                OR o.LastName + ' ' + o.FirstName LIKE @Search)";
        if (!string.IsNullOrEmpty(query.Species)) where += " AND p.Species = @Species";
        if (!string.IsNullOrEmpty(query.Sex)) where += " AND p.Sex = @Sex";

        string sql = @"
WITH Matches AS (
    SELECT " + SelectColumns + @",
           ROW_NUMBER() OVER (ORDER BY p.CreatedAt DESC, p.PetID DESC) AS RowNum
    FROM Pet p
    JOIN Owner o ON o.OwnerID = p.OwnerID
    WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;

SELECT COUNT(*) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID WHERE " + where + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);
        if (hasSearch) command.Parameters.AddWithValue("@Search", "%" + EscapeLike(query.Search.Trim()) + "%");
        if (!string.IsNullOrEmpty(query.Species)) command.Parameters.AddWithValue("@Species", query.Species);
        if (!string.IsNullOrEmpty(query.Sex)) command.Parameters.AddWithValue("@Sex", query.Sex);

        var pets = new List<Pet>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) pets.Add(Read(reader));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }

        return new PagedResult<Pet>(pets, total, page, pageSize);
    }

    /// <summary>The pet if its owner belongs to <paramref name="clinicId"/> and it isn't archived; otherwise null.</summary>
    public async Task<Pet> GetByIdAsync(int clinicId, int petId)
    {
        string sql = "SELECT " + SelectColumns + @"
            FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
            WHERE p.PetID = @PetID AND o.ClinicID = @ClinicID AND p.IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@PetID", petId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    /// <summary>The owner's active pets, oldest registration first.</summary>
    public async Task<IReadOnlyList<Pet>> GetByOwnerAsync(int clinicId, int ownerId)
    {
        string sql = "SELECT " + SelectColumns + @"
            FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
            WHERE p.OwnerID = @OwnerID AND o.ClinicID = @ClinicID AND p.IsDeleted = 0
            ORDER BY p.CreatedAt, p.PetID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@OwnerID", ownerId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);

        var pets = new List<Pet>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) pets.Add(Read(reader));
        return pets;
    }

    /// <summary>OwnerID → names of their active pets (oldest first), for the given owners of the clinic.</summary>
    public async Task<IDictionary<int, List<string>>> GetNamesByOwnersAsync(int clinicId, IReadOnlyCollection<int> ownerIds)
    {
        var result = new Dictionary<int, List<string>>();
        if (ownerIds.Count == 0) return result;

        // One parameter per owner (a page holds at most a few dozen).
        var names = ownerIds.Select((id, i) => "@O" + i).ToList();
        string sql = @"
SELECT p.OwnerID, p.PetName
FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE o.ClinicID = @ClinicID AND p.IsDeleted = 0 AND p.OwnerID IN (" + string.Join(", ", names) + @")
ORDER BY p.OwnerID, p.CreatedAt, p.PetID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        int index = 0;
        foreach (int id in ownerIds) command.Parameters.AddWithValue(names[index++], id);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int ownerId = reader.GetInt32(0);
            if (!result.TryGetValue(ownerId, out var list)) result[ownerId] = list = new List<string>();
            list.Add(reader.GetString(1));
        }
        return result;
    }

    /// <summary>Total, dogs and cats among the clinic's listed pets.</summary>
    public async Task<(int Total, int Dogs, int Cats)> GetCountsAsync(int clinicId)
    {
        string sql = @"
SELECT COUNT(*),
       SUM(CASE WHEN p.Species = 'Dog' THEN 1 ELSE 0 END),
       SUM(CASE WHEN p.Species = 'Cat' THEN 1 ELSE 0 END)
FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE " + ListScope + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        int Value(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt32(i);
        return (Value(0), Value(1), Value(2));
    }

    /// <summary>Rules 2 and 10: true if the owner is active and belongs to the clinic.</summary>
    public async Task<bool> OwnerInClinicAsync(int clinicId, int ownerId)
    {
        const string sql = "SELECT COUNT(*) FROM Owner WHERE OwnerID = @OwnerID AND ClinicID = @ClinicID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@OwnerID", ownerId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        return (int)await command.ExecuteScalarAsync() > 0;
    }

    /// <summary>Inserts the pet (CreatedAt / UpdatedAt set by the database) and returns the new PetID.</summary>
    public async Task<int> InsertAsync(Pet pet)
    {
        await using var connection = await Database.OpenAsync();
        return await InsertAsync(pet, connection, null);
    }

    /// <summary>The same insert inside the caller's transaction (guest check-in, walk-in registration).</summary>
    internal static async Task<int> InsertAsync(Pet pet, SqlConnection connection, SqlTransaction tx)
    {
        const string sql = @"
INSERT INTO Pet (OwnerID, PetName, Species, Breed, Sex, BirthDate, Color, Weight, SyncStatus)
VALUES (@OwnerID, @PetName, @Species, @Breed, @Sex, @BirthDate, @Color, @Weight, @SyncStatus);
SELECT CAST(SCOPE_IDENTITY() AS int);";
        await using var command = new SqlCommand(sql, connection, tx);
        AddEditableParameters(command, pet);
        command.Parameters.AddWithValue("@SyncStatus", pet.SyncStatus ?? SyncStatuses.Synced);

        pet.PetId = (int)await command.ExecuteScalarAsync();
        return pet.PetId;
    }

    /// <summary>
    /// Saves the editable fields (including OwnerID, so a transfer is an update) and stamps UpdatedAt.
    /// Only touches a pet whose current owner belongs to <paramref name="clinicId"/>. Returns false if none.
    /// </summary>
    public async Task<bool> UpdateAsync(int clinicId, Pet pet)
    {
        const string sql = @"
UPDATE p
SET OwnerID = @OwnerID, PetName = @PetName, Species = @Species, Breed = @Breed, Sex = @Sex,
    BirthDate = @BirthDate, Color = @Color, Weight = @Weight,
    UpdatedAt = GETDATE(), SyncStatus = @SyncStatus
FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE p.PetID = @PetID AND o.ClinicID = @ClinicID AND p.IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddEditableParameters(command, pet);
        command.Parameters.AddWithValue("@PetID", pet.PetId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Rule 10: moves the pet to another owner (the controller has checked the clinic).</summary>
    public async Task<bool> TransferAsync(int clinicId, int petId, int newOwnerId)
    {
        const string sql = @"
UPDATE p SET OwnerID = @NewOwnerID, UpdatedAt = GETDATE(), SyncStatus = @SyncStatus
FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE p.PetID = @PetID AND o.ClinicID = @ClinicID AND p.IsDeleted = 0
  AND EXISTS (SELECT 1 FROM Owner n WHERE n.OwnerID = @NewOwnerID AND n.ClinicID = @ClinicID AND n.IsDeleted = 0);";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@PetID", petId);
        command.Parameters.AddWithValue("@NewOwnerID", newOwnerId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Rule 7: archives (soft-deletes) the pet so its medical history is kept.</summary>
    public async Task ArchiveAsync(int clinicId, int petId)
    {
        const string sql = @"
UPDATE p SET IsDeleted = 1, UpdatedAt = GETDATE(), SyncStatus = @SyncStatus
FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE p.PetID = @PetID AND o.ClinicID = @ClinicID AND p.IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@PetID", petId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddEditableParameters(SqlCommand command, Pet pet)
    {
        command.Parameters.AddWithValue("@OwnerID", pet.OwnerId);
        command.Parameters.AddWithValue("@PetName", pet.PetName);
        command.Parameters.AddWithValue("@Species", pet.Species);
        command.Parameters.AddWithValue("@Breed", (object)pet.Breed ?? DBNull.Value);
        command.Parameters.AddWithValue("@Sex", pet.Sex);
        command.Parameters.Add(new SqlParameter("@BirthDate", SqlDbType.Date) { Value = (object)pet.BirthDate?.Date ?? DBNull.Value });
        command.Parameters.AddWithValue("@Color", (object)pet.Color ?? DBNull.Value);
        command.Parameters.Add(new SqlParameter("@Weight", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object)pet.Weight ?? DBNull.Value });
    }

    /// <summary>Makes %, _ and [ match literally inside a LIKE pattern.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static Pet Read(SqlDataReader reader)
    {
        string Text(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }

        int birth = reader.GetOrdinal("BirthDate");
        int weight = reader.GetOrdinal("Weight");
        return new Pet
        {
            PetId = reader.GetInt32(reader.GetOrdinal("PetID")),
            OwnerId = reader.GetInt32(reader.GetOrdinal("OwnerID")),
            PetName = Text("PetName"),
            Species = Text("Species"),
            Breed = Text("Breed"),
            Sex = Text("Sex"),
            BirthDate = reader.IsDBNull(birth) ? null : reader.GetDateTime(birth),
            Color = Text("Color"),
            Weight = reader.IsDBNull(weight) ? null : reader.GetDecimal(weight),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
            SyncStatus = Text("SyncStatus"),
            OwnerFirstName = Text("OwnerFirstName"),
            OwnerLastName = Text("OwnerLastName"),
            OwnerContactNo = Text("OwnerContactNo")
        };
    }
}
