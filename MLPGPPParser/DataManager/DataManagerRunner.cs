namespace MLPGPPParser.DataManager;

public class DataManagerRunner : ITaskRunner {

    public string TaskName => "data";

    // run data pack [path|base]
    // run data unpack [path|base]
    // run data remove <list-file> [-y]
    // run data shift [path|base] [start_from=<integer>]
    // run data add_buckets=<number>
    // run data start_from <integer>   (also: data start_from=<integer>)
    public Task<int> Run(string[] args) {
        if (args.Length < 1) {
            Logger.LogError("no mode selected (pack | unpack | remove | shift | add_buckets=<number> | start_from <integer>)");
            return Task.FromResult(-1);
        }

        var mode = args[0];

        if (mode == "add_buckets" || mode.StartsWith("add_buckets=", StringComparison.Ordinal)) {
            return AddBuckets(args);
        }

        if (mode == "start_from" || mode.StartsWith("start_from=", StringComparison.Ordinal)) {
            return StartFrom(args);
        }

        switch (mode) {
            case "pack": {
                var gamesPath = GlobalOptions.ResolveGamesFolder(args.Length > 1 && args[1] != "base" ? args[1] : null);
                if (gamesPath == null)
                    return Task.FromResult(-1);

                DataPacker.PackAll(gamesPath);
                return Task.FromResult(0);
            }
            case "unpack": {
                var gamesPath = GlobalOptions.ResolveGamesFolder(args.Length > 1 && args[1] != "base" ? args[1] : null);
                if (gamesPath == null)
                    return Task.FromResult(-1);

                DataPacker.UnpackAll(gamesPath);
                return Task.FromResult(0);
            }
            case "remove": {
                if (args.Length < 2) {
                    Logger.LogError("usage: data remove <list-file> [-y]");
                    return Task.FromResult(-1);
                }

                var extra = args.Skip(2).Where(arg => arg != "-y").ToArray();
                if (extra.Length > 0) {
                    Logger.LogError($"unknown argument '{extra[0]}' (usage: data remove <list-file> [-y])");
                    return Task.FromResult(-1);
                }

                var gamesPath = GlobalOptions.ResolveGamesFolder();
                if (gamesPath == null)
                    return Task.FromResult(-1);

                var skipConfirmation = args.Skip(2).Contains("-y");
                return Task.FromResult(DataRemover.Remove(args[1], gamesPath, skipConfirmation));
            }
            case "shift": {
                // data shift [path|base] [start_from=<integer>]   (both optional, any order)
                const string startFromPrefix = "start_from=";
                uint startFrom = 0;
                string? explicitPath = null;

                foreach (var arg in args.Skip(1)) {
                    if (arg.StartsWith(startFromPrefix, StringComparison.Ordinal)) {
                        if (uint.TryParse(arg[startFromPrefix.Length..], out startFrom) == false) {
                            Logger.LogError($"'{arg}' is not valid. usage: data shift [path|base] [start_from=<integer>]");
                            return Task.FromResult(-1);
                        }
                    } else if (arg == "base") {
                        // default folder, same as leaving the path out
                    } else if (explicitPath == null) {
                        explicitPath = arg;
                    } else {
                        Logger.LogError($"unknown argument '{arg}'. usage: data shift [path|base] [start_from=<integer>]");
                        return Task.FromResult(-1);
                    }
                }

                var gamesPath = GlobalOptions.ResolveGamesFolder(explicitPath);
                if (gamesPath == null)
                    return Task.FromResult(-1);

                return Task.FromResult(DataShifter.Shift(gamesPath, startFrom));
            }
            default:
                Logger.LogError($"unknown data mode '{mode}' (expected pack | unpack | remove | shift | add_buckets=<number> | start_from <integer>)");
                return Task.FromResult(-1);
        }
    }

    // data add_buckets=<number>   (also accepted: data add_buckets <number>)
    private static Task<int> AddBuckets(string[] args) {
        const string usage = "usage: data add_buckets=<number>  (number of buckets, 1 or more)";

        string? countText;
        int extraCount;
        if (args[0] == "add_buckets") {
            countText = args.Length > 1 ? args[1] : null;
            extraCount = Math.Max(0, args.Length - 2);
        } else {
            countText = args[0]["add_buckets=".Length..];
            extraCount = args.Length - 1;
        }

        if (extraCount > 0) {
            Logger.LogError($"unknown argument '{args[^1]}'. {usage}");
            return Task.FromResult(-1);
        }

        if (int.TryParse(countText, out var count) == false || count <= 0) {
            Logger.LogError($"'{countText}' is not a valid bucket count. {usage}");
            return Task.FromResult(-1);
        }

        var gamesPath = GlobalOptions.ResolveGamesFolder();
        if (gamesPath == null)
            return Task.FromResult(-1);

        return Task.FromResult(BucketCreator.AddBuckets(gamesPath, count));
    }

    // data start_from <integer>   (also accepted: data start_from=<integer>)
    private static Task<int> StartFrom(string[] args) {
        const string usage = "usage: data start_from <integer>  (folder and --g_path are used, e.g. data start_from 10 --g_path=\"D:\\games\")";

        string? numberText;
        int extraCount;
        if (args[0] == "start_from") {
            numberText = args.Length > 1 ? args[1] : null;
            extraCount = Math.Max(0, args.Length - 2);
        } else {
            numberText = args[0]["start_from=".Length..];
            extraCount = args.Length - 1;
        }

        if (extraCount > 0) {
            Logger.LogError($"unknown argument '{args[^1]}'. {usage}");
            return Task.FromResult(-1);
        }

        if (uint.TryParse(numberText, out var start) == false) {
            Logger.LogError($"'{numberText}' is not a valid start number. {usage}");
            return Task.FromResult(-1);
        }

        var gamesPath = GlobalOptions.ResolveGamesFolder();
        if (gamesPath == null)
            return Task.FromResult(-1);

        return Task.FromResult(BucketRenamer.Remap(gamesPath, start));
    }
}
