using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace StudentSync.Web.Services;

/// <summary>
/// <see cref="IDistributedCache"/> on the local file system, used for signed-in sessions when <c>Sessions:StorePath</c>
/// is configured so a restart or redeploy of a single web node does not sign everyone out. Each entry is one file named
/// by the SHA-256 of its key (the key itself, a session id, never reaches the disk) holding the absolute expiry and the
/// value. Values are stored as given: <see cref="SessionStore"/> encrypts its tokens before they get here.
/// Only absolute expiry is supported. Several web nodes need a shared cache (Redis or SQL Server) instead.
/// </summary>
public sealed class FileDistributedCache : IDistributedCache
{
    private const string Extension = ".entry";
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);
    private readonly string _root;
    private readonly TimeProvider _time;
    private long _nextSweepTicks;

    public FileDistributedCache(string root, TimeProvider? time = null)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _time = time ?? TimeProvider.System;
    }

    public byte[]? Get(string key) => Read(PathFor(key), ReadAll(PathFor(key)));

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        var path = PathFor(key);
        byte[]? bytes;
        try { bytes = await File.ReadAllBytesAsync(path, token); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { bytes = null; }
        return Read(path, bytes);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var (path, temp, data) = Prepare(key, value, options);
        using (var stream = new FileStream(temp, CreateOptions())) stream.Write(data);
        File.Move(temp, path, overwrite: true);
        SweepIfDue();
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        var (path, temp, data) = Prepare(key, value, options);
        await using (var stream = new FileStream(temp, CreateOptions())) await stream.WriteAsync(data, token);
        // Write-then-rename: a reader sees the old entry or the new one, never a half-written file.
        File.Move(temp, path, overwrite: true);
        SweepIfDue();
    }

    /// <summary>No sliding expiry, so there is nothing to refresh.</summary>
    public void Refresh(string key) { }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => TryDelete(PathFor(key));

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    private string PathFor(string key) =>
        Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + Extension);

    private (string Path, string Temp, byte[] Data) Prepare(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (options.SlidingExpiration is not null)
            throw new NotSupportedException("FileDistributedCache supports absolute expiry only.");
        var expires = options.AbsoluteExpiration
            ?? (options.AbsoluteExpirationRelativeToNow is { } relative ? _time.GetUtcNow().Add(relative) : DateTimeOffset.MaxValue);
        var data = new byte[sizeof(long) + value.Length];
        BinaryPrimitives.WriteInt64LittleEndian(data, expires.UtcTicks);
        value.CopyTo(data, sizeof(long));
        var path = PathFor(key);
        return (path, $"{path}.{Guid.NewGuid():N}.tmp", data);
    }

    private byte[]? Read(string path, byte[]? bytes)
    {
        if (bytes is null) return null;
        if (bytes.Length < sizeof(long) || IsExpired(bytes))
        {
            TryDelete(path);
            return null;
        }
        return bytes[sizeof(long)..];
    }

    private bool IsExpired(ReadOnlySpan<byte> entry) => BinaryPrimitives.ReadInt64LittleEndian(entry) <= _time.GetUtcNow().UtcTicks;

    private static byte[]? ReadAll(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    /// <summary>Owner-only files on Linux/macOS; the values are encrypted anyway.</summary>
    private static FileStreamOptions CreateOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    /// <summary>At most hourly, removes expired entries of sessions nobody came back to, and abandoned temporary files.</summary>
    private void SweepIfDue()
    {
        var now = _time.GetUtcNow().UtcTicks;
        var due = Interlocked.Read(ref _nextSweepTicks);
        if (now < due || Interlocked.CompareExchange(ref _nextSweepTicks, now + SweepInterval.Ticks, due) != due) return;
        foreach (var file in Directory.EnumerateFiles(_root))
        {
            try
            {
                if (file.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    if (File.GetLastWriteTimeUtc(file).Ticks < now - SweepInterval.Ticks) TryDelete(file);
                    continue;
                }
                if (!file.EndsWith(Extension, StringComparison.Ordinal)) continue;
                Span<byte> header = stackalloc byte[sizeof(long)];
                bool expired;
                using (var stream = File.OpenRead(file)) expired = stream.ReadAtLeast(header, sizeof(long), throwOnEndOfStream: false) < sizeof(long) || IsExpired(header);
                if (expired) TryDelete(file);
            }
            catch (IOException) { /* in use or already gone; the next sweep retries */ }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* removed concurrently */ }
    }
}
