
using System.Text.Json;
using Microsoft.Graph.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FunctionApp.Services;

public class BlobEventCacheStore : IEventCacheStore
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobEventCacheStore> _logger;

    public BlobEventCacheStore(string connectionString, string containerName, ILogger<BlobEventCacheStore> logger)
    {
        var svc = new BlobServiceClient(connectionString);
        _container = svc.GetBlobContainerClient(containerName);
        _container.CreateIfNotExists();
        _logger = logger;
    }

    public async Task UpsertAsync(string roomUpn, Event ev, string? visitorId)
    {
        var id = ev.Id ?? Guid.NewGuid().ToString("N");
        var blob = _container.GetBlobClient($"{roomUpn}/{id}.json");

        var payload = new
        {
            roomUpn,
            id,
            Subject = ev.Subject,
            start = ev.Start?.DateTime,
            end = ev.End?.DateTime,
            Organizer = ev.Organizer,
            Attendees = ev.Attendees,
            bodyPreview = ev.BodyPreview,
            visitorId,
            lastModified = ev.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            isCancelled = ev.IsCancelled ?? false,
            created = ev.CreatedDateTime ?? DateTimeOffset.UtcNow,
            ingestedAtUtc = DateTime.UtcNow
        };

        await blob.UploadAsync(BinaryData.FromObjectAsJson(payload, new JsonSerializerOptions { WriteIndented = true }), overwrite: true);
    }

    public async Task<IEnumerable<JsonElement>> GetAllEventsAsync(string roomUpn)
    {
        var events = new List<JsonElement>();

        await foreach (var blobItem in _container.GetBlobsAsync(prefix: $"{roomUpn}/"))
        {
            try
            {
                var blob = _container.GetBlobClient(blobItem.Name);
                var content = await blob.DownloadContentAsync();
                var json = content.Value.Content.ToString();
                using var doc = JsonDocument.Parse(json);
                // JsonElement は struct なので Clone して格納
                events.Add(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize event blob: {blobName}", blobItem.Name);
            }
        }

        return events;
    }
}
