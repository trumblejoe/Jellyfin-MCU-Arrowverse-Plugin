namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>
/// "Start at my next unwatched item": a playlist drops watched items from the FRONT only, up to
/// the first unwatched one. Something watched out of order further down stays in its place
/// (Jellyfin shows it as watched), so it isn't lost from the order before the user gets there.
/// To skip an item on purpose, mark it watched.
/// </summary>
public static class WatchProgress
{
    /// <summary>The playlist from the first unwatched item onward.</summary>
    public static List<Guid> FromNextUnwatched(IReadOnlyList<Guid> ids, Func<Guid, bool> isWatched)
    {
        var start = 0;
        while (start < ids.Count && isWatched(ids[start]))
        {
            start++;
        }

        return ids.Skip(start).ToList();
    }

    /// <summary>The watched items at the front of the playlist, which should be removed.</summary>
    public static List<Guid> WatchedAtFront(IReadOnlyList<Guid> ids, Func<Guid, bool> isWatched) =>
        ids.TakeWhile(isWatched).ToList();
}
