using System.Security.Authentication;

namespace MLPGPPParser;

public static class HashPrinter {
    public static async Task PrintHashes(string gamesPath) {

        var directories = Directory.GetDirectories(gamesPath);
        
        //sort directories by name
        Array.Sort(directories, (x, y) => {
            var xName = x.Split("\\").Last();
            var yName = y.Split("\\").Last();
            if (uint.TryParse(xName, out var xId) && uint.TryParse(yName, out var yId)) {
                return xId.CompareTo(yId);
            }
            return string.Compare(xName, yName, StringComparison.Ordinal);
        } );

        foreach (var directory in directories) {
            var hashFile = await HashFile.TryLoadFromDirectory(directory);
            if (hashFile == null) {
                Logger.LogError($"Failed to load hash file for directory: {directory}");
                continue;
            }
            
            var hash = hashFile.GetHash(HashAlgorithmType.Md5);
            if (uint.TryParse((directory.Split("\\").Last()), out var dirID) == false) {
                Logger.LogError($"Failed to parse directory ID for directory: {directory}");
                continue;
            }
            
            Logger.LogInfo($"{dirID}\t{hash.Value}");
        }

        Directory.EnumerateDirectories(gamesPath).ToList().ForEach(async dir => {
            var hashFile = await HashFile.TryLoadFromDirectory(dir);
            if(hashFile is null) {
                Logger.LogInfo($"No .hash file found in directory {dir}");
                return;
            }
            
            Logger.LogInfo($"Hashes for {dir}:");
            foreach (var hash in hashFile.Hashes) {
                Logger.LogInfo($"{hash.AlgorithmType}: {hash.Value}");
            }
        });
    }
}
