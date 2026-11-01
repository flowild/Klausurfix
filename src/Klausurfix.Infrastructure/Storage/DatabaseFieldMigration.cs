namespace Klausurfix.Infrastructure.Storage;

using System.Security.Cryptography;
using System.Text.Json;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Security;
using Microsoft.Data.Sqlite;

internal static class DatabaseFieldMigration
{
    private const string MigrationName = "database_fields_aad_v2";
    private sealed record Table(string Name, string[] Keys, string[] Fields, string[] LegacyPlaintext);
    private sealed record Row(Table Table, string[] Keys, string?[] Values);
    private sealed record StoredEntry(Dictionary<int, decimal?> TaskPoints, int? WordCount, int? ErrorCount, StudentExamStatus Status);

    // Identifiers are fixed program constants, never input or database contents.
    private static readonly Table[] Tables =
    [
        new("schulformen", ["id"], ["name", "profile_json_enc", "created_at"], ["name", "created_at"]),
        new("classes", ["id"], ["name", "schulform_name", "created_at"], ["name", "schulform_name", "created_at"]),
        new("students", ["id"], ["first_name_enc", "last_name_enc"], []),
        new("exams", ["id"], ["class_name", "schulform_name", "title", "date", "tasks_json", "profile_snapshot_json", "created_at", "updated_at"],
            ["class_name", "schulform_name", "title", "date", "tasks_json", "profile_snapshot_json", "created_at", "updated_at"]),
        new("exam_cover_sheets", ["exam_id"], ["document_enc"], []),
        new("exam_entries", ["exam_id", "student_id"], ["student_name_enc", "task_points_enc", "note_enc"], [])
    ];

    internal static void Migrate(SqliteConnection connection, byte[] key)
    {
        using (var transaction = connection.BeginTransaction(deferred: false))
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt32(command.ExecuteScalar());
            if (version > 4) throw new InvalidOperationException("Die Datenbankversion wird nicht unterstützt.");
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM app_migrations WHERE name=$name)";
            command.Parameters.AddWithValue("$name", MigrationName);
            var complete = Convert.ToInt32(command.ExecuteScalar()) != 0;
            if (complete && version == 4) return;
            if (!complete)
            {
                if (version >= 3) throw new CryptographicException("Ungültiger Stand der Datenmigration.");
                var rows = ReadRows(connection, transaction, key);
                // Nothing is written until every legacy ciphertext authenticates.
                foreach (var row in rows)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = $"UPDATE {row.Table.Name} SET " +
                        string.Join(",", row.Table.Fields.Select((f, i) => $"{f}=$v{i}")) + " WHERE " +
                        string.Join(" AND ", row.Table.Keys.Select((f, i) => $"{f}=$k{i}"));
                    for (var i = 0; i < row.Keys.Length; i++) update.Parameters.AddWithValue($"$k{i}", row.Keys[i]);
                    for (var i = 0; i < row.Values.Length; i++)
                        update.Parameters.AddWithValue($"$v{i}", row.Values[i] is { } value
                            ? DatabaseFieldProtection.Protect(value, key, row.Table.Name, row.Table.Fields[i], row.Keys) : DBNull.Value);
                    update.ExecuteNonQuery();
                }
                // Clear legacy plaintext counters only after incorporating them in
                // the authenticated entry. Archive markers and snapshots stay intact.
                command.CommandText = "UPDATE exam_entries SET status=0,word_count=NULL,error_count=NULL; " +
                    "INSERT INTO app_migrations(name) VALUES ($name); PRAGMA user_version=3;";
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        // A committed version 3 resumes cleanup after an interruption. No plaintext
        // or unbound ciphertext is accepted by the regular database readers.
        SqliteConnection.ClearAllPools();
        using var cleanup = connection.CreateCommand();
        void Checkpoint()
        {
            cleanup.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            using var reader = cleanup.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(0) != 0)
                throw new InvalidOperationException("Die Bereinigung alter Daten konnte noch nicht abgeschlossen werden.");
        }
        Checkpoint();
        cleanup.CommandText = "VACUUM";
        cleanup.ExecuteNonQuery();
        Checkpoint();
        cleanup.CommandText = "PRAGMA user_version=4";
        cleanup.ExecuteNonQuery();
    }

    private static List<Row> ReadRows(SqliteConnection connection, SqliteTransaction transaction, byte[] key)
    {
        var rows = new List<Row>();
        bool hasCurrent = false, hasLegacy = false;
        foreach (var table in Tables)
        {
            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = $"SELECT {string.Join(",", table.Keys.Concat(table.Fields))}" +
                (table.Name == "exam_entries" ? ",status,word_count,error_count" : "") + $" FROM {table.Name}";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var ids = Enumerable.Range(0, table.Keys.Length).Select(reader.GetString).ToArray();
                var values = new string?[table.Fields.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    var ordinal = ids.Length + i;
                    if (reader.IsDBNull(ordinal)) continue;
                    var stored = reader.GetString(ordinal);
                    if (stored.StartsWith(DatabaseFieldProtection.Prefix, StringComparison.Ordinal))
                    {
                        if (hasLegacy) throw new CryptographicException("Vermischte Verschlüsselungsformate werden nicht akzeptiert.");
                        hasCurrent = true;
                        values[i] = DatabaseFieldProtection.Unprotect(stored, key, table.Name, table.Fields[i], ids);
                    }
                    else
                    {
                        if (hasCurrent) throw new CryptographicException("Vermischte Verschlüsselungsformate werden nicht akzeptiert.");
                        hasLegacy = true;
                        values[i] = table.LegacyPlaintext.Contains(table.Fields[i])
                            ? stored.StartsWith("kf1:", StringComparison.Ordinal) ? CryptoService.DecryptString(stored[4..], key) : stored
                            : CryptoService.DecryptString(stored, key);
                    }
                }
                if (table.Name == "exam_entries")
                {
                    using var points = JsonDocument.Parse(values[1]!);
                    if (!points.RootElement.TryGetProperty("TaskPoints", out _))
                    {
                        var offset = ids.Length + values.Length;
                        values[1] = JsonSerializer.Serialize(new StoredEntry(
                            JsonSerializer.Deserialize<Dictionary<int, decimal?>>(values[1]!)!,
                            reader.IsDBNull(offset + 1) ? null : reader.GetInt32(offset + 1),
                            reader.IsDBNull(offset + 2) ? null : reader.GetInt32(offset + 2),
                            (StudentExamStatus)reader.GetInt32(offset)));
                    }
                }
                rows.Add(new Row(table, ids, values));
            }
        }
        return rows;
    }
}
