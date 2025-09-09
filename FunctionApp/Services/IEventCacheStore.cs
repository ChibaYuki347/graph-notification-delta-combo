
using Microsoft.Graph.Models;

namespace FunctionApp.Services;

public interface IEventCacheStore
{
    Task UpsertAsync(string roomUpn, Microsoft.Graph.Models.Event graphEvent, string? visitorId);
    // Blob に保存されたイベント(JSON)を JsonElement として列挙
    Task<IEnumerable<System.Text.Json.JsonElement>> GetAllEventsAsync(string roomUpn);
}
