using Microsoft.Data.SqlClient;

namespace VetCare.Models.Data;

/// <summary>Where the central VetCare database lives. Change <see cref="ConnectionString"/> to point elsewhere.</summary>
public static class Database
{
    /// <summary>
    /// SQL Server Express on this PC, database <c>db_vetcare</c>, signed in with the Windows account.
    /// For another server use e.g. <c>Server=SERVERNAME\SQLEXPRESS;Database=db_vetcare;User Id=...;Password=...;TrustServerCertificate=True;</c>
    /// </summary>
    public static string ConnectionString { get; set; } =
        @"Server=.\SQLEXPRESS;Database=db_vetcare;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=8;";

    public static async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
