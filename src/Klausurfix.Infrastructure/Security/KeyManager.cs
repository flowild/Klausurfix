namespace Klausurfix.Infrastructure.Security;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Klausurfix.Infrastructure.Storage;

public class KeyEnvelope
{
    public int Version { get; set; } = 1;
    public string MasterSalt { get; set; } = string.Empty;
    public string EncryptedDekWithPin { get; set; } = string.Empty;
    public string EncryptedDekWithRecovery { get; set; } = string.Empty;
    public string RecoveryKeyHint { get; set; } = string.Empty; // e.g. first/last 4 chars
    public string? EncryptedDekWithDpapi { get; set; } = null;
    public string? HelloCredentialId { get; set; } = null;
    public string? HelloPublicKey { get; set; } = null;
    public uint HelloSignCount { get; set; } = 0;
    public int InactivityTimeoutMinutes { get; set; } = 15;
    public int FailedPinAttempts { get; set; } = 0;
    public DateTimeOffset? LockedUntilUtc { get; set; } = null;
}

public class KeyManager
{
    private readonly object _lock = new();
    private byte[]? _activeDek;
    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;
    private KeyEnvelope? _envelope;

    public bool IsInitialized
    {
        get
        {
            lock (_lock)
            {
                return File.Exists(AppPaths.KeyEnvelopeFile);
            }
        }
    }

    public bool IsUnlocked
    {
        get
        {
            lock (_lock)
            {
                if (_activeDek == null) return false;
                CheckInactivityTimeout();
                return _activeDek != null;
            }
        }
    }

    public bool IsHelloRegistered
    {
        get
        {
            lock (_lock)
            {
                var env = GetEnvelopeInternal();
                return env != null && !string.IsNullOrEmpty(env.HelloCredentialId);
            }
        }
    }

    public int InactivityTimeoutMinutes
    {
        get
        {
            lock (_lock)
            {
                var env = GetEnvelopeInternal();
                return env?.InactivityTimeoutMinutes ?? 15;
            }
        }
        set
        {
            lock (_lock)
            {
                var env = GetEnvelopeInternal();
                if (env != null)
                {
                    env.InactivityTimeoutMinutes = value;
                    SaveEnvelopeInternal(env);
                }
            }
        }
    }

