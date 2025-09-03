
using System.Text.Json;
using Microsoft.Graph.Models;
using Azure.Storage.Blobs;

namespace FunctionApp.Services;

public class BlobEventCacheStore : IEventCacheStore
{
    private readonly BlobContainerClient _container;
    public BlobEventCacheStore(string connectionString, string containerName)
    {
        var svc = new BlobServiceClient(connectionString);
        _container = svc.GetBlobContainerClient(containerName);
        _container.CreateIfNotExists();
    }

    public async Task UpsertAsync(string roomUpn, Event ev, string? visitorId, CancellationToken ct = default)
    {
    var id = ev.Id ?? Guid.NewGuid().ToString("N");
        var blob = _container.GetBlobClient($"{roomUpn}/{id}.json");

        var payload = new
        {
            roomUpn,
            id,
            ev.Subject,
            start = ev.Start?.DateTime,
            end = ev.End?.DateTime,
            ev.Organizer,
            ev.Attendees,
            bodyPreview = ev.BodyPreview,
            visitorId,
            lastModified = ev.LastModifiedDateTime,
            isCancelled = ev.IsCancelled,
            created = ev.CreatedDateTime
        };

        await blob.UploadAsync(BinaryData.FromObjectAsJson(payload, new JsonSerializerOptions { WriteIndented = true }), overwrite: true, cancellationToken: ct);
    }
}
