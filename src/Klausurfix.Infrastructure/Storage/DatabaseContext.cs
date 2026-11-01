namespace Klausurfix.Infrastructure.Storage;

using System.Text.Json;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Security;
using Microsoft.Data.Sqlite;

public class DatabaseContext
{
    private readonly KeyManager _keyManager;

    public DatabaseContext(KeyManager keyManager)
    {
        _keyManager = keyManager;
    }

    private SqliteConnection CreateConnection()
    {
        AppPaths.EnsureDirectoriesExist();
        var connStr = new SqliteConnectionStringBuilder
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
    }

    #region Schulformen

    public List<Schulform> GetSchulformen(bool includeArchived = false)
    {
        var dek = _keyManager.GetActiveDek();
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
            var name = reader.GetString(1);
            var profEnc = reader.GetString(2);
            var isArchived = reader.GetInt32(3) == 1;
            var createdAt = DateTimeOffset.Parse(reader.GetString(4));

            var profJson = CryptoService.DecryptString(profEnc, dek);
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

        return list;
    }

    public Schulform? GetSchulformById(string id)
    {
        var dek = _keyManager.GetActiveDek();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, profile_json_enc, is_archived, created_at FROM schulformen WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var name = reader.GetString(1);
        var profEnc = reader.GetString(2);
        var isArchived = reader.GetInt32(3) == 1;
        var createdAt = DateTimeOffset.Parse(reader.GetString(4));

        var profJson = CryptoService.DecryptString(profEnc, dek);
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
        var dek = _keyManager.GetActiveDek();
        var profJson = JsonSerializer.Serialize(schulform.Profile);
        var profEnc = CryptoService.EncryptString(profJson, dek);

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
        cmd.Parameters.AddWithValue("$name", schulform.Name);
        cmd.Parameters.AddWithValue("$profEnc", profEnc);
        cmd.Parameters.AddWithValue("$isArchived", schulform.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", schulform.CreatedAt.ToString("O"));

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
                Name = reader.GetString(1),
                SchulformId = reader.GetString(2),
                SchulformName = reader.GetString(3),
                IsArchived = reader.GetInt32(4) == 1,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(5))
            });
        }

        return list;
    }

    public ClassGroup? GetClassById(string id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, schulform_id, schulform_name, is_archived, created_at FROM classes WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new ClassGroup
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            SchulformId = reader.GetString(2),
            SchulformName = reader.GetString(3),
            IsArchived = reader.GetInt32(4) == 1,
            CreatedAt = DateTimeOffset.Parse(reader.GetString(5))
        };
    }

    public void SaveClass(ClassGroup classGroup)
    {
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
        cmd.Parameters.AddWithValue("$name", classGroup.Name);
        cmd.Parameters.AddWithValue("$schulformId", classGroup.SchulformId);
        cmd.Parameters.AddWithValue("$schulformName", classGroup.SchulformName);
        cmd.Parameters.AddWithValue("$isArchived", classGroup.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", classGroup.CreatedAt.ToString("O"));

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
        var dek = _keyManager.GetActiveDek();
        var list = new List<Student>();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT id, class_id, first_name_enc, last_name_enc, order_index, is_archived FROM students WHERE class_id = $classId ORDER BY order_index ASC;"
            : "SELECT id, class_id, first_name_enc, last_name_enc, order_index, is_archived FROM students WHERE class_id = $classId AND is_archived = 0 ORDER BY order_index ASC;";
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

            var fn = CryptoService.DecryptString(fnEnc, dek);
            var ln = CryptoService.DecryptString(lnEnc, dek);

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

    public void SaveStudent(Student student)
    {
        var dek = _keyManager.GetActiveDek();
        var fnEnc = CryptoService.EncryptString(student.FirstName, dek);
        var lnEnc = CryptoService.EncryptString(student.LastName, dek);

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

    public List<Exam> GetExams()
    {
        var list = new List<Exam>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, class_id, class_name, schulform_id, schulform_name, title, date, tasks_json, profile_snapshot_json, created_at, updated_at FROM exams ORDER BY date DESC, title ASC;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var classId = reader.GetString(1);
            var className = reader.GetString(2);
            var schulformId = reader.GetString(3);
            var schulformName = reader.GetString(4);
            var title = reader.GetString(5);
            var date = DateOnly.Parse(reader.GetString(6));
            var tasksJson = reader.GetString(7);
            var profJson = reader.GetString(8);
            var createdAt = DateTimeOffset.Parse(reader.GetString(9));
            var updatedAt = DateTimeOffset.Parse(reader.GetString(10));

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
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            });
        }

        return list;
    }

    public Exam? GetExamById(string id)
    {
        var dek = _keyManager.GetActiveDek();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, class_id, class_name, schulform_id, schulform_name, title, date, tasks_json, profile_snapshot_json, created_at, updated_at FROM exams WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var classId = reader.GetString(1);
        var className = reader.GetString(2);
        var schulformId = reader.GetString(3);
        var schulformName = reader.GetString(4);
        var title = reader.GetString(5);
        var date = DateOnly.Parse(reader.GetString(6));
        var tasksJson = reader.GetString(7);
        var profJson = reader.GetString(8);
        var createdAt = DateTimeOffset.Parse(reader.GetString(9));
        var updatedAt = DateTimeOffset.Parse(reader.GetString(10));

        var tasks = JsonSerializer.Deserialize<List<TaskDefinition>>(tasksJson) ?? new();
        var profile = JsonSerializer.Deserialize<GradingProfile>(profJson) ?? GradingProfile.CreateDefaultSchulnoten();

        reader.Close();

        // Load student entries
        var entries = new List<ExamStudentEntry>();
        using var entriesCmd = conn.CreateCommand();
        entriesCmd.CommandText = "SELECT student_id, student_name_enc, status, task_points_enc, word_count, error_count, note_enc FROM exam_entries WHERE exam_id = $examId;";
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

            var sName = CryptoService.DecryptString(nameEnc, dek);
            var ptsJson = CryptoService.DecryptString(ptsEnc, dek);
            var pts = JsonSerializer.Deserialize<Dictionary<int, decimal?>>(ptsJson) ?? new();
            var note = string.IsNullOrEmpty(noteEnc) ? null : CryptoService.DecryptString(noteEnc, dek);

            entries.Add(new ExamStudentEntry
            {
                StudentId = studentId,
                StudentName = sName,
                Status = status,
                TaskPoints = pts,
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
            StudentEntries = entries,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void SaveExam(Exam exam)
    {
        var dek = _keyManager.GetActiveDek();
        var tasksJson = JsonSerializer.Serialize(exam.Tasks);
        var profJson = JsonSerializer.Serialize(exam.GradingProfileSnapshot);

        using var conn = CreateConnection();
        using var trans = conn.BeginTransaction();

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
        cmd.Parameters.AddWithValue("$className", exam.ClassName);
        cmd.Parameters.AddWithValue("$schulformId", exam.SchulformId);
        cmd.Parameters.AddWithValue("$schulformName", exam.SchulformName);
        cmd.Parameters.AddWithValue("$title", exam.Title);
        cmd.Parameters.AddWithValue("$date", exam.Date.ToString("O"));
        cmd.Parameters.AddWithValue("$tasksJson", tasksJson);
        cmd.Parameters.AddWithValue("$profJson", profJson);
        cmd.Parameters.AddWithValue("$createdAt", exam.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();

        // Save entries
        foreach (var entry in exam.StudentEntries)
        {
            var nameEnc = CryptoService.EncryptString(entry.StudentName, dek);
            var ptsJson = JsonSerializer.Serialize(entry.TaskPoints);
            var ptsEnc = CryptoService.EncryptString(ptsJson, dek);
            var noteEnc = string.IsNullOrEmpty(entry.Note) ? null : CryptoService.EncryptString(entry.Note, dek);

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
            entryCmd.Parameters.AddWithValue("$status", (int)entry.Status);
            entryCmd.Parameters.AddWithValue("$ptsEnc", ptsEnc);
            entryCmd.Parameters.AddWithValue("$words", (object?)entry.WordCount ?? DBNull.Value);
            entryCmd.Parameters.AddWithValue("$errors", (object?)entry.ErrorCount ?? DBNull.Value);
            entryCmd.Parameters.AddWithValue("$noteEnc", (object?)noteEnc ?? DBNull.Value);

            entryCmd.ExecuteNonQuery();
        }

        trans.Commit();
    }

    public void DeleteExam(string id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM exams WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    #endregion
}
