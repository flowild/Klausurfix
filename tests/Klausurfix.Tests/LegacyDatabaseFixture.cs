namespace Klausurfix.Tests;

using System.Text.Json;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

// Constructs synthetic 0.1 databases for compatibility tests. Never used by the app.
internal static class LegacyDatabaseFixture
{
    internal static readonly (string Table, string[] Keys, string[] Fields)[] Tables =
    [
        ("schulformen", ["id"], ["name", "profile_json_enc", "created_at"]),
        ("classes", ["id"], ["name", "schulform_name", "created_at"]),
        ("students", ["id"], ["first_name_enc", "last_name_enc"]),
        ("exams", ["id"], ["class_name", "schulform_name", "title", "date", "tasks_json", "profile_snapshot_json", "created_at", "updated_at"]),
        ("exam_cover_sheets", ["exam_id"], ["document_enc"]),
        ("exam_entries", ["exam_id", "student_id"], ["student_name_enc", "task_points_enc", "note_enc"])
    ];

    internal static void Downgrade(byte[] key)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
            connection.Open();
            using var transaction = connection.BeginTransaction();
            foreach (var (table, keys, fields) in Tables)
            {
                using var select = connection.CreateCommand();
                select.Transaction = transaction;
                select.CommandText = $"SELECT {string.Join(",", keys.Concat(fields))} FROM {table}";
                var rows = new List<(string[] Ids, string?[] Values)>();
                using (var reader = select.ExecuteReader())
                    while (reader.Read())
                        rows.Add((Enumerable.Range(0, keys.Length).Select(reader.GetString).ToArray(),
                            Enumerable.Range(keys.Length, fields.Length).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i)).ToArray()));
                foreach (var (ids, values) in rows)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = $"UPDATE {table} SET " + string.Join(",", fields.Select((f, i) => $"{f}=$v{i}")) +
                        " WHERE " + string.Join(" AND ", keys.Select((k, i) => $"{k}=$k{i}"));
                    for (var i = 0; i < ids.Length; i++) update.Parameters.AddWithValue($"$k{i}", ids[i]);
                    for (var i = 0; i < fields.Length; i++)
                    {
                        string? legacy = null;
                        if (values[i] is { } value)
                        {
                            Assert.StartsWith("kf2:", value);
                            var context = JsonSerializer.SerializeToUtf8Bytes(new[] { "Klausurfix.DatabaseField", "2", table, fields[i] }.Concat(ids));
                            var plaintext = CryptoService.DecryptString(value[4..], key, context);
                            legacy = CryptoService.EncryptString(plaintext, key);
                            if (!fields[i].EndsWith("_enc", StringComparison.Ordinal)) legacy = "kf1:" + legacy;
                        }
                        update.Parameters.AddWithValue($"$v{i}", (object?)legacy ?? DBNull.Value);
                    }
                    update.ExecuteNonQuery();
                }
            }
            using var reset = connection.CreateCommand();
            reset.Transaction = transaction;
            reset.CommandText = "DELETE FROM app_migrations WHERE name='database_fields_aad_v2'; PRAGMA user_version=2";
            reset.ExecuteNonQuery();
            transaction.Commit();
        }
        finally { CryptoService.ZeroMemory(key); }
    }
}
