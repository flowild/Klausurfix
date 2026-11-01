using System.Collections.Concurrent;
using System.Security.Cryptography;

// Session credentials are random, process-local and never sent to JavaScript.
public sealed class LocalSessions
{
    public const string CookieName = "klausurfix_session";
    private readonly ConcurrentDictionary<string, byte> _sessions = new();

    public bool IsValid(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out var token) && _sessions.ContainsKey(token);

    public void Issue(HttpContext context)
    {
        _sessions.Clear();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = 0;
        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Path = "/"
        });
    }

    public void RevokeAll(HttpContext context)
    {
        _sessions.Clear();
        context.Response.Cookies.Delete(CookieName);
    }
}
