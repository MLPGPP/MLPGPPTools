namespace MLPGPPParser.DataManager;

/// <summary>
/// Makes sure bucket folders 0 .. count-1 exist in the games folder.
/// Existing folders are left alone. Only missing ones are created, empty.
/// </summary>
public static class BucketCreator {

    /// <returns>0 on success, -1 if any folder could not be created.</returns>
    public static int AddBuckets(string gamesPath, int count) {
        var existing = Directory.GetDirectories(gamesPath)
            .Select(Path.GetFileName)
            .Where(name => uint.TryParse(name, out _))
            .Select(name => uint.Parse(name!))
            .ToHashSet();

        var created = 0;
        var alreadyExisted = 0;
        var failed = 0;

        for (uint id = 0; id < count; id++) {
            if (existing.Contains(id)) {
                alreadyExisted++;
                continue;
            }

            var path = Path.Combine(gamesPath, id.ToString());

            // a file with this name blocks the folder; never delete or overwrite it
            if (File.Exists(path)) {
                Logger.LogWarning($"{id}: a file with this name exists, cannot create folder");
                failed++;
                continue;
            }

            try {
                Directory.CreateDirectory(path);
                created++;
                Logger.LogInfo($"{id}: created");
            } catch (Exception ex) {
                Logger.LogError($"{id}: could not create folder: {ex.Message}");
                failed++;
            }
        }

        Logger.LogInfo($"Buckets 0..{count - 1}: {created} created, {alreadyExisted} already existed, {failed} failed");
        return failed == 0 ? 0 : -1;
    }
}
