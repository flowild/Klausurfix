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
    public bool PinBoundToWindows { get; set; }
    public string EncryptedDekWithRecovery { get; set; } = string.Empty;
    public string RecoveryKeyHint { get; set; } = string.Empty; // e.g. first/last 4 chars
    public string? EncryptedDekWithDpapi { get; set; } = null;
    public string? HelloCredentialId { get; set; } = null;
    public string? HelloPublicKey { get; set; } = null;
    public uint HelloSignCount { get; set; } = 0;
    public HelloKeyEnvelope? HelloKey { get; set; }
    public int InactivityTimeoutMinutes { get; set; } = 15;
    public bool AutomaticBackupEnabled { get; set; } = true;
    public int AutomaticBackupIntervalHours { get; set; } = 24;
    public int FailedPinAttempts { get; set; } = 0;
    public DateTimeOffset? LockedUntilUtc { get; set; } = null;
}

public sealed class HelloKeyEnvelope
{
    public string CredentialId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string PrfSalt { get; set; } = string.Empty;
    public string ProtectedDek { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public uint SignCount { get; set; }
}

public class KeyManager
{
    private readonly ILocalKeyProtector _localProtector;
    public KeyManager(ILocalKeyProtector? localProtector = null) =>
        _localProtector = localProtector ?? new UserKeyProtector();
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
                return env?.HelloKey is not null;
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

