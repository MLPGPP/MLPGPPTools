namespace MLPGPPParser;

/// <summary>
/// Compares the hashes of two games folders ("g" databases), e.g. <c>hash find_g</c>.
///
/// For every game hash in folder A:
///  - SAME:   folder B has the same hash in the same bucket id.
///  - MOVED:  folder B has the same hash in a different bucket id.
///  - ONLY_A: folder B does not have this hash at all.
/// For every hash in folder B that folder A does not have: ONLY_B.
///
/// Empty buckets (no .hash file) are skipped and only counted.
/// </summary>
public static class GamesComparer {

    public static async Task<int> Compare(string gamesA, string gamesB) {
        Logger.LogInfo($"Loading hashes from A: {gamesA}");
        var a = await LoadBuckets(gamesA);
        if (a == null) {
            return -1;
        }

        Logger.LogInfo($"Loading hashes from B: {gamesB}");
        var b = await LoadBuckets(gamesB);
        if (b == null) {
            return -1;
        }

        var bIdsByKey = new Dictionary<string, List<uint>>();
        foreach (var (id, key) in b.Hashes) {
            if (bIdsByKey.TryGetValue(key, out var ids) == false) {
                bIdsByKey[key] = ids = new List<uint>();
            }
            ids.Add(id);
        }

        var aKeys = new HashSet<string>(a.Hashes.Values);
        var same = 0;
        var moved = 0;
        var onlyA = 0;
        var onlyB = 0;

        Logger.LogInfo("=== A -> B ===");
        foreach (var (id, key) in a.Hashes) {
            if (b.Hashes.TryGetValue(id, out var bKeyAtSameId) && bKeyAtSameId == key) {
                same++;
                Logger.LogInfo($"SAME    A:{id} == B:{id}");
            } else if (bIdsByKey.TryGetValue(key, out var bIds)) {
                moved++;
                Logger.LogWarning($"MOVED   A:{id} == B:{string.Join(", B:", bIds)}");
            } else {
                onlyA++;
                Logger.LogError($"ONLY_A  A:{id} (not in B)");
            }
        }

        Logger.LogInfo("=== only in B ===");
        foreach (var (id, key) in b.Hashes) {
            if (aKeys.Contains(key) == false) {
                onlyB++;
                Logger.LogError($"ONLY_B  B:{id} (not in A)");
            }
        }

        Logger.LogInfo($"Summary: same {same}, moved {moved}, only in A {onlyA}, only in B {onlyB}");
        Logger.LogInfo($"Empty buckets: A {a.EmptyBuckets}, B {b.EmptyBuckets}. Invalid hash files: A {a.InvalidFiles}, B {b.InvalidFiles}");
        return 0;
    }

    private record Buckets(SortedDictionary<uint, string> Hashes, int EmptyBuckets, int InvalidFiles);

    /// <summary>
    /// Reads the first .hash file of every numbered folder. The key is the full hash (algorithm + value),
    /// so hash files with different algorithms never match by accident.
    /// </summary>
    private static async Task<Buckets?> LoadBuckets(string gamesPath) {
        if (Directory.Exists(gamesPath) == false) {
            Logger.LogError($"Games folder not found: {gamesPath}");
            return null;
        }

        var hashes = new SortedDictionary<uint, string>();
        var emptyBuckets = 0;
        var invalidFiles = 0;

        foreach (var dir in Directory.GetDirectories(gamesPath)) {
            if (uint.TryParse(Path.GetFileName(dir), out var id) == false) {
                continue;
            }

            var hashFiles = Directory.GetFiles(dir, "*.hash");
            if (hashFiles.Length == 0) {
                emptyBuckets++;
                continue;
            }

            var hashFile = await HashFile.LoadHashFile(hashFiles[0]);
            if (hashFile.IsValid == false || hashFile.Hashes == null || hashFile.Hashes.Length == 0) {
                Logger.LogWarning($"{id}: invalid hash file {Path.GetFileName(hashFiles[0])}, skipping");
                invalidFiles++;
                continue;
            }

            hashes[id] = string.Join("|", hashFile.Hashes.Select(h => $"{h.AlgorithmType}:{h.Value}"));
        }

        Logger.LogInfo($"{gamesPath}: {hashes.Count} hashes loaded");
        return new Buckets(hashes, emptyBuckets, invalidFiles);
    }
}
