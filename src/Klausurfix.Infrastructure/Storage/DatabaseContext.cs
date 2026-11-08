namespace Klausurfix.Infrastructure.Storage;

using System.Text.Json;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Klausurfix.Infrastructure.Backup;
using static Klausurfix.Infrastructure.Storage.DatabaseFieldProtection;

public sealed class ArchivedExamReadOnlyException : InvalidOperationException
{
    public const string Description = "Diese Klausur ist archiviert und schreibgeschützt. Bitte stellen Sie sie vor dem Bearbeiten wieder her.";
    public ArchivedExamReadOnlyException() : base(Description) { }
}

public class DatabaseContext
{
    private readonly KeyManager _keyManager;
    private readonly string? _snapshotConnectionString;
    private record StoredEntry(Dictionary<int, decimal?> TaskPoints, int? WordCount, int? ErrorCount, StudentExamStatus Status, Dictionary<int, string>? TaskPointExpressions = null);
    private static string ProtectEntry(ExamStudentEntry entry, byte[] key, string examId) => Protect(
        JsonSerializer.Serialize(new StoredEntry(entry.TaskPoints, entry.WordCount, entry.ErrorCount, entry.Status, entry.TaskPointExpressions)),
        key, "exam_entries", "task_points_enc", examId, entry.StudentId);

    public DatabaseContext(KeyManager keyManager)
    {
        _keyManager = keyManager;
    }

    private DatabaseContext(KeyManager keyManager, string connectionString)
    {
        _keyManager = keyManager;
        _snapshotConnectionString = connectionString;
    }

