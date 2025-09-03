
using Microsoft.Graph.Models;

namespace FunctionApp.Services;

public interface IEventCacheStore
{
    Task UpsertAsync(string roomUpn, Event ev, string? visitorId, CancellationToken ct = default);
}
