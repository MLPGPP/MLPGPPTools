namespace MLPGPPParser.DataManager;

/// <summary>
/// Renames all bucket folders so the first bucket gets the new start id and every other bucket keeps its distance
/// from the first one. Example, start 10: 0 -> 10, 1 -> 11, 2 -> 12. Gaps are kept: 5 -> 15 if the first bucket was 0 and 5 exists.
///
/// Rules:
///  - Hash files are renamed with their folder (.{old}.in.hash -> .{new}.in.hash).
///  - Renames go through temporary names, so a new id may equal an old id that is still in use.
///  - Every step is recorded. If one fails, all completed steps are reversed and the command stops.
///  - Nothing is deleted. Junctions and symbolic links stop the command before anything changes.
///  - Bucket ids change. Anything outside this tool that stores ids (for example the games JSON) must be updated.
/// </summary>
public static class BucketRenamer {

    private const string TempPrefix = ".remap_";

    /// <returns>0 on success or when nothing needs to change, -1 on error.</returns>
    public static int Remap(string gamesPath, uint start) {
        var buckets = new List<(uint Id, string Folder)>();
        foreach (var dir in Directory.GetDirectories(gamesPath)) {
            if (uint.TryParse(Path.GetFileName(dir), out var id)) {
                buckets.Add((id, dir));
            }
        }

        if (buckets.Count == 0) {
            Logger.LogInfo("No bucket folders found");
            return 0;
        }

        buckets.Sort((a, b) => a.Id.CompareTo(b.Id));

        for (var i = 1; i < buckets.Count; i++) {
            if (buckets[i].Id == buckets[i - 1].Id) {
                Logger.LogError($"Two folders map to bucket {buckets[i].Id}. Nothing was changed.");
                return -1;
            }
        }

        foreach (var (id, folder) in buckets) {
            if (FileUtilities.IsReparsePoint(folder)) {
                Logger.LogError($"{id}: junction or symbolic link. Nothing was changed.");
                return -1;
            }
        }

        if (Directory.GetDirectories(gamesPath).Any(dir => Path.GetFileName(dir).StartsWith(TempPrefix, StringComparison.Ordinal))) {
            Logger.LogError($"Leftover '{TempPrefix}*' folders from an earlier run exist in the games folder. Remove or rename them first. Nothing was changed.");
            return -1;
        }

        var plan = new List<(uint Old, uint New, string Folder)>();
        var firstId = buckets[0].Id;
        foreach (var (id, folder) in buckets) {
            var newId = (long)start + (id - firstId);
            if (newId > uint.MaxValue) {
                Logger.LogError($"Bucket {id} would get id {newId}, which is too large. Nothing was changed.");
                return -1;
            }
            plan.Add((id, (uint)newId, folder));
        }

        if (plan.All(step => step.Old == step.New)) {
            Logger.LogInfo($"Buckets already start at {start}. Nothing to do.");
            return 0;
        }

        // a file with the new name would block the folder
        foreach (var step in plan) {
            var target = Path.Combine(gamesPath, step.New.ToString());
            if (File.Exists(target)) {
                Logger.LogError($"A file named '{step.New}' exists in the games folder, so bucket {step.Old} cannot become {step.New}. Nothing was changed.");
                return -1;
            }
        }

        Logger.LogInfo($"Renaming {plan.Count} bucket folders, first bucket becomes {start}:");
        foreach (var step in plan) {
            Logger.LogInfo($"  {step.Old} -> {step.New}");
        }

        // undo steps, most recent last; run in reverse when something fails
        var undo = new Stack<Action>();
        var temps = new List<string>();

        try {
            // phase 1: rename hash files and move every folder to a temporary name
            for (var i = 0; i < plan.Count; i++) {
                var step = plan[i];

                var oldHash = Path.Combine(step.Folder, $".{step.Old}.in.hash");
                if (File.Exists(oldHash)) {
                    var newHash = Path.Combine(step.Folder, $".{step.New}.in.hash");
                    File.Move(oldHash, newHash);
                    undo.Push(() => File.Move(newHash, oldHash));
                }

                var temp = Path.Combine(gamesPath, $"{TempPrefix}{i}");
                Directory.Move(step.Folder, temp);
                undo.Push(() => Directory.Move(temp, step.Folder));
                temps.Add(temp);
            }

            // phase 2: move temporary folders to their final names
            for (var i = 0; i < plan.Count; i++) {
                var final = Path.Combine(gamesPath, plan[i].New.ToString());
                var temp = temps[i];
                Directory.Move(temp, final);
                undo.Push(() => Directory.Move(final, temp));
            }
        } catch (Exception ex) {
            Logger.LogError($"Remap failed: {ex.Message}. Reversing {undo.Count} completed steps.");
            while (undo.Count > 0) {
                try {
                    undo.Pop()();
                } catch (Exception undoEx) {
                    Logger.LogError($"Could not reverse a step: {undoEx.Message}. Check the games folder manually.");
                }
            }
            return -1;
        }

        Logger.LogInfo($"Remap finished: {plan.Count} bucket folders renamed");
        return 0;
    }
}