    public void RecordActivity()
    {
        lock (_lock)
        {
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public byte[] GetActiveDek()
    {
        lock (_lock)
        {
            CheckInactivityTimeout();
            if (_activeDek == null)
            {
                throw new InvalidOperationException("Anwendung ist gesperrt. Bitte zuerst mit PIN oder Windows Hello entsperren.");
            }
            RecordActivity();
            return _activeDek;
        }
    }

    public static string GenerateRecoveryKey()
    {
        // 16 cryptographically random bytes formatted as 4 groups of 4 chars
        var bytes = CryptoService.GenerateRandomBytes(15);
        var base32 = Convert.ToHexString(bytes).ToUpperInvariant();
        // Format as KF-XXXX-XXXX-XXXX-XXXX
        return $"KF-{base32.Substring(0, 4)}-{base32.Substring(4, 4)}-{base32.Substring(8, 4)}-{base32.Substring(12, 4)}";
    }

    public void Setup(string pin, string recoveryKey, int inactivityTimeoutMinutes = 15)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(pin) || pin.Length < 6)
                throw new ArgumentException("PIN muss mindestens 6 Stellen lang sein.", nameof(pin));
            if (string.IsNullOrWhiteSpace(recoveryKey))
                throw new ArgumentException("Wiederherstellungsschlüssel ist erforderlich.", nameof(recoveryKey));

            AppPaths.EnsureDirectoriesExist();

            var masterSalt = CryptoService.GenerateRandomBytes(CryptoService.SaltSize);
            var dek = CryptoService.GenerateRandomBytes(CryptoService.KeySize);

            // Encrypt DEK with PIN
            var kekPin = CryptoService.DeriveKey(pin, masterSalt);
            var encryptedDekPin = CryptoService.Encrypt(dek, kekPin);
            CryptoService.ZeroMemory(kekPin);

            // Encrypt DEK with Recovery Key
            var kekRec = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), masterSalt);
            var encryptedDekRec = CryptoService.Encrypt(dek, kekRec);
            CryptoService.ZeroMemory(kekRec);

            // Protect DEK with DPAPI if on Windows
            string? dpapiEnc = null;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var dpapiBytes = WindowsDpapiProtector.Protect(dek);
                    dpapiEnc = Convert.ToBase64String(dpapiBytes);
                }
                catch
                {
                    dpapiEnc = null;
                }
            }

            var env = new KeyEnvelope
            {
                Version = 1,
                MasterSalt = Convert.ToBase64String(masterSalt),
                EncryptedDekWithPin = Convert.ToBase64String(encryptedDekPin),
                EncryptedDekWithRecovery = Convert.ToBase64String(encryptedDekRec),
                RecoveryKeyHint = recoveryKey.Length > 8 ? $"{recoveryKey.Substring(0, 4)}...{recoveryKey.Substring(recoveryKey.Length - 4)}" : "****",
                EncryptedDekWithDpapi = dpapiEnc,
                InactivityTimeoutMinutes = inactivityTimeoutMinutes,
                FailedPinAttempts = 0,
                LockedUntilUtc = null
            };

            SaveEnvelopeInternal(env);

            _activeDek = dek;
            _envelope = env;
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public (bool Success, string Message, int? LockoutSeconds) UnlockWithPin(string pin)
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal();
            if (env == null)
                return (false, "Die Anwendung ist noch nicht eingerichtet.", null);

            // Check lockout timer
            if (env.LockedUntilUtc.HasValue && env.LockedUntilUtc.Value > DateTimeOffset.UtcNow)
            {
                var remaining = (int)Math.Ceiling((env.LockedUntilUtc.Value - DateTimeOffset.UtcNow).TotalSeconds);
                return (false, $"Zu viele Fehlversuche. Bitte warten Sie noch {remaining} Sekunden.", remaining);
            }

            var masterSalt = Convert.FromBase64String(env.MasterSalt);
            var kekPin = CryptoService.DeriveKey(pin, masterSalt);

            try
            {
                var encryptedDekPin = Convert.FromBase64String(env.EncryptedDekWithPin);
                var dek = CryptoService.Decrypt(encryptedDekPin, kekPin);

                // Success!
                env.FailedPinAttempts = 0;
                env.LockedUntilUtc = null;
                SaveEnvelopeInternal(env);

                if (_activeDek != null)
                {
                    CryptoService.ZeroMemory(_activeDek);
                }
                _activeDek = dek;
                _lastActivityUtc = DateTimeOffset.UtcNow;

                return (true, "Erfolgreich entsperrt.", null);
            }
            catch (CryptographicException)
            {
                // Failed attempt
                env.FailedPinAttempts++;
                int delaySeconds = 0;
                if (env.FailedPinAttempts >= 5)
                    delaySeconds = 30;
                else if (env.FailedPinAttempts == 4)
                    delaySeconds = 10;
                else if (env.FailedPinAttempts == 3)
                    delaySeconds = 3;

                if (delaySeconds > 0)
                {
                    env.LockedUntilUtc = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
                }
                SaveEnvelopeInternal(env);

                string msg = delaySeconds > 0
                    ? $"Falsche PIN. Zugriff gesperrt für {delaySeconds} Sekunden."
                    : $"Falsche PIN. Noch {3 - env.FailedPinAttempts} reguläre Versuche verbleibend.";

                return (false, msg, delaySeconds > 0 ? delaySeconds : null);
            }
            finally
            {
                CryptoService.ZeroMemory(kekPin);
            }
        }
    }

    public (bool Success, string Message) UnlockWithRecoveryKey(string recoveryKey)
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal();
            if (env == null)
                return (false, "Die Anwendung ist noch nicht eingerichtet.");

            var masterSalt = Convert.FromBase64String(env.MasterSalt);
            var kekRec = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), masterSalt);

            try
            {
                var encryptedDekRec = Convert.FromBase64String(env.EncryptedDekWithRecovery);
                var dek = CryptoService.Decrypt(encryptedDekRec, kekRec);

                env.FailedPinAttempts = 0;
                env.LockedUntilUtc = null;
                SaveEnvelopeInternal(env);

                if (_activeDek != null)
                {
                    CryptoService.ZeroMemory(_activeDek);
                }
                _activeDek = dek;
                _lastActivityUtc = DateTimeOffset.UtcNow;

                return (true, "Erfolgreich mit Wiederherstellungsschlüssel entsperrt.");
            }
            catch (CryptographicException)
            {
                return (false, "Ungültiger Wiederherstellungsschlüssel.");
            }
            finally
            {
                CryptoService.ZeroMemory(kekRec);
            }
        }
    }

    public (bool Success, string Message) UnlockWithDpapiSession()
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal();
            if (env == null)
                return (false, "Die Anwendung ist noch nicht eingerichtet.");

            if (string.IsNullOrEmpty(env.EncryptedDekWithDpapi) || !OperatingSystem.IsWindows())
                return (false, "Windows Hello / DPAPI ist für dieses Profil nicht eingerichtet.");

            try
            {
                var dpapiBytes = Convert.FromBase64String(env.EncryptedDekWithDpapi);
                var dek = WindowsDpapiProtector.Unprotect(dpapiBytes);

                if (_activeDek != null)
                {
                    CryptoService.ZeroMemory(_activeDek);
                }
                _activeDek = dek;
                _lastActivityUtc = DateTimeOffset.UtcNow;

                return (true, "Erfolgreich mit Windows Hello entsperrt.");
            }
            catch (Exception ex)
            {
                return (false, $"Windows Hello Entsperrung fehlgeschlagen: {ex.Message}");
            }
        }
    }

    public void RegisterHello(string credentialId, string publicKey)
    {
        lock (_lock)
        {
            var dek = GetActiveDek();
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Nicht eingerichtet.");

            env.HelloCredentialId = credentialId;
            env.HelloPublicKey = publicKey;
            env.HelloSignCount = 0;

            if (OperatingSystem.IsWindows())
            {
                var dpapiBytes = WindowsDpapiProtector.Protect(dek);
                env.EncryptedDekWithDpapi = Convert.ToBase64String(dpapiBytes);
            }

            SaveEnvelopeInternal(env);
        }
    }

    public void RemoveHello()
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Nicht eingerichtet.");
            env.HelloCredentialId = null;
            env.HelloPublicKey = null;
            env.EncryptedDekWithDpapi = null;
            SaveEnvelopeInternal(env);
        }
    }

    public void ChangePin(string oldPin, string newPin)
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Nicht eingerichtet.");
            if (string.IsNullOrWhiteSpace(newPin) || newPin.Length < 6)
                throw new ArgumentException("Neue PIN muss mindestens 6 Stellen lang sein.", nameof(newPin));

            // Verify old PIN
            var masterSalt = Convert.FromBase64String(env.MasterSalt);
            var kekOld = CryptoService.DeriveKey(oldPin, masterSalt);
            byte[] dek;
            try
            {
                var encOld = Convert.FromBase64String(env.EncryptedDekWithPin);
                dek = CryptoService.Decrypt(encOld, kekOld);
            }
            catch (CryptographicException)
            {
                throw new UnauthorizedAccessException("Die aktuelle PIN ist falsch.");
            }
            finally
            {
                CryptoService.ZeroMemory(kekOld);
            }

            // Encrypt DEK with new PIN
            var kekNew = CryptoService.DeriveKey(newPin, masterSalt);
            var encNew = CryptoService.Encrypt(dek, kekNew);
            CryptoService.ZeroMemory(kekNew);

            env.EncryptedDekWithPin = Convert.ToBase64String(encNew);
            env.FailedPinAttempts = 0;
            env.LockedUntilUtc = null;
            SaveEnvelopeInternal(env);

            _activeDek = dek;
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void Lock()
    {
        lock (_lock)
        {
            if (_activeDek != null)
            {
                CryptoService.ZeroMemory(_activeDek);
                _activeDek = null;
            }
        }
    }

    private void CheckInactivityTimeout()
    {
        if (_activeDek == null) return;
        var env = GetEnvelopeInternal();
        int timeoutMinutes = env?.InactivityTimeoutMinutes ?? 15;
        if (timeoutMinutes > 0 && DateTimeOffset.UtcNow - _lastActivityUtc > TimeSpan.FromMinutes(timeoutMinutes))
        {
            Lock();
        }
    }

    private KeyEnvelope? GetEnvelopeInternal()
    {
        if (_envelope != null) return _envelope;
        if (!File.Exists(AppPaths.KeyEnvelopeFile)) return null;

        var json = File.ReadAllText(AppPaths.KeyEnvelopeFile, Encoding.UTF8);
        _envelope = JsonSerializer.Deserialize<KeyEnvelope>(json);
        return _envelope;
    }

    private void SaveEnvelopeInternal(KeyEnvelope envelope)
    {
        AppPaths.EnsureDirectoriesExist();
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.KeyEnvelopeFile, json, Encoding.UTF8);
        _envelope = envelope;
    }
}