    public T WithSnapshot<T>(Func<DatabaseContext, T> read)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "klausurfix-snapshot-" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared, Pooling = false
        }.ToString();
        using var source = CreateConnection();
        using var snapshot = new SqliteConnection(connectionString);
        snapshot.Open();
        source.BackupDatabase(snapshot);
        return read(new DatabaseContext(_keyManager, connectionString));
    }

    private SqliteConnection CreateConnection()
    {
        AppPaths.EnsureDirectoriesExist();
        var connStr = _snapshotConnectionString ?? new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var connection = new SqliteConnection(connStr);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();

        return connection;
    }

    public void InitializeDatabase()
    {
        using var conn = CreateConnection();
        var statements = new[]
        {
            @"CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY
            );",
            @"CREATE TABLE IF NOT EXISTS schulformen (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                profile_json_enc TEXT NOT NULL,
                is_archived INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS classes (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                schulform_id TEXT NOT NULL,
                schulform_name TEXT NOT NULL,
                is_archived INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS students (
                id TEXT PRIMARY KEY,
                class_id TEXT NOT NULL,
                first_name_enc TEXT NOT NULL,
                last_name_enc TEXT NOT NULL,
                order_index INTEGER NOT NULL DEFAULT 0,
                is_archived INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(class_id) REFERENCES classes(id) ON DELETE CASCADE
            );",
            @"CREATE TABLE IF NOT EXISTS exams (
                id TEXT PRIMARY KEY,
                class_id TEXT NOT NULL,
                class_name TEXT NOT NULL,
                schulform_id TEXT NOT NULL,
                schulform_name TEXT NOT NULL,
                title TEXT NOT NULL,
                date TEXT NOT NULL,
                tasks_json TEXT NOT NULL,
                profile_snapshot_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS exam_cover_sheets (
                exam_id TEXT PRIMARY KEY,
                document_enc TEXT NOT NULL,
                FOREIGN KEY(exam_id) REFERENCES exams(id) ON DELETE CASCADE
            );",
            @"CREATE TABLE IF NOT EXISTS archived_exams (
                exam_id TEXT PRIMARY KEY,
                FOREIGN KEY(exam_id) REFERENCES exams(id) ON DELETE CASCADE
            );",
            @"CREATE TABLE IF NOT EXISTS app_migrations (name TEXT PRIMARY KEY);
              INSERT OR IGNORE INTO app_migrations(name) VALUES ('exam_cover_sheets_v1'), ('archived_exams_v1');",
            @"CREATE TABLE IF NOT EXISTS exam_entries (
                exam_id TEXT NOT NULL,
                student_id TEXT NOT NULL,
                student_name_enc TEXT NOT NULL,
                status INTEGER NOT NULL DEFAULT 1,
                task_points_enc TEXT NOT NULL,
                word_count INTEGER,
                error_count INTEGER,
                note_enc TEXT,
                PRIMARY KEY(exam_id, student_id),
                FOREIGN KEY(exam_id) REFERENCES exams(id) ON DELETE CASCADE
            );"
        };

        foreach (var sql in statements)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        MigrateLegacyPlaintext();
    }

    // Replace a validated backup as one SQLite transaction. A failure at any
    // point rolls back both the deletion and all inserted rows.
    public void ReplaceFromBackup(BackupPayload payload, bool validateProfiles = true)
    {
        if (payload.Version is not (1 or 2 or 3 or 4 or 5) || payload.Schulformen == null || payload.Classes == null ||
            payload.Students == null || payload.Exams == null)
            throw new InvalidOperationException("Ungültige Sicherungsdaten.");
        if (validateProfiles) foreach (var sf in payload.Schulformen) sf.Profile.Validate();
        foreach (var exam in payload.Exams) exam.CoverSheet?.Validate(exam);
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        using var conn = CreateConnection();
        using var transaction = conn.BeginTransaction();
        void Execute(string sql, params (string Name, object? Value)[] args)
        {
            using var command = conn.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        try
        {
            Execute("DELETE FROM exam_entries; DELETE FROM exams; DELETE FROM students; DELETE FROM classes; DELETE FROM schulformen;");
            foreach (var sf in payload.Schulformen)
                Execute("INSERT INTO schulformen (id,name,profile_json_enc,is_archived,created_at) VALUES ($id,$name,$profile,$arch,$created)",
                    ("$id", sf.Id), ("$name", Protect(sf.Name, dek, "schulformen", "name", sf.Id)), ("$profile", Protect(JsonSerializer.Serialize(sf.Profile), dek, "schulformen", "profile_json_enc", sf.Id)),
                    ("$arch", sf.IsArchived ? 1 : 0), ("$created", Protect(sf.CreatedAt.ToString("O"), dek, "schulformen", "created_at", sf.Id)));
            foreach (var cls in payload.Classes)
                Execute("INSERT INTO classes (id,name,schulform_id,schulform_name,is_archived,created_at) VALUES ($id,$name,$sf,$sfname,$arch,$created)",
                    ("$id", cls.Id), ("$name", Protect(cls.Name, dek, "classes", "name", cls.Id)), ("$sf", cls.SchulformId), ("$sfname", Protect(cls.SchulformName, dek, "classes", "schulform_name", cls.Id)),
                    ("$arch", cls.IsArchived ? 1 : 0), ("$created", Protect(cls.CreatedAt.ToString("O"), dek, "classes", "created_at", cls.Id)));
            foreach (var student in payload.Students)
                Execute("INSERT INTO students (id,class_id,first_name_enc,last_name_enc,order_index,is_archived) VALUES ($id,$class,$first,$last,$ord,$arch)",
                    ("$id", student.Id), ("$class", student.ClassId), ("$first", Protect(student.FirstName, dek, "students", "first_name_enc", student.Id)),
                    ("$last", Protect(student.LastName, dek, "students", "last_name_enc", student.Id)), ("$ord", student.OrderIndex), ("$arch", student.IsArchived ? 1 : 0));
            foreach (var exam in payload.Exams)
            {
                if (validateProfiles) exam.GradingProfileSnapshot.Validate();
                Execute("INSERT INTO exams (id,class_id,class_name,schulform_id,schulform_name,title,date,tasks_json,profile_snapshot_json,created_at,updated_at) VALUES ($id,$class,$className,$sf,$sfName,$title,$date,$tasks,$profile,$created,$updated)",
                    ("$id", exam.Id), ("$class", exam.ClassId), ("$className", Protect(exam.ClassName, dek, "exams", "class_name", exam.Id)), ("$sf", exam.SchulformId),
                    ("$sfName", Protect(exam.SchulformName, dek, "exams", "schulform_name", exam.Id)), ("$title", Protect(exam.Title, dek, "exams", "title", exam.Id)), ("$date", Protect(exam.Date.ToString("O"), dek, "exams", "date", exam.Id)),
                    ("$tasks", Protect(JsonSerializer.Serialize(exam.Tasks), dek, "exams", "tasks_json", exam.Id)), ("$profile", Protect(JsonSerializer.Serialize(exam.GradingProfileSnapshot), dek, "exams", "profile_snapshot_json", exam.Id)),
                    ("$created", Protect(exam.CreatedAt.ToString("O"), dek, "exams", "created_at", exam.Id)), ("$updated", Protect(exam.UpdatedAt.ToString("O"), dek, "exams", "updated_at", exam.Id)));
                if (exam.CoverSheet != null)
                    Execute("INSERT INTO exam_cover_sheets (exam_id,document_enc) VALUES ($id,$document)",
                        ("$id", exam.Id), ("$document", Protect(JsonSerializer.Serialize(exam.CoverSheet), dek, "exam_cover_sheets", "document_enc", exam.Id)));
                if (exam.IsArchived)
                    Execute("INSERT INTO archived_exams (exam_id) VALUES ($id)", ("$id", exam.Id));
                foreach (var entry in exam.StudentEntries)
                    Execute("INSERT INTO exam_entries (exam_id,student_id,student_name_enc,status,task_points_enc,word_count,error_count,note_enc) VALUES ($exam,$student,$name,$status,$points,$words,$errors,$note)",
                        ("$exam", exam.Id), ("$student", entry.StudentId), ("$name", Protect(entry.StudentName, dek, "exam_entries", "student_name_enc", exam.Id, entry.StudentId)),
                        ("$status", 0), ("$points", ProtectEntry(entry, dek, exam.Id)),
                        ("$words", null), ("$errors", null),
                        ("$note", string.IsNullOrEmpty(entry.Note) ? null : Protect(entry.Note, dek, "exam_entries", "note_enc", exam.Id, entry.StudentId)));
            }
            transaction.Commit();
        }
        finally { CryptoService.ZeroMemory(dek); }
    }

    // Kept as the upgrade entry point for existing callers. Legacy formats are
    // decoded exclusively inside the transactional field migration.
    public void MigrateLegacyPlaintext()
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        try
        {
            using var connection = CreateConnection();
            DatabaseFieldMigration.Migrate(connection, dek);
        }
        finally { CryptoService.ZeroMemory(dek); }
    }

    public void EnsureRequestedSchoolProfiles()
    {
        using (var conn = CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS app_migrations (name TEXT PRIMARY KEY); SELECT COUNT(*) FROM app_migrations WHERE name='requested_school_profiles_v1'";
            if (Convert.ToInt32(cmd.ExecuteScalar()) != 0) return;
        }
        var schools = GetSchulformen(true);
        if (!schools.Any(s => s.Name.Equals("Berufliches Gymnasium", StringComparison.OrdinalIgnoreCase)))
            SaveSchulform(new Schulform { Name = "Berufliches Gymnasium", Profile = GradingProfile.BeruflichesGymnasium() });
        if (!schools.Any(s => s.Name.Equals("Fachschule Sozialwesen", StringComparison.OrdinalIgnoreCase)))
            SaveSchulform(new Schulform { Name = "Fachschule Sozialwesen", Profile = GradingProfile.FachschuleSozialwesen() });
        using var markConnection = CreateConnection();
        using var marker = markConnection.CreateCommand();
        marker.CommandText = "INSERT OR IGNORE INTO app_migrations(name) VALUES ('requested_school_profiles_v1')";
        marker.ExecuteNonQuery();
    }

    #region Schulformen

    public List<Schulform> GetSchulformen(bool includeArchived = false)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        var list = new List<Schulform>();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT id, name, profile_json_enc, is_archived, created_at FROM schulformen ORDER BY name ASC;"
            : "SELECT id, name, profile_json_enc, is_archived, created_at FROM schulformen WHERE is_archived = 0 ORDER BY name ASC;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var name = Unprotect(reader.GetString(1), dek, "schulformen", "name", id);
            var profEnc = reader.GetString(2);
            var isArchived = reader.GetInt32(3) == 1;
            var createdAt = DateTimeOffset.Parse(Unprotect(reader.GetString(4), dek, "schulformen", "created_at", id));

            var profJson = Unprotect(profEnc, dek, "schulformen", "profile_json_enc", id);
            var profile = JsonSerializer.Deserialize<GradingProfile>(profJson) ?? GradingProfile.CreateDefaultSchulnoten();

            list.Add(new Schulform
            {
                Id = id,
                Name = name,
                Profile = profile,
                IsArchived = isArchived,
                CreatedAt = createdAt
            });
        }

        return list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Schulform? GetSchulformById(string id)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, profile_json_enc, is_archived, created_at FROM schulformen WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var name = Unprotect(reader.GetString(1), dek, "schulformen", "name", id);
        var profEnc = reader.GetString(2);
        var isArchived = reader.GetInt32(3) == 1;
        var createdAt = DateTimeOffset.Parse(Unprotect(reader.GetString(4), dek, "schulformen", "created_at", id));

        var profJson = Unprotect(profEnc, dek, "schulformen", "profile_json_enc", id);
        var profile = JsonSerializer.Deserialize<GradingProfile>(profJson) ?? GradingProfile.CreateDefaultSchulnoten();

        return new Schulform
        {
            Id = id,
            Name = name,
            Profile = profile,
            IsArchived = isArchived,
            CreatedAt = createdAt
        };
    }

    public void SaveSchulform(Schulform schulform)
    {
        schulform.Profile.Validate();
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        var profJson = JsonSerializer.Serialize(schulform.Profile);
        var profEnc = Protect(profJson, dek, "schulformen", "profile_json_enc", schulform.Id);

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO schulformen (id, name, profile_json_enc, is_archived, created_at)
VALUES ($id, $name, $profEnc, $isArchived, $createdAt)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    profile_json_enc = excluded.profile_json_enc,
    is_archived = excluded.is_archived;";

        cmd.Parameters.AddWithValue("$id", schulform.Id);
        cmd.Parameters.AddWithValue("$name", Protect(schulform.Name, dek, "schulformen", "name", schulform.Id));
        cmd.Parameters.AddWithValue("$profEnc", profEnc);
        cmd.Parameters.AddWithValue("$isArchived", schulform.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", Protect(schulform.CreatedAt.ToString("O"), dek, "schulformen", "created_at", schulform.Id));

        cmd.ExecuteNonQuery();
    }

    public void DeleteSchulform(string id)
    {
        using var conn = CreateConnection();

        // Check if referenced by classes
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM classes WHERE schulform_id = $id;";
        checkCmd.Parameters.AddWithValue("$id", id);
        var count = Convert.ToInt64(checkCmd.ExecuteScalar());

        if (count > 0)
        {
            // If referenced, archive instead of delete to preserve historical integrity
            using var archCmd = conn.CreateCommand();
            archCmd.CommandText = "UPDATE schulformen SET is_archived = 1 WHERE id = $id;";
            archCmd.Parameters.AddWithValue("$id", id);
            archCmd.ExecuteNonQuery();
        }
        else
        {
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM schulformen WHERE id = $id;";
            delCmd.Parameters.AddWithValue("$id", id);
            delCmd.ExecuteNonQuery();
        }
    }

    #endregion

    #region Classes

    public List<ClassGroup> GetClasses(bool includeArchived = false)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        var list = new List<ClassGroup>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT id, name, schulform_id, schulform_name, is_archived, created_at FROM classes ORDER BY name ASC;"
            : "SELECT id, name, schulform_id, schulform_name, is_archived, created_at FROM classes WHERE is_archived = 0 ORDER BY name ASC;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ClassGroup
            {
                Id = reader.GetString(0),
                Name = Unprotect(reader.GetString(1), dek, "classes", "name", reader.GetString(0)),
                SchulformId = reader.GetString(2),
                SchulformName = Unprotect(reader.GetString(3), dek, "classes", "schulform_name", reader.GetString(0)),
                IsArchived = reader.GetInt32(4) == 1,
                CreatedAt = DateTimeOffset.Parse(Unprotect(reader.GetString(5), dek, "classes", "created_at", reader.GetString(0)))
            });
        }

        return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public ClassGroup? GetClassById(string id)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, schulform_id, schulform_name, is_archived, created_at FROM classes WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new ClassGroup
        {
            Id = reader.GetString(0),
            Name = Unprotect(reader.GetString(1), dek, "classes", "name", reader.GetString(0)),
            SchulformId = reader.GetString(2),
            SchulformName = Unprotect(reader.GetString(3), dek, "classes", "schulform_name", reader.GetString(0)),
            IsArchived = reader.GetInt32(4) == 1,
            CreatedAt = DateTimeOffset.Parse(Unprotect(reader.GetString(5), dek, "classes", "created_at", reader.GetString(0)))
        };
    }

    public void SaveClass(ClassGroup classGroup)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO classes (id, name, schulform_id, schulform_name, is_archived, created_at)
