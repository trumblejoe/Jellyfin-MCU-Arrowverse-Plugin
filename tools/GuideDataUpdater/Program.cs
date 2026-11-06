// Refreshes the guide backup copies in the given folder (the checked-out "data" branch).
// Prints counts only — never titles. Exits 1 if any guide couldn't be refreshed, so the
// workflow run fails and GitHub emails the repo owner.
using Jellyfin.Plugin.WatchOrder.Sources;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: GuideDataUpdater <data-folder>");
    return 2;
}

var store = new GuideSnapshotStore(args[0]);
using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-WatchOrderPlaylists-Backup/1.1");

// backupUrl: null — the data branch *is* the backup here.
var arrowverse = await GuideDownloader.ArrowverseAsync(http, store, backupUrl: null, CancellationToken.None);
var mcu = await GuideDownloader.McuAsync(http, store, backupUrl: null, CancellationToken.None);

var ok = true;
foreach (var (name, outcome, count, fresh, error) in new[]
         {
             ("arrowverse.info", arrowverse.Outcome, arrowverse.Items?.Count, arrowverse.FreshCount, arrowverse.Error),
             ("MCU guide", mcu.Outcome, mcu.Items?.Count, mcu.FreshCount, mcu.Error)
         })
{
    Console.WriteLine($"{name}: {outcome}; backup has {count?.ToString() ?? "no"} entries (download: {fresh?.ToString() ?? "failed"}){(error is null ? "" : $" - {error}")}");
    ok &= outcome == SnapshotOutcome.Fresh;
}

return ok ? 0 : 1;
