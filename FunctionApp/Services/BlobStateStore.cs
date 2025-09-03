
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

namespace FunctionApp.Services;

public class BlobStateStore : IStateStore
{
    private readonly BlobContainerClient _container;

    public BlobStateStore(string connectionString, string containerName)
    {
        var svc = new BlobServiceClient(connectionString);
        _container = svc.GetBlobContainerClient(containerName);
        _container.CreateIfNotExists();
    }

    private BlobClient SubBlob(string roomUpn) => _container.GetBlobClient($"sub/{roomUpn}.json");
    private BlobClient DeltaBlob(string roomUpn) => _container.GetBlobClient($"sub/{roomUpn}.delta");

    public async Task<SubscriptionState?> GetSubscriptionAsync(string roomUpn, CancellationToken ct = default)
    {
        var blob = SubBlob(roomUpn);
        if (!await blob.ExistsAsync(ct)) return null;
        var resp = await blob.DownloadContentAsync(ct);
        return resp.Value.Content.ToObjectFromJson<SubscriptionState>();
    }

    public async Task SetSubscriptionAsync(SubscriptionState state, CancellationToken ct = default)
    {
        var blob = SubBlob(state.RoomUpn);
        using var ms = new MemoryStream();
        await JsonSerializer.SerializeAsync(ms, state, cancellationToken: ct);
        ms.Position = 0;
        await blob.UploadAsync(ms, overwrite: true, cancellationToken: ct);
    }

    public async Task<string?> GetDeltaLinkAsync(string roomUpn, CancellationToken ct = default)
    {
        var blob = DeltaBlob(roomUpn);
        if (!await blob.ExistsAsync(ct)) return null;
        var resp = await blob.DownloadContentAsync(ct);
        return resp.Value.Content.ToString();
    }

    public async Task SetDeltaLinkAsync(string roomUpn, string deltaLink, CancellationToken ct = default)
    {
        var blob = DeltaBlob(roomUpn);
        await blob.UploadAsync(new BinaryData(deltaLink), overwrite: true, cancellationToken: ct);
    }

    public async Task<IEnumerable<string>> GetKnownRoomsAsync(CancellationToken ct = default)
    {
        var result = new List<string>();
        await foreach (var item in _container.GetBlobsAsync(prefix: "sub/", cancellationToken: ct))
        {
            var name = item.Name; // sub/room@contoso.com.json
            var room = name.Substring("sub/".Length);
            if (room.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                result.Add(room[..^5]);
        }
        return result;
    }
}
