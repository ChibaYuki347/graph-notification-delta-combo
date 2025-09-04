
namespace FunctionApp.Utils;

public static class GraphHelpers
{
    public static string? TryParseRoomFromResource(string resource)
    {
        // examples:
        // /users/room1@contoso.com/events('AAMk...')
        // /users('room1@contoso.com')/events
        // Users/1c805180-8163-4032-9889-d5a0ad456d90/Events/...
        var lower = resource.ToLowerInvariant();
        
        // Try different patterns
        var patterns = new[] { "/users/", "users/" };
        foreach (var pattern in patterns)
        {
            var idx = lower.IndexOf(pattern);
            if (idx < 0) continue;
            
            var rest = resource[(idx + pattern.Length)..];
            // Trim leading '(' if users('room@')
            if (rest.StartsWith("('")) rest = rest[2..];
            // Extract until next '/' or quote
            var endIdx = rest.IndexOfAny(new[] { '/', '\'', ')', '\\' });
            var identifier = endIdx > 0 ? rest[..endIdx] : rest;
            
            // If it's a GUID, we need to look it up somehow, but for now return the GUID
            // This case represents a user ID rather than UPN
            if (IsGuid(identifier))
            {
                // For now, we don't have a way to resolve GUID to UPN without additional Graph calls
                // This might be the cause of our failures - we need to handle GUID resources
                return identifier; // Return GUID for now
            }
            
            return identifier; // Return UPN
        }
        
        return null;
    }

    private static bool IsGuid(string str)
    {
        return Guid.TryParse(str, out _);
    }
}
