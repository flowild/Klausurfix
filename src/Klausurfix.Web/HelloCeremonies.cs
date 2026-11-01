using System.Collections.Concurrent;
using System.Security.Cryptography;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Klausurfix.Infrastructure.Security;

/// <summary>Manages short-lived, single-use WebAuthn challenges on the local server.</summary>
public sealed class HelloCeremonies
{
    private readonly Fido2 _fido = new(new Fido2Configuration
    {
        RPID = "localhost",
        RPName = "Klausurfix",
        Origins = new HashSet<string> { "http://localhost:5000" },
        BackupEligibleCredentialPolicy = Fido2Configuration.CredentialBackupPolicy.Disallowed,
        BackedUpCredentialPolicy = Fido2Configuration.CredentialBackupPolicy.Disallowed
    });
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly KeyManager _keys;
    private sealed record Pending(DateTimeOffset Expires, string Kind, string Binding, object Options,
        byte[]? CredentialId = null, byte[]? PublicKey = null, byte[]? Salt = null, byte[]? UserId = null, uint Counter = 0);

    public HelloCeremonies(KeyManager keys) => _keys = keys;

    private string Put(Pending pending)
    {
        foreach (var entry in _pending)
            if (entry.Value.Expires <= DateTimeOffset.UtcNow) _pending.TryRemove(entry.Key, out _);
        if (_pending.Count >= 32) throw new InvalidOperationException("Zu viele offene Anmeldeversuche.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (!_pending.TryAdd(token, pending)) throw new InvalidOperationException("Anmeldeversuch konnte nicht erstellt werden.");
        return token;
    }

    private Pending Take(string token, string kind, string binding)
    {
        if (string.IsNullOrEmpty(token) || !_pending.TryRemove(token, out var pending) ||
            pending.Expires <= DateTimeOffset.UtcNow || pending.Kind != kind || pending.Binding != binding)
            throw new InvalidOperationException("Die Anmeldung ist abgelaufen. Bitte erneut versuchen.");
        return pending;
    }

    private static DateTimeOffset Deadline() => DateTimeOffset.UtcNow.AddMinutes(2);

    public (string Token, CredentialCreateOptions Options) BeginRegistration(string binding)
    {
        if (!_keys.IsUnlocked) throw new InvalidOperationException("Anwendung ist gesperrt.");
        var userId = RandomNumberGenerator.GetBytes(32);
        var options = _fido.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = userId, Name = "klausurfix", DisplayName = "Klausurfix" },
            AuthenticatorSelection = new AuthenticatorSelection
            {
                AuthenticatorAttachment = AuthenticatorAttachment.Platform,
                UserVerification = UserVerificationRequirement.Required,
                ResidentKey = ResidentKeyRequirement.Discouraged
            },
            AttestationPreference = AttestationConveyancePreference.None,
            Extensions = new AuthenticationExtensionsClientInputs { PRF = new AuthenticationExtensionsPRFInputs() }
        });
        var token = Put(new Pending(Deadline(), "register", binding, options, UserId: userId));
        return (token, options);
    }

    public async Task<(string Token, AssertionOptions Options, byte[] Salt)> CompleteRegistrationAsync(
        string token, string binding, AuthenticatorAttestationRawResponse response)
    {
        var pending = Take(token, "register", binding);
        if (!_keys.IsUnlocked) throw new InvalidOperationException("Anwendung ist gesperrt.");
        var credential = await _fido.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = (CredentialCreateOptions)pending.Options,
            IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true)
        });
        if (credential.IsBackupEligible || credential.IsBackedUp)
            throw new InvalidOperationException("Synchronisierte Passkeys werden nicht unterstützt.");
        var salt = RandomNumberGenerator.GetBytes(32);
        var options = CreateAssertion(credential.Id);
        var next = Put(new Pending(Deadline(), "confirm", binding, options, credential.Id,
            credential.PublicKey, salt, pending.UserId, credential.SignCount));
        return (next, options, salt);
    }

    public async Task ConfirmRegistrationAsync(string token, string binding, AuthenticatorAssertionRawResponse response, byte[] prf)
    {
        var pending = Take(token, "confirm", binding);
        if (!_keys.IsUnlocked) throw new InvalidOperationException("Anwendung ist gesperrt.");
        var result = await VerifyAsync(pending, response);
        _keys.SaveVerifiedHelloCredential(pending.CredentialId!, pending.PublicKey!, pending.Salt!, prf, result.SignCount, pending.UserId!);
    }

    public (string Token, AssertionOptions Options, byte[] Salt) BeginUnlock(string binding)
    {
        var credential = _keys.GetHelloCredential() ?? throw new InvalidOperationException("Geräteanmeldung ist nicht eingerichtet.");
        var id = Convert.FromBase64String(credential.CredentialId);
        var salt = Convert.FromBase64String(credential.PrfSalt);
        var options = CreateAssertion(id);
        var token = Put(new Pending(Deadline(), "unlock", binding, options, id,
            Convert.FromBase64String(credential.PublicKey), salt, Convert.FromBase64String(credential.UserId), credential.SignCount));
        return (token, options, salt);
    }

    public async Task CompleteUnlockAsync(string token, string binding, AuthenticatorAssertionRawResponse response, byte[] prf)
    {
        var pending = Take(token, "unlock", binding);
        var result = await VerifyAsync(pending, response);
        _keys.UnlockWithVerifiedHello(pending.CredentialId!, prf, result.SignCount);
    }

    private AssertionOptions CreateAssertion(byte[] id) => _fido.GetAssertionOptions(new GetAssertionOptionsParams
    {
        AllowedCredentials = new[] { new PublicKeyCredentialDescriptor(id) },
        UserVerification = UserVerificationRequirement.Required
    });

    private async Task<VerifyAssertionResult> VerifyAsync(Pending pending, AuthenticatorAssertionRawResponse response)
    {
        if (!CryptographicOperations.FixedTimeEquals(response.RawId, pending.CredentialId!))
            throw new CryptographicException("Ungültige Anmeldekennung.");
        return await _fido.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = response,
            OriginalOptions = (AssertionOptions)pending.Options,
            StoredPublicKey = pending.PublicKey!,
            StoredSignatureCounter = pending.Counter,
            IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                pending.UserId is not null && CryptographicOperations.FixedTimeEquals(args.UserHandle, pending.UserId) &&
                CryptographicOperations.FixedTimeEquals(args.CredentialId, pending.CredentialId!))
        });
    }
}
