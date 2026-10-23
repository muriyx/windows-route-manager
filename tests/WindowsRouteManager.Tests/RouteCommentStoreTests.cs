using System.IO;
using System.Text.Json;
using windows_route_manager.Models;
using windows_route_manager.Services;

namespace WindowsRouteManager.Tests;

public sealed class RouteCommentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wrm-comments-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public RouteCommentStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "route-comments.json");
    }

    [Fact]
    public async Task MissingFileLoadsEmptyWithoutCreatingFiles()
    {
        Assert.Empty(await new JsonRouteCommentStore(_path).LoadAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task RoundTripUsesNameScopeAndNormalizedPrefixAndPreservesUnicode()
    {
        var store = new JsonRouteCommentStore(_path);
        var profile = new VpnProfileId("Office | ' VPN", VpnScope.User);
        var prefix = Ipv4Prefix.Parse("10.20.30.40/8");
        var comment = "Серверы \"dev\"\nSecond line";
        await store.SetAsync(profile, prefix, comment, CancellationToken.None);
        await store.SetAsync(profile with { Scope = VpnScope.AllUsers }, prefix, "Shared", CancellationToken.None);
        await store.SetAsync(profile with { Name = "Other" }, prefix, "Other VPN", CancellationToken.None);
        await store.SetAsync(profile, Ipv4Prefix.Parse("172.16.0.0/12"), "Other network", CancellationToken.None);
        var entries = await new JsonRouteCommentStore(_path).LoadAsync(CancellationToken.None);
        Assert.Equal(4, entries.Count);
        Assert.Equal(comment, entries.Single(entry => entry.Profile == profile && entry.Prefix == prefix).Comment);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(_path));
        Assert.Equal(1, json.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal("10.0.0.0/8", json.RootElement.GetProperty("Comments")[0].GetProperty("DestinationPrefix").GetString());
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task IndependentWindowsMergeUnrelatedEntriesAndClearingPreservesOthers()
    {
        var first = new JsonRouteCommentStore(_path);
        var second = new JsonRouteCommentStore(_path);
        var profile = new VpnProfileId("Office", VpnScope.User);
        var prefix = Ipv4Prefix.Parse("10.0.0.0/8");
        await first.LoadAsync(CancellationToken.None);
        await second.LoadAsync(CancellationToken.None);
        await first.SetAsync(profile, prefix, "First", CancellationToken.None);
        await second.SetAsync(profile with { Scope = VpnScope.AllUsers }, prefix, "Second", CancellationToken.None);
        await first.SetAsync(profile, prefix, "Replacement", CancellationToken.None);
        Assert.Equal(2, (await second.LoadAsync(CancellationToken.None)).Count);
        await second.SetAsync(profile, prefix, "", CancellationToken.None);
        var remaining = Assert.Single(await first.LoadAsync(CancellationToken.None));
        Assert.Equal(VpnScope.AllUsers, remaining.Profile.Scope);
        Assert.Equal("Second", remaining.Comment);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"Comments\":[]}")]
    [InlineData("{\"Version\":1,\"Comments\":null}")]
    [InlineData("{\"Version\":1,\"Comments\":[null]}")]
    [InlineData("{\"Version\":1,\"Comments\":[{}]}")]
    [InlineData("{\"Version\":1,\"Comments\":[{\"ProfileName\":\"Office\",\"Scope\":\"User\",\"DestinationPrefix\":\"::1/128\",\"Comment\":\"Keep\"}]}")]
    [InlineData("{\"Version\":1,\"Comments\":[{\"ProfileName\":\"Office\",\"Scope\":\"Other\",\"DestinationPrefix\":\"10.0.0.0/8\",\"Comment\":\"Keep\"}]}")]
    [InlineData("{\"Version\":1,\"Comments\":[],\"UnknownData\":true}")]
    public async Task InvalidOrUnsupportedFileIsNeverOverwritten(string contents)
    {
        await File.WriteAllTextAsync(_path, contents);
        var store = new JsonRouteCommentStore(_path);
        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<JsonException>(() => store.SetAsync(new("Office", VpnScope.User), Ipv4Prefix.Parse("10.0.0.0/8"), "New", CancellationToken.None));
        Assert.Equal(contents, await File.ReadAllTextAsync(_path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task DuplicateNormalizedKeysAreRejectedWithoutDataLoss()
    {
        var contents = """
            {"Version":1,"Comments":[
              {"ProfileName":"Office","Scope":"User","DestinationPrefix":"10.0.0.0/8","Comment":"First"},
              {"ProfileName":"Office","Scope":"User","DestinationPrefix":"10.20.30.40/8","Comment":"Second"}
            ]}
            """;
        await File.WriteAllTextAsync(_path, contents);
        await Assert.ThrowsAsync<JsonException>(() => new JsonRouteCommentStore(_path).SetAsync(
            new("Office", VpnScope.User), Ipv4Prefix.Parse("10.0.0.0/8"), "New", CancellationToken.None));
        Assert.Equal(contents, await File.ReadAllTextAsync(_path));
    }

    [Fact]
    public async Task FailedReplacementKeepsOriginalAndRemovesTemporaryFile()
    {
        var store = new JsonRouteCommentStore(_path);
        var profile = new VpnProfileId("Office", VpnScope.User);
        var prefix = Ipv4Prefix.Parse("10.0.0.0/8");
        await store.SetAsync(profile, prefix, "Original", CancellationToken.None);
        var contents = await File.ReadAllTextAsync(_path);
        using (var readLock = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => store.SetAsync(profile, prefix, "Replacement", CancellationToken.None));
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(contents, await File.ReadAllTextAsync(_path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ConcurrentWriterIsReportedInsteadOfOverwriting()
    {
        using (var writeLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            await Assert.ThrowsAsync<IOException>(() => new JsonRouteCommentStore(_path).SetAsync(
                new("Office", VpnScope.User), Ipv4Prefix.Parse("10.0.0.0/8"), "New", CancellationToken.None));
        Assert.False(File.Exists(_path));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CanceledSavePreservesExistingFile()
    {
        var store = new JsonRouteCommentStore(_path);
        var profile = new VpnProfileId("Office", VpnScope.User);
        var prefix = Ipv4Prefix.Parse("10.0.0.0/8");
        await store.SetAsync(profile, prefix, "Original", CancellationToken.None);
        var contents = await File.ReadAllTextAsync(_path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetAsync(profile, prefix, "New", cancellation.Token));
        Assert.Equal(contents, await File.ReadAllTextAsync(_path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