VALUES ($id, $name, $schulformId, $schulformName, $isArchived, $createdAt)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    schulform_id = excluded.schulform_id,
    schulform_name = excluded.schulform_name,
    is_archived = excluded.is_archived;";

        cmd.Parameters.AddWithValue("$id", classGroup.Id);
        cmd.Parameters.AddWithValue("$name", Protect(classGroup.Name, dek, "classes", "name", classGroup.Id));
        cmd.Parameters.AddWithValue("$schulformId", classGroup.SchulformId);
        cmd.Parameters.AddWithValue("$schulformName", Protect(classGroup.SchulformName, dek, "classes", "schulform_name", classGroup.Id));
        cmd.Parameters.AddWithValue("$isArchived", classGroup.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", Protect(classGroup.CreatedAt.ToString("O"), dek, "classes", "created_at", classGroup.Id));

        cmd.ExecuteNonQuery();
    }

    public void DeleteClass(string id)
    {
        using var conn = CreateConnection();

        // Check if referenced by exams
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM exams WHERE class_id = $id;";
        checkCmd.Parameters.AddWithValue("$id", id);
        var count = Convert.ToInt64(checkCmd.ExecuteScalar());

        if (count > 0)
        {
            using var archCmd = conn.CreateCommand();
            archCmd.CommandText = "UPDATE classes SET is_archived = 1 WHERE id = $id;";
            archCmd.Parameters.AddWithValue("$id", id);
            archCmd.ExecuteNonQuery();
        }
        else
        {
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM classes WHERE id = $id;";
            delCmd.Parameters.AddWithValue("$id", id);
            delCmd.ExecuteNonQuery();
        }
    }

    #endregion

    #region Students

    public List<Student> GetStudentsByClass(string classId, bool includeArchived = false)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        var list = new List<Student>();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT id, class_id, first_name_enc, last_name_enc, order_index, is_archived FROM students WHERE class_id = $classId ORDER BY order_index ASC, rowid ASC;"
            : "SELECT id, class_id, first_name_enc, last_name_enc, order_index, is_archived FROM students WHERE class_id = $classId AND is_archived = 0 ORDER BY order_index ASC, rowid ASC;";
        cmd.Parameters.AddWithValue("$classId", classId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var cId = reader.GetString(1);
            var fnEnc = reader.GetString(2);
            var lnEnc = reader.GetString(3);
            var orderIndex = reader.GetInt32(4);
            var isArchived = reader.GetInt32(5) == 1;

            var fn = Unprotect(fnEnc, dek, "students", "first_name_enc", id);
            var ln = Unprotect(lnEnc, dek, "students", "last_name_enc", id);

            list.Add(new Student
            {
                Id = id,
                ClassId = cId,
                FirstName = fn,
                LastName = ln,
                OrderIndex = orderIndex,
                IsArchived = isArchived
            });
        }

        return list;
    }

    public bool ReorderStudents(string classId, IReadOnlyList<string> studentIds)
    {
        using var conn = CreateConnection();
        using var transaction = conn.BeginTransaction(deferred: false);
        using var query = conn.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "SELECT id FROM students WHERE class_id=$classId AND is_archived=0";
        query.Parameters.AddWithValue("$classId", classId);
        var existing = new HashSet<string>();
        using (var reader = query.ExecuteReader())
            while (reader.Read()) existing.Add(reader.GetString(0));
        if (studentIds.Count != existing.Count || studentIds.Any(id => id == null || !existing.Remove(id)) || existing.Count != 0)
            return false;
        for (var i = 0; i < studentIds.Count; i++)
        {
            using var update = conn.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE students SET order_index=$order WHERE class_id=$classId AND id=$id AND is_archived=0";
            update.Parameters.AddWithValue("$order", i);
            update.Parameters.AddWithValue("$classId", classId);
            update.Parameters.AddWithValue("$id", studentIds[i]);
            update.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;
    }

    public void SaveStudent(Student student)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        var fnEnc = Protect(student.FirstName, dek, "students", "first_name_enc", student.Id);
        var lnEnc = Protect(student.LastName, dek, "students", "last_name_enc", student.Id);

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO students (id, class_id, first_name_enc, last_name_enc, order_index, is_archived)
VALUES ($id, $classId, $fnEnc, $lnEnc, $orderIndex, $isArchived)
ON CONFLICT(id) DO UPDATE SET
    first_name_enc = excluded.first_name_enc,
    last_name_enc = excluded.last_name_enc,
    order_index = excluded.order_index,
    is_archived = excluded.is_archived;";

        cmd.Parameters.AddWithValue("$id", student.Id);
        cmd.Parameters.AddWithValue("$classId", student.ClassId);
        cmd.Parameters.AddWithValue("$fnEnc", fnEnc);
        cmd.Parameters.AddWithValue("$lnEnc", lnEnc);
        cmd.Parameters.AddWithValue("$orderIndex", student.OrderIndex);
        cmd.Parameters.AddWithValue("$isArchived", student.IsArchived ? 1 : 0);

        cmd.ExecuteNonQuery();
    }

    public void DeleteStudent(string id)
    {
        using var conn = CreateConnection();

        // Check if student has exam entries
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM exam_entries WHERE student_id = $id;";
        checkCmd.Parameters.AddWithValue("$id", id);
        var count = Convert.ToInt64(checkCmd.ExecuteScalar());

        if (count > 0)
        {
            using var archCmd = conn.CreateCommand();
            archCmd.CommandText = "UPDATE students SET is_archived = 1 WHERE id = $id;";
            archCmd.Parameters.AddWithValue("$id", id);
            archCmd.ExecuteNonQuery();
        }
        else
        {
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM students WHERE id = $id;";
            delCmd.Parameters.AddWithValue("$id", id);
            delCmd.ExecuteNonQuery();
        }
    }

    #endregion

    #region Exams

    public List<Exam> GetExams(bool includeArchived = false)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        var list = new List<Exam>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, class_id, class_name, schulform_id, schulform_name, title, date, tasks_json,
            profile_snapshot_json, created_at, updated_at, EXISTS(SELECT 1 FROM archived_exams a WHERE a.exam_id=exams.id)
            FROM exams ORDER BY date DESC, title ASC;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var isArchived = reader.GetInt32(11) != 0;
            if (isArchived && !includeArchived) continue;
            var id = reader.GetString(0);
            var classId = reader.GetString(1);
            var className = Unprotect(reader.GetString(2), dek, "exams", "class_name", id);
            var schulformId = reader.GetString(3);
            var schulformName = Unprotect(reader.GetString(4), dek, "exams", "schulform_name", id);
            var title = Unprotect(reader.GetString(5), dek, "exams", "title", id);
            var date = DateOnly.Parse(Unprotect(reader.GetString(6), dek, "exams", "date", id));
            var tasksJson = Unprotect(reader.GetString(7), dek, "exams", "tasks_json", id);
            var profJson = Unprotect(reader.GetString(8), dek, "exams", "profile_snapshot_json", id);
            var createdAt = DateTimeOffset.Parse(Unprotect(reader.GetString(9), dek, "exams", "created_at", id));
            var updatedAt = DateTimeOffset.Parse(Unprotect(reader.GetString(10), dek, "exams", "updated_at", id));

            var tasks = JsonSerializer.Deserialize<List<TaskDefinition>>(tasksJson) ?? new();
            var profile = JsonSerializer.Deserialize<GradingProfile>(profJson) ?? GradingProfile.CreateDefaultSchulnoten();

            list.Add(new Exam
            {
                Id = id,
                ClassId = classId,
                ClassName = className,
                SchulformId = schulformId,
                SchulformName = schulformName,
                Title = title,
                Date = date,
                Tasks = tasks,
                GradingProfileSnapshot = profile,
                IsArchived = isArchived,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            });
        }

        return list.OrderByDescending(e => e.Date).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Exam? GetExamById(string id)
    {
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, class_id, class_name, schulform_id, schulform_name, title, date, tasks_json,
            profile_snapshot_json, created_at, updated_at, EXISTS(SELECT 1 FROM archived_exams a WHERE a.exam_id=exams.id)
            FROM exams WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var classId = reader.GetString(1);
        var className = Unprotect(reader.GetString(2), dek, "exams", "class_name", id);
        var schulformId = reader.GetString(3);
        var schulformName = Unprotect(reader.GetString(4), dek, "exams", "schulform_name", id);
        var title = Unprotect(reader.GetString(5), dek, "exams", "title", id);
        var date = DateOnly.Parse(Unprotect(reader.GetString(6), dek, "exams", "date", id));
        var tasksJson = Unprotect(reader.GetString(7), dek, "exams", "tasks_json", id);
        var profJson = Unprotect(reader.GetString(8), dek, "exams", "profile_snapshot_json", id);
        var createdAt = DateTimeOffset.Parse(Unprotect(reader.GetString(9), dek, "exams", "created_at", id));
        var updatedAt = DateTimeOffset.Parse(Unprotect(reader.GetString(10), dek, "exams", "updated_at", id));
        var isArchived = reader.GetInt32(11) != 0;

        var tasks = JsonSerializer.Deserialize<List<TaskDefinition>>(tasksJson) ?? new();
        var profile = JsonSerializer.Deserialize<GradingProfile>(profJson) ?? GradingProfile.CreateDefaultSchulnoten();

        reader.Close();

        CoverSheet? coverSheet = null;
        using (var coverCmd = conn.CreateCommand())
        {
            coverCmd.CommandText = "SELECT document_enc FROM exam_cover_sheets WHERE exam_id=$id";
            coverCmd.Parameters.AddWithValue("$id", id);
            if (coverCmd.ExecuteScalar() is string encrypted)
                coverSheet = JsonSerializer.Deserialize<CoverSheet>(Unprotect(encrypted, dek, "exam_cover_sheets", "document_enc", id));
        }

        // Load student entries
        var entries = new List<ExamStudentEntry>();
        using var entriesCmd = conn.CreateCommand();
        entriesCmd.CommandText = "SELECT student_id, student_name_enc, status, task_points_enc, word_count, error_count, note_enc FROM exam_entries WHERE exam_id = $examId ORDER BY rowid;";
        entriesCmd.Parameters.AddWithValue("$examId", id);

        using var entriesReader = entriesCmd.ExecuteReader();
        while (entriesReader.Read())
        {
            var studentId = entriesReader.GetString(0);
            var nameEnc = entriesReader.GetString(1);
            var status = (StudentExamStatus)entriesReader.GetInt32(2);
            var ptsEnc = entriesReader.GetString(3);
            int? words = entriesReader.IsDBNull(4) ? null : entriesReader.GetInt32(4);
            int? errors = entriesReader.IsDBNull(5) ? null : entriesReader.GetInt32(5);
            var noteEnc = entriesReader.IsDBNull(6) ? null : entriesReader.GetString(6);

            var sName = Unprotect(nameEnc, dek, "exam_entries", "student_name_enc", id, studentId);
            var ptsJson = Unprotect(ptsEnc, dek, "exam_entries", "task_points_enc", id, studentId);
            var stored = JsonSerializer.Deserialize<StoredEntry>(ptsJson) ?? throw new InvalidOperationException("Ungültiger Klausureintrag.");
            var pts = stored.TaskPoints;
            words = stored.WordCount;
            errors = stored.ErrorCount;
            status = stored.Status;
            var note = noteEnc == null ? null : Unprotect(noteEnc, dek, "exam_entries", "note_enc", id, studentId);

            entries.Add(new ExamStudentEntry
            {
                StudentId = studentId,
                StudentName = sName,
                Status = status,
                TaskPoints = pts,
                TaskPointExpressions = stored.TaskPointExpressions ?? new(),
                WordCount = words,
                ErrorCount = errors,
                Note = note
            });
        }

        return new Exam
        {
            Id = id,
            ClassId = classId,
            ClassName = className,
            SchulformId = schulformId,
            SchulformName = schulformName,
            Title = title,
            Date = date,
            Tasks = tasks,
            GradingProfileSnapshot = profile,
            CoverSheet = coverSheet,
            IsArchived = isArchived,
            StudentEntries = entries,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void SaveExam(Exam exam)
    {
        exam.CoverSheet?.Validate(exam);
        var dek = _keyManager.GetActiveDek(_snapshotConnectionString == null);
        var tasksJson = Protect(JsonSerializer.Serialize(exam.Tasks), dek, "exams", "tasks_json", exam.Id);
        var profJson = Protect(JsonSerializer.Serialize(exam.GradingProfileSnapshot), dek, "exams", "profile_snapshot_json", exam.Id);

        using var conn = CreateConnection();
        // The archive check and write share an immediate transaction, so an
        // archive transition cannot slip between the check and the update.
        using var trans = conn.BeginTransaction(deferred: false);
        EnsureExamIsEditable(conn, trans, exam.Id);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = @"
INSERT INTO exams (id, class_id, class_name, schulform_id, schulform_name, title, date, tasks_json, profile_snapshot_json, created_at, updated_at)
VALUES ($id, $classId, $className, $schulformId, $schulformName, $title, $date, $tasksJson, $profJson, $createdAt, $updatedAt)
ON CONFLICT(id) DO UPDATE SET
    class_name = excluded.class_name,
    schulform_name = excluded.schulform_name,
    title = excluded.title,
    date = excluded.date,
    tasks_json = excluded.tasks_json,
    profile_snapshot_json = excluded.profile_snapshot_json,
    updated_at = excluded.updated_at;";

        cmd.Parameters.AddWithValue("$id", exam.Id);
        cmd.Parameters.AddWithValue("$classId", exam.ClassId);
        cmd.Parameters.AddWithValue("$className", Protect(exam.ClassName, dek, "exams", "class_name", exam.Id));
        cmd.Parameters.AddWithValue("$schulformId", exam.SchulformId);
        cmd.Parameters.AddWithValue("$schulformName", Protect(exam.SchulformName, dek, "exams", "schulform_name", exam.Id));
        cmd.Parameters.AddWithValue("$title", Protect(exam.Title, dek, "exams", "title", exam.Id));
        cmd.Parameters.AddWithValue("$date", Protect(exam.Date.ToString("O"), dek, "exams", "date", exam.Id));
        cmd.Parameters.AddWithValue("$tasksJson", tasksJson);
        cmd.Parameters.AddWithValue("$profJson", profJson);
        cmd.Parameters.AddWithValue("$createdAt", Protect(exam.CreatedAt.ToString("O"), dek, "exams", "created_at", exam.Id));
        cmd.Parameters.AddWithValue("$updatedAt", Protect(DateTimeOffset.UtcNow.ToString("O"), dek, "exams", "updated_at", exam.Id));

        cmd.ExecuteNonQuery();

        using (var coverCmd = conn.CreateCommand())
        {
            coverCmd.Transaction = trans;
            coverCmd.Parameters.AddWithValue("$id", exam.Id);
            if (exam.CoverSheet == null)
                coverCmd.CommandText = "DELETE FROM exam_cover_sheets WHERE exam_id=$id";
            else
            {
                coverCmd.CommandText = @"INSERT INTO exam_cover_sheets (exam_id,document_enc) VALUES ($id,$document)
                    ON CONFLICT(exam_id) DO UPDATE SET document_enc=excluded.document_enc";
                coverCmd.Parameters.AddWithValue("$document", Protect(JsonSerializer.Serialize(exam.CoverSheet), dek, "exam_cover_sheets", "document_enc", exam.Id));
            }
            coverCmd.ExecuteNonQuery();
        }

        // Save entries
        foreach (var entry in exam.StudentEntries)
        {
            var nameEnc = Protect(entry.StudentName, dek, "exam_entries", "student_name_enc", exam.Id, entry.StudentId);
            var ptsEnc = ProtectEntry(entry, dek, exam.Id);
            var noteEnc = string.IsNullOrEmpty(entry.Note) ? null : Protect(entry.Note, dek, "exam_entries", "note_enc", exam.Id, entry.StudentId);

            using var entryCmd = conn.CreateCommand();
            entryCmd.Transaction = trans;
            entryCmd.CommandText = @"
INSERT INTO exam_entries (exam_id, student_id, student_name_enc, status, task_points_enc, word_count, error_count, note_enc)
VALUES ($examId, $studentId, $nameEnc, $status, $ptsEnc, $words, $errors, $noteEnc)
ON CONFLICT(exam_id, student_id) DO UPDATE SET
    student_name_enc = excluded.student_name_enc,
    status = excluded.status,
    task_points_enc = excluded.task_points_enc,
    word_count = excluded.word_count,
    error_count = excluded.error_count,
    note_enc = excluded.note_enc;";

            entryCmd.Parameters.AddWithValue("$examId", exam.Id);
            entryCmd.Parameters.AddWithValue("$studentId", entry.StudentId);
            entryCmd.Parameters.AddWithValue("$nameEnc", nameEnc);
            entryCmd.Parameters.AddWithValue("$status", 0);
            entryCmd.Parameters.AddWithValue("$ptsEnc", ptsEnc);
            entryCmd.Parameters.AddWithValue("$words", DBNull.Value);
            entryCmd.Parameters.AddWithValue("$errors", DBNull.Value);
            entryCmd.Parameters.AddWithValue("$noteEnc", (object?)noteEnc ?? DBNull.Value);

            entryCmd.ExecuteNonQuery();
        }

        trans.Commit();
    }

    // Archive transitions modify only the archive marker, never the exam snapshot or entries.
    public bool SetExamArchived(string id, bool archived)
    {
        using var conn = CreateConnection();
        using var transaction = conn.BeginTransaction(deferred: false);
        using var check = conn.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT EXISTS(SELECT 1 FROM exams WHERE id=$id)";
        check.Parameters.AddWithValue("$id", id);
        if (Convert.ToInt32(check.ExecuteScalar()) == 0) return false;
        using var command = conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = archived
            ? "INSERT OR IGNORE INTO archived_exams (exam_id) VALUES ($id)"
            : "DELETE FROM archived_exams WHERE exam_id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public void DeleteExam(string id)
    {
        using var conn = CreateConnection();
        using var transaction = conn.BeginTransaction(deferred: false);
        EnsureExamIsEditable(conn, transaction, id);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM exams WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void EnsureExamIsEditable(SqliteConnection conn, SqliteTransaction transaction, string id)
    {
        using var command = conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM archived_exams WHERE exam_id=$id)";
        command.Parameters.AddWithValue("$id", id);
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) throw new ArchivedExamReadOnlyException();
    }

    #endregion
}