    public (bool Enabled, int IntervalHours) GetAutomaticBackupSettings()
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Anwendung ist noch nicht eingerichtet.");
            return (env.AutomaticBackupEnabled, env.AutomaticBackupIntervalHours);
        }
    }

    public void SetAutomaticBackupSettings(bool enabled, int intervalHours)
    {
        if (intervalHours is not (6 or 12 or 24 or 168)) throw new ArgumentOutOfRangeException(nameof(intervalHours));
        lock (_lock)
        {
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Anwendung ist noch nicht eingerichtet.");
            env.AutomaticBackupEnabled = enabled;
            env.AutomaticBackupIntervalHours = intervalHours;
            SaveEnvelopeInternal(env);
        }
    }

    public byte[] GetActiveDek(bool recordActivity = true)
    {
        lock (_lock)
        {
            CheckInactivityTimeout();
            if (_activeDek == null)
            {
                throw new InvalidOperationException("Anwendung ist gesperrt. Bitte zuerst mit PIN oder Windows Hello entsperren.");
            }
            if (recordActivity) RecordActivity();
            return (byte[])_activeDek.Clone();
        }
    }

    public (byte[] Dek, string RecoverySalt, string RecoveryWrappedDek) GetBackupKeyMaterial()
    {
        lock (_lock)
        {
            CheckInactivityTimeout();
            if (_activeDek == null) throw new InvalidOperationException("Anwendung ist gesperrt.");
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Schlüsselhülle fehlt.");
            return ((byte[])_activeDek.Clone(), env.MasterSalt, env.EncryptedDekWithRecovery);
        }
    }

    public static string GenerateRecoveryKey()
    {
        // 16 cryptographically random bytes formatted as 4 groups of 4 chars
        var bytes = CryptoService.GenerateRandomBytes(16);
        var base32 = Convert.ToHexString(bytes).ToUpperInvariant();
        // Format as KF-XXXX-XXXX-XXXX-XXXX
        return "KF-" + string.Join('-', Enumerable.Range(0, 8).Select(i => base32.Substring(i * 4, 4)));
    }

    public void Setup(string pin, string recoveryKey, int inactivityTimeoutMinutes = 15)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(pin) || pin.Length is < 6 or > 64 || pin.Any(c => c is < '0' or > '9'))
                throw new ArgumentException("PIN muss aus 6 bis 64 Ziffern bestehen.", nameof(pin));
            if (string.IsNullOrWhiteSpace(recoveryKey))
                throw new ArgumentException("Wiederherstellungsschlüssel ist erforderlich.", nameof(recoveryKey));

            AppPaths.EnsureDirectoriesExist();

            var masterSalt = CryptoService.GenerateRandomBytes(CryptoService.SaltSize);
            var dek = CryptoService.GenerateRandomBytes(CryptoService.KeySize);

            // Encrypt DEK with PIN
            var kekPin = CryptoService.DeriveKey(pin, masterSalt);
            var encryptedDekPin = _localProtector.Protect(CryptoService.Encrypt(dek, kekPin));
            CryptoService.ZeroMemory(kekPin);

            // Encrypt DEK with Recovery Key
            var kekRec = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), masterSalt);
            var encryptedDekRec = CryptoService.Encrypt(dek, kekRec);
            CryptoService.ZeroMemory(kekRec);

            var env = new KeyEnvelope
            {
                Version = 1,
                MasterSalt = Convert.ToBase64String(masterSalt),
                EncryptedDekWithPin = Convert.ToBase64String(encryptedDekPin),
                PinBoundToWindows = true,
                EncryptedDekWithRecovery = Convert.ToBase64String(encryptedDekRec),
                RecoveryKeyHint = recoveryKey.Length > 8 ? $"{recoveryKey.Substring(0, 4)}...{recoveryKey.Substring(recoveryKey.Length - 4)}" : "****",
                EncryptedDekWithDpapi = null,
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
                if (env.PinBoundToWindows) encryptedDekPin = _localProtector.Unprotect(encryptedDekPin);
                var dek = CryptoService.Decrypt(encryptedDekPin, kekPin);

                // Upgrade legacy envelopes only after the existing PIN is verified.
                if (!env.PinBoundToWindows)
                {
                    env.EncryptedDekWithPin = Convert.ToBase64String(_localProtector.Protect(encryptedDekPin));
                    env.PinBoundToWindows = true;
                }
                env.EncryptedDekWithDpapi = null;
                env.HelloCredentialId = null;
                env.HelloPublicKey = null;

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
                // Remove the former DPAPI-only shortcut after recovery as well.
                env.EncryptedDekWithDpapi = null;
                env.HelloCredentialId = null;
                env.HelloPublicKey = null;
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

    public bool VerifyRecoveryKey(string recoveryKey)
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal();
            if (env == null || _activeDek == null) return false;
            var key = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), Convert.FromBase64String(env.MasterSalt));
            try
            {
                var candidate = CryptoService.Decrypt(Convert.FromBase64String(env.EncryptedDekWithRecovery), key);
                try { return CryptographicOperations.FixedTimeEquals(candidate, _activeDek); }
                finally { CryptoService.ZeroMemory(candidate); }
            }
            catch (CryptographicException) { return false; }
            finally { CryptoService.ZeroMemory(key); }
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
            env.HelloKey = null;
            SaveEnvelopeInternal(env);
        }
    }

    /// <summary>Store a credential only after its WebAuthn registration has been verified.</summary>
    public void SaveVerifiedHelloCredential(byte[] credentialId, byte[] publicKey, byte[] prfSalt, byte[] prfResult, uint signCount, byte[] userId)
    {
        lock (_lock)
        {
            CheckInactivityTimeout();
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Nicht eingerichtet.");
            if (_activeDek == null) throw new InvalidOperationException("Anwendung ist gesperrt.");
            if (credentialId.Length == 0 || publicKey.Length == 0 || prfSalt.Length != 32 || userId.Length == 0)
                throw new ArgumentException("Ungültige Hello-Anmeldedaten.");
            var protectedDek = HelloKeyProtector.Protect(_activeDek, prfResult, credentialId);
            env.HelloKey = new HelloKeyEnvelope
            {
                CredentialId = Convert.ToBase64String(credentialId),
                PublicKey = Convert.ToBase64String(publicKey),
                PrfSalt = Convert.ToBase64String(prfSalt),
                ProtectedDek = Convert.ToBase64String(protectedDek),
                UserId = Convert.ToBase64String(userId),
                SignCount = signCount
            };
            SaveEnvelopeInternal(env);
        }
    }

    /// <summary>Returns a copy of the public credential data needed to verify an assertion.</summary>
    public HelloKeyEnvelope? GetHelloCredential()
    {
        lock (_lock)
        {
            var hello = GetEnvelopeInternal()?.HelloKey;
            return hello is null ? null : new HelloKeyEnvelope
            {
                CredentialId = hello.CredentialId, PublicKey = hello.PublicKey,
                PrfSalt = hello.PrfSalt, ProtectedDek = hello.ProtectedDek, SignCount = hello.SignCount, UserId = hello.UserId
            };
        }
    }

    /// <summary>Only call after verifying the challenge, signature, origin and user verification.</summary>
    public void UnlockWithVerifiedHello(byte[] credentialId, byte[] prfResult, uint signCount)
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Nicht eingerichtet.");
            var hello = env.HelloKey ?? throw new InvalidOperationException("Windows Hello ist nicht eingerichtet.");
            var expectedId = Convert.FromBase64String(hello.CredentialId);
            if (!CryptographicOperations.FixedTimeEquals(credentialId, expectedId))
                throw new CryptographicException("Ungültige Anmeldekennung.");
            if (hello.SignCount != 0 && signCount <= hello.SignCount)
                throw new CryptographicException("Ungültiger Signaturzähler.");
            var dek = HelloKeyProtector.Unprotect(Convert.FromBase64String(hello.ProtectedDek), prfResult, credentialId);
            try
            {
                hello.SignCount = signCount;
                SaveEnvelopeInternal(env);
                if (_activeDek != null) CryptoService.ZeroMemory(_activeDek);
                _activeDek = dek;
                _lastActivityUtc = DateTimeOffset.UtcNow;
            }
            catch { CryptoService.ZeroMemory(dek); throw; }
        }
    }

    public void ChangePin(string oldPin, string newPin)
    {
        lock (_lock)
        {
            var env = GetEnvelopeInternal() ?? throw new InvalidOperationException("Nicht eingerichtet.");
            if (string.IsNullOrWhiteSpace(newPin) || newPin.Length is < 6 or > 64 || newPin.Any(c => c is < '0' or > '9'))
                throw new ArgumentException("Die neue PIN muss aus 6 bis 64 Ziffern bestehen.", nameof(newPin));

            var verification = UnlockWithPin(oldPin);
            if (!verification.Success) throw new UnauthorizedAccessException(verification.Message);

            // Verify old PIN
            var masterSalt = Convert.FromBase64String(env.MasterSalt);
            var kekOld = CryptoService.DeriveKey(oldPin, masterSalt);
            byte[] dek;
            try
            {
                var encOld = Convert.FromBase64String(env.EncryptedDekWithPin);
                if (env.PinBoundToWindows) encOld = _localProtector.Unprotect(encOld);
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
            var encNew = _localProtector.Protect(CryptoService.Encrypt(dek, kekNew));
            CryptoService.ZeroMemory(kekNew);

            env.EncryptedDekWithPin = Convert.ToBase64String(encNew);
            env.PinBoundToWindows = true;
            env.EncryptedDekWithDpapi = null;
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
        var temporary = AppPaths.KeyEnvelopeFile + ".tmp";
        File.WriteAllText(temporary, json, Encoding.UTF8);
        File.Move(temporary, AppPaths.KeyEnvelopeFile, true);
        _envelope = envelope;
    }
}
