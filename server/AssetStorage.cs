using Google.Cloud.Storage.V1;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StitchHelper;

public interface IPrivateObjectStore
{
    Task Put(string key, Stream source, string mediaType, CancellationToken cancellation = default);
    Task<Stream> Read(string key, CancellationToken cancellation = default);
    Task Delete(string key, CancellationToken cancellation = default);
}
public interface IPatternAssetStore : IPrivateObjectStore { }
public interface IBackupArtifactStore : IPrivateObjectStore { }

public static class StorageKeys
{
    public static string Validate(string key) => Regex.IsMatch(key, "^[a-f0-9]{32}$") ? key : throw new InvalidDataException("Invalid private storage key.");
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
public class LocalObjectStore(string directory) : IPrivateObjectStore
{
    private string PathFor(string key) => Path.Combine(Path.GetFullPath(directory), StorageKeys.Validate(key));
    public async Task Put(string key, Stream source, string mediaType, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(key);
        var temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) await source.CopyToAsync(file, cancellation);
            File.Move(temporary, path); // Immutable: existing objects are never overwritten.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task<Stream> Read(string key, CancellationToken cancellation = default) => Task.FromResult<Stream>(File.OpenRead(PathFor(key)));
    public Task Delete(string key, CancellationToken cancellation = default) { File.Delete(PathFor(key)); return Task.CompletedTask; }
}
public sealed class LocalPatternAssetStore(string root) : LocalObjectStore(Path.Combine(root, "sources")), IPatternAssetStore { }
public sealed class LocalBackupArtifactStore(string root) : LocalObjectStore(Path.Combine(root, "backups")), IBackupArtifactStore { }

public class GoogleObjectStore(StorageClient client, string bucket) : IPrivateObjectStore
{
    public async Task Put(string key, Stream source, string mediaType, CancellationToken cancellation = default) =>
        await client.UploadObjectAsync(bucket, StorageKeys.Validate(key), mediaType, source,
            new UploadObjectOptions { IfGenerationMatch = 0 }, cancellation);
    public async Task<Stream> Read(string key, CancellationToken cancellation = default)
    {
        // Temporary download staging is disposable and bounded by the source/export limits.
        var stream = new FileStream(Path.Combine(Path.GetTempPath(), "stitch-read-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try { await client.DownloadObjectAsync(bucket, StorageKeys.Validate(key), stream, cancellationToken: cancellation); stream.Position = 0; return stream; }
        catch { await stream.DisposeAsync(); throw; }
    }
    public async Task Delete(string key, CancellationToken cancellation = default)
    {
        try { await client.DeleteObjectAsync(bucket, StorageKeys.Validate(key), cancellationToken: cancellation); }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound) { }
    }
}
public sealed class GooglePatternAssetStore(StorageClient client, string bucket) : GoogleObjectStore(client, bucket), IPatternAssetStore { }
public sealed class GoogleBackupArtifactStore(StorageClient client, string bucket) : GoogleObjectStore(client, bucket), IBackupArtifactStore { }
