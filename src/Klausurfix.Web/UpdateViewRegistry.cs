using System.Security.Cryptography;
using System.Diagnostics;

/// <summary>
/// In-memory coordination primitive. An update cannot use a successful round as
/// an installation permit until registered views are prevented from editing.
/// </summary>
public sealed class UpdateViewRegistry
{
    private readonly object _sync = new();
    private readonly HashSet<string> _views = new(StringComparer.Ordinal);
    private Round? _round;

    public string Register()
    {
        lock (_sync)
        {
            if (_views.Count >= 256) throw new InvalidOperationException("Zu viele Ansichten registriert.");
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _views.Add(id);
            if (_round is not null) _round.Invalidated = true;
            return id;
        }
    }

    public bool Contains(string id)
    {
        lock (_sync) return _views.Contains(id);
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
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _round = new Round(id, new HashSet<string>(_views, StringComparer.Ordinal));
            return id;
        }
    }

    public string? PendingFor(string view)
    {
        lock (_sync)
            return _round is { Invalidated: false, Expired: false } round && round.Targets.Contains(view) ? round.Id : null;
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
            var failed = round.Invalidated || round.Expired || round.Responses.Values.Any(saved => !saved);
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

    private sealed class Round(string id, HashSet<string> targets)
    {
        public string Id { get; } = id;
        public HashSet<string> Targets { get; } = targets;
        public Dictionary<string, bool> Responses { get; } = new(StringComparer.Ordinal);
        public bool Invalidated { get; set; }
        private long Started { get; } = Stopwatch.GetTimestamp();
        public bool Expired => Stopwatch.GetElapsedTime(Started) >= TimeSpan.FromSeconds(30);
    }
}

public sealed record SaveRoundStatus(int Expected, int Responded, bool Failed, bool Complete);
