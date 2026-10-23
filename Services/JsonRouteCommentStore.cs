using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using windows_route_manager.Models;
using windows_route_manager.Services.Interfaces;

namespace windows_route_manager.Services;

public sealed class JsonRouteCommentStore(string path) : IRouteCommentStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<VpnScope>(allowIntegerValues: false) }
    };

    public async Task<IReadOnlyList<RouteComment>> LoadAsync(CancellationToken cancellationToken)
    {
        string json;
        try { json = await File.ReadAllTextAsync(path, cancellationToken); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }

        var file = JsonSerializer.Deserialize<CommentFile>(json, Options);
        if (file is null || file.Version != 1 || file.Comments is null)
            throw new JsonException("Unsupported route comments file. Expected version 1 and a comments array.");
        List<RouteComment> comments = [];
        HashSet<(VpnProfileId, Ipv4Prefix)> keys = [];
        foreach (var entry in file.Comments)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.ProfileName) || !Enum.IsDefined(entry.Scope) ||
                !Ipv4Prefix.TryParse(entry.DestinationPrefix, out var prefix) || entry.Comment is null)
                throw new JsonException("The route comments file contains an invalid entry.");
            var profile = new VpnProfileId(entry.ProfileName, entry.Scope);
            if (!keys.Add((profile, prefix!)))
                throw new JsonException("The route comments file contains duplicate profile, scope and prefix entries.");
            comments.Add(new RouteComment(profile, prefix!, entry.Comment));
        }
        return comments;
    }

    public async Task SetAsync(VpnProfileId profile, Ipv4Prefix prefix, string comment, CancellationToken cancellationToken)
    {
        // Serialize read/modify/replace across application windows, including an elevated window.
        using var writeLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 1, FileOptions.DeleteOnClose);
        var comments = (await LoadAsync(cancellationToken)).ToList();
        comments.RemoveAll(entry => entry.Profile == profile && entry.Prefix == prefix);
        if (!string.IsNullOrWhiteSpace(comment)) comments.Add(new RouteComment(profile, prefix, comment));
        var file = new CommentFile
        {
            Version = 1,
            Comments = comments.Select(entry => new CommentEntry
            {
                ProfileName = entry.Profile.Name,
                Scope = entry.Profile.Scope,
                DestinationPrefix = entry.Prefix.ToString(),
                Comment = entry.Comment
            }).ToList()
        };
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(file, Options), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class CommentFile
    {
        public required int Version { get; init; }
        public required List<CommentEntry> Comments { get; init; }
    }

    private sealed class CommentEntry
    {
        public required string ProfileName { get; init; }
        public required VpnScope Scope { get; init; }
        public required string DestinationPrefix { get; init; }
        public required string Comment { get; init; }
    }
}
