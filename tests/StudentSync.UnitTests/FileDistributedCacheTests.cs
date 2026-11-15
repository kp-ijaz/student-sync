using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using StudentSync.Web.Services;

namespace StudentSync.UnitTests;

/// <summary>The on-disk session cache that keeps people signed in across web restarts.</summary>
public sealed class FileDistributedCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ss-session-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private static readonly byte[] Value = Encoding.UTF8.GetBytes("protected-session-bytes");
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private FileDistributedCache NewCache() => new(_dir, _clock);
    private DistributedCacheEntryOptions ExpiresIn(TimeSpan t) => new() { AbsoluteExpiration = _clock.Now.Add(t) };

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public async Task An_entry_survives_a_new_instance_like_a_restart()
    {
        await NewCache().SetAsync("ss-session:abc", Value, ExpiresIn(TimeSpan.FromDays(30)), Ct);
        Assert.Equal(Value, await NewCache().GetAsync("ss-session:abc", Ct));
        Assert.Equal(Value, NewCache().Get("ss-session:abc"));
    }

    [Fact]
    public async Task Expired_entries_are_not_returned_and_are_deleted()
    {
        var cache = NewCache();
        await cache.SetAsync("k", Value, ExpiresIn(TimeSpan.FromMinutes(10)), Ct);
        _clock.Now = _clock.Now.AddMinutes(10);
        Assert.Null(await cache.GetAsync("k", Ct));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Relative_expiry_counts_from_now_and_no_expiry_never_expires()
    {
        var cache = NewCache();
        await cache.SetAsync("relative", Value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) }, Ct);
        await cache.SetAsync("forever", Value, new DistributedCacheEntryOptions(), Ct);
        _clock.Now = _clock.Now.AddMinutes(59);
        Assert.NotNull(await cache.GetAsync("relative", Ct));
        _clock.Now = _clock.Now.AddYears(50);
        Assert.Null(await cache.GetAsync("relative", Ct));
        Assert.Equal(Value, await cache.GetAsync("forever", Ct));
    }

    [Fact]
    public void Sliding_expiry_is_refused_rather_than_silently_ignored()
    {
        var ex = Assert.Throws<NotSupportedException>(() => NewCache().Set("k", Value, new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) }));
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public async Task Overwrite_replaces_the_value_and_leaves_no_temporary_files()
    {
        var cache = NewCache();
        await cache.SetAsync("k", Value, ExpiresIn(TimeSpan.FromDays(1)), Ct);
        var second = Encoding.UTF8.GetBytes("rotated");
        cache.Set("k", second, ExpiresIn(TimeSpan.FromDays(1)));
        Assert.Equal(second, await cache.GetAsync("k", Ct));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Remove_deletes_the_entry()
    {
        var cache = NewCache();
        await cache.SetAsync("k", Value, ExpiresIn(TimeSpan.FromDays(1)), Ct);
        await cache.RemoveAsync("k", Ct);
        Assert.Null(cache.Get("k"));
        Assert.Empty(Directory.GetFiles(_dir));
        cache.Remove("never-existed"); // no exception
    }

    [Fact]
    public async Task The_key_never_appears_on_disk()
    {
        const string sid = "ss-session:SuperSecretSessionId+/=";
        await NewCache().SetAsync(sid, Value, ExpiresIn(TimeSpan.FromDays(1)), Ct);
        var name = Path.GetFileName(Assert.Single(Directory.GetFiles(_dir)));
        Assert.DoesNotContain("SuperSecret", name);
        Assert.Matches("^[0-9a-f]{64}\\.entry$", name);
    }

    [Fact]
    public async Task A_truncated_file_is_treated_as_missing()
    {
        var cache = NewCache();
        await cache.SetAsync("k", Value, ExpiresIn(TimeSpan.FromDays(1)), Ct);
        await File.WriteAllBytesAsync(Directory.GetFiles(_dir)[0], [1, 2, 3], Ct);
        Assert.Null(await cache.GetAsync("k", Ct));
    }

    [Fact]
    public async Task Abandoned_expired_sessions_are_swept_at_most_hourly()
    {
        var cache = NewCache();
        await cache.SetAsync("abandoned", Value, ExpiresIn(TimeSpan.FromMinutes(30)), Ct); // first write sweeps (nothing yet)
        _clock.Now = _clock.Now.AddMinutes(45);
        await cache.SetAsync("active", Value, ExpiresIn(TimeSpan.FromDays(1)), Ct);      // within the hour: no sweep
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
        _clock.Now = _clock.Now.AddMinutes(30);
        await cache.SetAsync("active", Value, ExpiresIn(TimeSpan.FromDays(1)), Ct);      // an hour later: swept
        Assert.Single(Directory.GetFiles(_dir));
        Assert.NotNull(await cache.GetAsync("active", Ct));
    }
}
