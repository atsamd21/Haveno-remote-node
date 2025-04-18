using Microsoft.Data.Sqlite;

namespace Manta.Remote.Helpers;

public class PasswordHelper
{
    public static string GetPassword()
    {
        string dbPath = "appdata.db";
        string connectionString = $"Data Source={dbPath}";

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var createTableCmd = connection.CreateCommand();
        createTableCmd.CommandText =
        @"
            CREATE TABLE IF NOT EXISTS AppData (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL UNIQUE,
                Value TEXT NOT NULL
            );
        ";

        createTableCmd.ExecuteNonQuery();

        string? password = null;

        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT Value FROM AppData WHERE Key = $key";
        selectCmd.Parameters.AddWithValue("$key", "password");

        using (var reader = selectCmd.ExecuteReader())
        {
            if (reader.Read())
            {
                password = reader.GetString(0);
            }
        }

        if (password is null)
        {
            password = Guid.NewGuid().ToString();
            var insertCmd = connection.CreateCommand();

            insertCmd.CommandText = "INSERT INTO AppData (Key, Value) VALUES ($key, $value)";
            insertCmd.Parameters.AddWithValue("$key", "password");
            insertCmd.Parameters.AddWithValue("$value", password);
            insertCmd.ExecuteNonQuery();
        }

        return password;
    }
}
