using System.Security.Cryptography;

/// <summary>
/// In-memory coordination primitive. An update cannot use a successful round as
/// an installation permit until registered views are prevented from editing.
/// </summary>
public sealed class UpdateViewRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _views = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private static readonly TimeSpan ViewTimeout = TimeSpan.FromSeconds(10);
    private Round? _round;

    public UpdateViewRegistry() : this(TimeProvider.System) { }
    public UpdateViewRegistry(TimeProvider clock) => _clock = clock;

    private bool IsStale(long lastSeen) => _clock.GetElapsedTime(lastSeen) >= ViewTimeout;

    public string Register()
    {
        lock (_sync)
        {
            if (_views.Count >= 256) throw new InvalidOperationException("Zu viele Ansichten registriert.");
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _views.Add(id, _clock.GetTimestamp());
            if (_round is not null) _round.Invalidated = true;
            return id;
        }
    }

    public bool Contains(string id)
    {
        lock (_sync) return _views.ContainsKey(id);
    }

    public void Unregister(string id)
    {
        lock (_sync)
        {
            if (_views.Remove(id) && _round is not null) _round.Invalidated = true;
        }
    }

    public string Begin()
    {
        lock (_sync)
        {
            if (_round is { } existing && existing.Expired) _round = null;
            if (_round is not null) throw new InvalidOperationException("Eine Speicherabstimmung läuft bereits.");
            if (_views.Count == 0) throw new InvalidOperationException("Keine Ansicht für die Speicherabstimmung registriert.");
            if (_views.Values.Any(IsStale)) throw new InvalidOperationException("Eine registrierte Ansicht antwortet nicht. Bitte Klausurfix in dieser Ansicht prüfen oder schließen.");
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _round = new Round(id, new HashSet<string>(_views.Keys, StringComparer.Ordinal), _clock);
            return id;
        }
    }

    public string? PendingFor(string view)
    {
        lock (_sync)
        {
            if (!_views.TryGetValue(view, out var lastSeen)) return null;
            if (IsStale(lastSeen) && _round is not null) _round.Invalidated = true;
            _views[view] = _clock.GetTimestamp();
            return _round is { Invalidated: false, Expired: false } round && round.Targets.Contains(view) ? round.Id : null;
        }
    }

    public bool Respond(string view, string roundId, bool saved)
    {
        lock (_sync)
        {
            if (_round is not { Invalidated: false, Expired: false } round || round.Id != roundId || !round.Targets.Contains(view)) return false;
            return round.Responses.TryAdd(view, saved);
        }
    }

    public SaveRoundStatus Status(string roundId)
    {
        lock (_sync)
        {
            if (_round is not { } round || round.Id != roundId) throw new InvalidOperationException("Speicherabstimmung nicht gefunden.");
            var failed = round.Invalidated || round.Expired || round.Responses.Values.Any(saved => !saved) ||
                round.Targets.Any(view => !_views.TryGetValue(view, out var lastSeen) || IsStale(lastSeen));
            if (failed) round.Invalidated = true;
            return new SaveRoundStatus(round.Targets.Count, round.Responses.Count, failed,
                !failed && round.Responses.Count == round.Targets.Count);
        }
    }

    public bool Cancel(string roundId)
    {
        lock (_sync)
        {
            if (_round is not { } round || round.Id != roundId) return false;
            _round = null;
            return true;
        }
    }

    private sealed class Round(string id, HashSet<string> targets, TimeProvider clock)
    {
        public string Id { get; } = id;
        public HashSet<string> Targets { get; } = targets;
        public Dictionary<string, bool> Responses { get; } = new(StringComparer.Ordinal);
        public bool Invalidated { get; set; }
        private long Started { get; } = clock.GetTimestamp();
        public bool Expired => clock.GetElapsedTime(Started) >= TimeSpan.FromSeconds(30);
    }
}

public sealed record SaveRoundStatus(int Expected, int Responded, bool Failed, bool Complete);
