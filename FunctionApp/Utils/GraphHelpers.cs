
namespace FunctionApp.Utils;

public static class GraphHelpers
{
    public static string? TryParseRoomFromResource(string resource)
    {
        // examples:
        // /users/room1@contoso.com/events('AAMk...')
        // /users('room1@contoso.com')/events
        var lower = resource.ToLowerInvariant();
        var key = "/users/";
        var idx = lower.IndexOf(key);
        if (idx < 0) return null;
    var rest = resource[(idx + key.Length)..];
        // Trim leading '(' if users('room@')
    if (rest.StartsWith("('")) rest = rest[2..];
        // Extract until next '/' or quote
        var endIdx = rest.IndexOfAny(new[] { '/', '\'', ')' });
        var upn = endIdx > 0 ? rest[..endIdx] : rest;
        return upn;
    }
}
