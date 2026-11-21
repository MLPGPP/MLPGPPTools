# MLPGPPParser — Command Reference

A small .NET 9 console tool (Windows) used to:

- compute and check MD5 "hash files" for game folders (`hash` task),
- build the games JSON database from a Google Sheet (`parser` task).

This document describes **everything the current code does**, including its quirks. Bugs found in the current code are listed in the [Bugs](#bugs) section.

---

## Contents

1. [Requirements](#requirements)
2. [Building and running](#building-and-running)
3. [Command input modes](#command-input-modes)
4. [Global options](#global-options)
5. [Quick reference](#quick-reference)
6. [Task: `hash`](#task-hash)
7. [Task: `data`](#task-data)
8. [Task: `parser`](#task-parser)
9. [Hash file format](#hash-file-format)
10. [Config file](#config-file)
11. [Hard-coded paths](#hard-coded-paths)
12. [Logging](#logging)
13. [Inactive code (not reachable from the CLI)](#inactive-code-not-reachable-from-the-cli)
14. [Bugs](#bugs)
15. [Quoting tips for Windows](#quoting-tips-for-windows)
16. [Cheat sheet](#cheat-sheet)

---

## Requirements

- Windows (paths are Windows-style, e.g. `O:\MLGPP\g\`).
- .NET 9 SDK (`net9.0`). Verified with SDK `9.0.303`.
- NuGet package: `Google.Apis.Sheets.v4` 1.75.0.4178 (restored automatically).
- For the `parser` task only: a Google Sheets API key and a sheet ID in the config file.

---

## Building and running

Project file: `MLPGPPParser.csproj` (solution: `MLPGPPParser.sln`).

Build:

```powershell
dotnet build MLPGPPParser.csproj
```

Run through `dotnet run` (note the `--` separator; everything after it goes to the program):

```powershell
dotnet run --project MLPGPPParser.csproj -- run hash calc
```

Run the built output directly (after a build):

```powershell
dotnet bin\Debug\net9.0\MLPGPPParser.dll run hash calc
```

Run a single command in interactive mode from a pipe (useful for testing):

```powershell
echo hash calc | dotnet bin\Debug\net9.0\MLPGPPParser.dll
```

> The program always exits with code `0`, even when a command fails. Check the log file or console output for errors.

---

## Command input modes

The program has two ways to receive a command. The first word is the **task name**; the rest are that task's arguments.

### 1. Direct mode (`run`)

```text
MLPGPPParser run <task> [arguments...]
```

Example: `MLPGPPParser run hash calc base -f`

### 2. Interactive mode (no `run`)

If the first argument is not `run` (or there are no arguments at all), the program prints `Waiting for the command`, reads **one line** from stdin, splits it into arguments (quotes group text, see below) and runs it. It then exits. It does **not** loop.

```text
> MLPGPPParser
Waiting for the command
hash calc base -f
```

> In interactive mode, whitespace separates arguments, but text inside double quotes stays together. Put paths with spaces in quotes: `hash calc "D:\my games"`, or `--g_path="D:\my games"`. An unclosed `"` stops the command with an error and nothing runs. See [Quoting tips](#quoting-tips-for-windows).

### Dispatch rules

- The first argument after `run` (or the first word of the line) is matched against the registered task names: `hash`, `parser`.
- If no task matches, **nothing is printed** and the program exits with code `0`.
- The task receives the remaining words as its arguments.
- Registered task names: `hash`, `data`, `parser`.

---

## Global options

Global options can be used with **any** command. They can appear anywhere on the line (before or after the task name). They are removed before the task sees its arguments.

| Option | Meaning |
|---|---|
| `--g_path="<path>"` | Use this folder as the games folder instead of the default `O:\MLGPP\g\` |
| `--g_path=<path>` | Same, without quotes (use when the path has no spaces) |
| `--g_path <path>` | Same, as two separate words |

Examples:

```text
run hash calc --g_path="D:\games"
run hash find "C:\Downloads\game.exe" --g_path="D:\games"
run data pack --g_path="D:\games"
--g_path="D:\games" run hash coll
```

Rules:

- The games folder is chosen in this order: an explicit positional path (for example `run hash calc "D:\games"`) → `--g_path` → the default `O:\MLGPP\g\`. Writing `base` as the positional path means "no explicit path".
- `--g_path` is used by `hash calc`, `hash find`, `hash coll` and `data`. `parser` does not use the games folder, so the option has no effect on it.
- If the folder does not exist, the command logs `Games folder not found` and stops.
- `--g_path` with no value, or an empty value, logs an error and nothing runs.
- If the option is given twice, the last one wins.
- A trailing backslash is fine (`--g_path="D:\games\"`). Stray quotes caused by Windows argument parsing are removed automatically.

---

## Quick reference

| Command | What it does | Status |
|---|---|---|
| `hash calc [path\|base] [-f]` | Create `.<id>.in.hash` files for numbered game folders | Working (see [Bugs](#bugs)) |
| `hash find <file-or-folder>` | Compare files against all stored hash files | Working (uses games folder) |
| `hash coll` | Report game folders whose stored hashes are identical | Working (uses games folder) |
| `hash find_all` | Placeholder, does nothing | Stub |
| `hash find_g <other-games-folder>` | Compare the hashes of two games folders (A = `--g_path`/default, B = the argument) | Working |
| `data pack [path\|base]` | Pack each game's content into one zip | Working |
| `data unpack [path\|base]` | Extract each game's zip back into its folder | Working |
| `data remove <list-file> [-y]` | Delete all data inside the games listed in a text file (folders kept) | Working |
| `data shift [path\|base] [start_from=<integer>]` | Move data from higher ids into empty lower folders (closes gaps), optionally only from a given bucket up | Working |
| `data add_buckets=<number>` | Create missing bucket folders `0` .. `number-1` in the games folder | Working |
| `data start_from <integer> [--g_path=…]` | Renumber all bucket folders so the first one becomes `<integer>`, keeping order and spacing | Working |
| `parser` | Read Google Sheet, build games JSON database | Working (needs valid config) |
| `help` | Would show help | **Not registered**, does nothing |
| `folders` | Creates 10 000 empty folders in a hard-coded path | **Not registered**, cannot be run |

---

## Task: `hash`

Entry: `run hash <mode> ...`

If no mode is given, prints `error: no mode selected` and stops.

Any mode name not listed below (for example `hash foo`) is silently ignored.

### `hash calc [path|base] [-f]`

Creates a hash file inside every numbered game folder in the games folder.

| Part | Meaning |
|---|---|
| `path` | Games folder to scan. Omit it, or write `base`, to use `--g_path` or the default `O:\MLGPP\g\`. |
| `-f` | Force recalculation: delete existing `.<id>.in.hash` files and create them again. **Must be the 3rd word** (after `path`/`base`). |

Examples:

```text
run hash calc                      -> default folder, skip existing hashes
run hash calc base -f              -> default folder, recalculate everything
run hash calc "O:\MLGPP\other"     -> custom folder, skip existing hashes
run hash calc "O:\MLGPP\other" -f  -> custom folder, recalculate everything
```

How it works:

1. Lists the immediate subfolders of the games folder whose names are unsigned integers (`11`, `12`, ...). Other folders are ignored.
2. Works out the lowest and highest id.
3. For each id in that range, in parallel:
   - If `<id>\.<id>.in.hash` already exists:
     - with `-f`: delete it and recalculate;
     - without `-f`: print a warning and skip.
   - Lists the **top-level files** of `<id>\` (subfolders are not searched).
   - If there are none: print a warning and skip.
   - Otherwise computes the **MD5 of the first file** in that list and writes the hash file.

Output lines: `Hashed File: <id> name <filename>` for each new hash.

> **Bugs in `calc`** (verified by running it against test folders, details in [Bugs](#bugs)):
> - The **highest-numbered folder is never hashed**. The range end is exclusive (`Parallel.ForAsync(games[0], games[^1])`). With folders `11, 12, 13` only `11` and `12` are hashed.
> - A **gap in numbering crashes the command**. With folders `11, 13, 14`, id `12` is in the range but has no folder, so the run throws `DirectoryNotFoundException`.
> - `hash calc -f` **does not work as expected**. `-f` is read as the path (`Could not find a part of the path '...\-f'`). Use `hash calc base -f`.
> - "First file" is whatever `Directory.GetFiles` returns first. Its order is not guaranteed by the API. If a game folder has several top-level files, the stored hash may be for a different file than you expect.

### `hash find <file-or-folder>`

Checks one file, or every file under a folder (recursively), against all stored hash files.

- Required argument. Without it the program crashes with an unhandled `ArgumentException` (see [Bugs](#bugs)).
- Loads every `*.hash` file found one level below the games folder (`--g_path`, or the default `O:\MLGPP\g\`; see [Global options](#global-options)).
- For each file, computes its full MD5 and compares it with each stored hash.
- Prints the list of matches at the end:
  - `✅ <file> (<md5>) == <hash file path> (<md5>)` if a stored hash matches (the first match is used);
  - `❌ <file> (<md5>)` if none matches (logged as an error).
- Files are sorted by full path before checking.
- `.hash` files are skipped, both when they are found inside the checked folder and when one is given directly. The number skipped is printed (`Skipped N .hash file(s)`). They are not game data, so they never appear in the results.

Examples:

```text
run hash find "C:\Downloads\some_game.exe"
run hash find "C:\Downloads\unpacked_game"
```

### `hash coll`

Looks for **collisions between game folders**: two different stored hash files with the same MD5.

- Uses the games folder (`--g_path`, or the default `O:\MLGPP\g\`).
- Prints `Collision found between <pathA> and <pathB>` for each pair, then a summary.
- If none is found, prints `No collisions found!` as an error line.

### `hash find_all`

Recognised but empty. It does nothing and prints nothing.

### `hash find_g <other-games-folder>`

Compares the hashes of two games folders (two "g" databases with the same layout).

- **Folder A** is `--g_path`, or the default `O:\MLGPP\g\`.
- **Folder B** is the argument. It is required. A missing or extra argument prints a usage message.

How it works:

- Each numbered folder's first `.hash` file is read. Folders with no `.hash` file are **empty buckets**: they are counted and skipped.
- Invalid hash files are warned about and skipped (and counted).
- Hashes are compared by algorithm and value, so the same file gives the same hash in both folders.

Output, for every hash in A:

| Line | Meaning |
|---|---|
| `SAME    A:13 == B:13` | Same hash, same bucket id in both folders |
| `MOVED   A:11 == B:12` | Same hash in B, but under a different bucket id |
| `ONLY_A  A:14 (not in B)` | Hash in A that B does not have |

Then, for every hash in B that A does not have:

| Line | Meaning |
|---|---|
| `ONLY_B  B:14 (not in A)` | Hash in B that A does not have |

The last lines give a summary (`same`, `moved`, `only in A`, `only in B`), the number of empty buckets in each folder, and the number of invalid hash files.

Example:

```text
run hash find_g "D:\games_backup" --g_path="O:\MLGPP\g"
```

Notes:

- Only the hashes are compared. The games themselves are not read.
- If one bucket in A matches several buckets in B, all of them are listed (`MOVED   A:11 == B:12, B:30`).
- Hashes come from the `.hash` files, so a folder must be hashed (`hash calc`) before it can be compared.

---

## Task: `data`

Entry: `run data <mode> [path|base]`

| Mode | What it does |
|---|---|
| `pack` | Packs the content of every numbered game folder into one zip |
| `unpack` | Extracts the zip in every numbered game folder back into that folder |
| `remove` | Deletes all data inside the game folders listed in a text file (see [`data remove`](#data-remove)) |
| `shift` | Moves data from higher ids into empty lower folders (see [`data shift`](#data-shift)) |
| `add_buckets=<number>` | Creates missing bucket folders (see [`data add_buckets`](#data-add_buckets)) |

- For `pack`, `unpack` and `shift`, `path` is the games folder. Omit it, or write `base`, to use `--g_path` or the default `O:\MLGPP\g\`.
- For `remove`, the argument is the list file instead (`run data remove <list-file>`). The games folder comes from `--g_path` or the default.
- Only folders whose names are unsigned integers are processed, the same as `hash calc`.
- Any other mode prints `unknown data mode` and does nothing.

Examples:

```text
run data pack                      -> default folder
run data pack base                 -> default folder
run data pack "O:\MLGPP\other"     -> custom folder
run data unpack "O:\MLGPP\other"   -> custom folder
```

### `data pack`

For each game folder `<id>`:

1. **Find the content root.** Starting at `<id>`, descend while the folder has no files (`.hash` files don't count) and exactly one subfolder. The first folder that doesn't match is the content root. This removes pointless wrapper folders.
2. **Decide whether to pack.** Count the files under the content root, excluding `.hash` files. Pack only if there are **more than one**.
   - **Exactly one file** (for example `23\Game\game.exe`): the file is not zipped. It is moved up into `<id>\` (`23\game.exe`), and the wrapper folders around it are deleted. Empty subfolders in the wrapper are deleted too. If a file with the same name already exists in `<id>\`, nothing is changed and a warning is printed.
   - **No files**: nothing to do.
3. **Skip with a warning** if `<id>` already contains a `.zip`, if the zip name is already taken, or if a `.hash` file sits inside a subfolder of the content root.
4. **Create the zip** `<id>\<content root name>.zip`. It holds the content root's files and empty subfolders, with paths relative to the content root.
5. **Verify the zip.** It must contain the same files (same sizes) and the same empty folders. If not, the zip is deleted, the originals are kept, and an error is logged.
6. **Delete the originals**: the packed files, the subfolders of the content root, and the wrapper folders between `<id>` and the content root. `<id>` itself is never deleted.

`.hash` files are never packed and never moved. They stay in `<id>\`.

Example, from a nested layout:

```text
Before                                      After
11\                                         11\
  .11.in.hash                                 .11.in.hash
  a\b\c\1.0\                                  1.0.zip      (run.exe, blahblah.dll, content\x.dat)
    run.exe
    blahblah.dll
    content\x.dat
```

### `data unpack`

For each game folder `<id>`:

1. Find the `.zip` directly inside `<id>\`. If there is none, nothing happens. If there is more than one, skip with a warning.
2. Check every entry before extracting anything. If an entry would overwrite an existing file or folder, or would point outside `<id>\`, skip the whole folder with a warning. Nothing is changed.
3. Extract the entries directly into `<id>\`. Wrapper folders are **not** recreated.
4. Check each extracted file's size. If something fails, the files this run created are removed and the zip is kept.
5. Delete the zip. This happens only after everything was extracted.

### `data remove`

```text
run data remove <list-file> [-y]
```

Deletes **all** data inside the game folders listed in `<list-file>`. The game folders themselves are kept, empty.

The list file is a plain text file with one game id per row:

```text
11
12

14
```

- Blank lines and surrounding spaces are ignored.
- Every line must be an unsigned integer. If any line is not, the command stops **before deleting anything**.
- Duplicate ids are only processed once.
- An id with no matching folder in the games folder is skipped with a warning.
- Only folders whose names are numbers are considered.

What is deleted: every file and every subfolder inside the selected game folder, including `.hash` files. Read-only files are deleted too.

Confirmation:

- Without `-y`, the command lists the selected ids and asks `Delete the data in these game folders? [Y/N]`.
- With `-y`, there is no prompt. Use this in scripts.
- If input is piped (not a terminal) and `-y` is missing, the command refuses to run.

Links:

- A game folder that is a junction or symbolic link is **skipped**, so the target's data is never deleted.
- A junction or symbolic link inside a game folder is removed as a link only. Its target is not touched.

Examples:

```text
run data remove "C:\lists\old_games.txt"              -> asks for confirmation
run data remove "C:\lists\old_games.txt" -y           -> no prompt
run data remove "C:\lists\old_games.txt" --g_path="D:\games" -y
```

### `data shift`

```text
run data shift [path|base]
```

Closes the gaps left by `data remove` (or any other empty folders). Data is moved from higher ids into the lowest empty folder, so the data sits right after the lowest ids.

Example:

```text
Before                          After
11\ game.zip                    11\ game.zip
12\ (empty)                     12\ another_game.zip, .12.in.hash
13\ another_game.zip            13\ (empty)
    .13.in.hash
```

How it works:

- An **empty folder** is a hole. Each hole is filled by the next non-empty folder above it, so the order of the games is kept.
- Every entry (files and subfolders) of the source folder is moved. The source folder stays, empty.
- A hash file named `.<id>.in.hash` is renamed to match the new id (`.13.in.hash` → `.12.in.hash`). Other files keep their names.
- Junctions and symbolic links are never moved or used as targets. They are listed as a warning and skipped.
- **Nothing is deleted.** Shift only moves.

Starting from a bucket: `start_from=<integer>` makes shift work only on buckets with id `>=` that number. Buckets below it are never touched, including empty ones. Example:

```text
run data shift start_from=13
```

```text
Before                  After
11\ game.zip            11\ game.zip          (untouched)
12\ (empty)             12\ (empty)           (untouched, below the start)
13\ a.zip               13\ a.zip
14\ (empty)             14\ c.zip  (was 15)
15\ c.zip               15\ (empty)
```

- Without `start_from`, shift works on all buckets, the same as `start_from=0`.
- The value must be a whole number, 0 or more. Anything else (`start_from=-1`, `start_from=abc`, `start_from=`) prints a usage message and nothing runs.
- It can go with a path, in any order: `run data shift "D:\games" start_from=13`.
- If a move fails (for example a file is in use), the moves already made in that step are reversed and the command stops. Fix the problem and run it again.
- Running it again when there are no gaps does nothing.

**Game ids change.** Every game moved to a lower id gets a new folder name. Anything that refers to ids outside this tool (for example the games JSON from `parser`) must be regenerated after a shift.

### `data add_buckets`

```text
run data add_buckets=<number>
run data add_buckets <number>        (same thing, two words)
```

Makes sure the bucket folders `0` to `<number>-1` exist in the games folder. Missing ones are created as empty folders. Existing folders are not changed.

Examples:

```text
run data add_buckets=10000                  -> folders 0..9999 (same layout as the old FoldersManager helper)
run data add_buckets=50 --g_path="D:\games" -> folders 0..49 in D:\games
```

Use it to:

- add more buckets: raise the number and run again;
- repair a games folder where bucket folders were deleted: run it with the same number as before.

Rules:

- `<number>` must be a whole number, 1 or more.
- Folders are numbered from `0`, so `add_buckets=N` creates `N` buckets: `0` through `N-1`.
- Existing folders are never touched. Their contents are not changed, and no hash files are created.
- A file with the same name as a missing bucket (for example a file called `4`) blocks that bucket. The command reports it and does not delete or overwrite the file. Other buckets are still created, and the command returns an error.
- Uses `--g_path` or the default `O:\MLGPP\g\`. It has no path argument of its own.

### `data start_from`

```text
run data start_from <integer>
run data start_from=<integer>        (same thing)
```

Renumbers **all** bucket folders in the games folder. The first bucket (the lowest id) becomes `<integer>`, and every other bucket keeps its distance from it. Only the folder names change. The order of the buckets is kept.

Example, `data start_from 10`:

```text
Before     After
0          10
1          11
2          12
```

With gaps, the spacing is kept rather than closed up:

```text
Before     After  (start_from 10)
3          10
5          12
8          15
```

Rules:

- Hash files are renamed with their folder: `.0.in.hash` → `.10.in.hash`. Their contents are not changed.
- Data inside the folders is not touched. Nothing is deleted or copied.
- The renames go through temporary names, so the new ids can overlap the old ones (`start_from 2` on buckets `0 1 2` gives `2 3 4`).
- Any failure reverses every rename already made in that run, so the games folder ends up as it was. Check the log if the undo itself fails.
- If a file has the same name as a new bucket id (for example a file called `11`), the command stops before changing anything.
- Junctions and symbolic links in the games folder also stop the command before anything changes.
- A command that finds the buckets already starting at `<integer>` does nothing.
- It uses `--g_path` or the default `O:\MLGPP\g\`. It has no path argument of its own.
- **Bucket ids change.** Anything outside this tool that stores ids (for example the games JSON from `parser`) must be regenerated afterwards.

Usage errors (a missing, negative or non-numeric value, or an extra argument) print a message and nothing runs.

### Notes

- Packing deletes the original files after verifying the zip. **Test on a copy of a game folder first.**
- Pack and unpack skip any game folder that contains a junction or symbolic link (they would read or write through the link). A warning is printed.
- Packing changes what is in the game folder. The stored `.hash` file is not updated, so `hash find` still matches the original file content. If you run `hash calc -f` after packing, the hash will be computed from the zip instead.
- Unpack restores the content into `<id>\` without the old wrapper folders. The wrapper folder names are not stored in the zip.
- Running `data pack` again on a folder that is already packed prints a warning and skips it.

---

## Task: `parser`

Entry: `run parser`

Arguments are ignored.

Steps:

1. Reads the config from `<current directory>\Resources\config.json`.
   - **Important:** it uses the *current working directory*, not the location of the `.exe`. Run it from the folder that contains `Resources\`.
   - If the file is missing, a template config is created at that path and the command stops with an error. Fill it in and run again.
   - If the file contains a JSON `null`, the template is written over it and the command stops with an error. If the file is malformed JSON, the command crashes with an unhandled exception and the file is left alone. Back the file up before editing.
2. Loads the screenshots database from `localScreenshotsDBPath`.
3. Downloads the sheet range `dataRange` from `sheetID` using `APIToken` as a Google API key.
4. Parses the rows into games, formats them, and writes JSON to `gamesJsonPath` (indented).

Screenshot moving into `siteLocalPath` is commented out in the code and does not run.

Output: prints `parsed <N> games`, `Generating Json`, `Saving Json`.

---

## Hash file format

Each game folder can contain one hash file:

- **Location:** `<games folder>\<id>\.<id>.in.hash`, e.g. `O:\MLGPP\g\12\.12.in.hash`
- **Encoding:** UTF-8 text (written by `StreamWriter`)
- **Content:**

  ```text
  Memory Leak Slopity Check! 67 /)
  MD5 <32-character uppercase hex>
  ```

- The first line is a fixed marker. A file without it is treated as invalid and ignored.
- The line after it is `<ALGORITHM> <hex value>`. Only `MD5` is written today.

Folder `<id>` is the number in the folder name. The hash file name always uses the same id.

---

## Config file

Path: `<current directory>\Resources\config.json`

Created automatically with placeholder values by the `parser` task if missing. Shape (field names are matched case-insensitively):

```json
{
  "APIToken": "idk",
  "sheetID": "idk",
  "dataRange": "source!A3:X",
  "localScreenshotsDBPath": "O:/MLGPP/s",
  "gamesJsonPath": "O:/MLGPP/Pony.Games//data/games.json",
  "siteLocalPath": "O:\\MLGPP\\Pony.Games",
  "logFolderPath": "O:\\MLGPP\\logs"
}
```

| Field | Used by | Purpose |
|---|---|---|
| `APIToken` | `parser` | Google Sheets API key |
| `sheetID` | `parser` | Spreadsheet ID |
| `dataRange` | `parser` | Range to read, e.g. `source!A3:X` |
| `localScreenshotsDBPath` | `parser` | Screenshots database folder |
| `gamesJsonPath` | `parser` | Output JSON file |
| `siteLocalPath` | none (screenshot moving is disabled) | Target folder for screenshots |
| `logFolderPath` | **none** (see [Bugs](#bugs)) | Documented intent: log folder |

---

## Hard-coded paths

| Path | Used by | Notes |
|---|---|---|
| `O:\MLGPP\g\` | Default games folder for `hash calc`, `hash find`, `hash coll`, `data` | Overridden by `--g_path` or by an explicit path argument. |
| `O:\MLGPP\logs` | Logger (all runs) | Log folder. Config `logFolderPath` is ignored. |
| `<cwd>\Resources\config.json` | `parser` | Config file |
| `O:\MLGPP\Pony.Games\s` | `folders` (inactive) | Target of the unused folder generator |

---

## Logging

- Every run creates a new log file: `O:\MLGPP\logs\yyyy-MM-dd_HH-mm-ss.log`.
- Messages are written to the console and to the log file. Lines in the file get a timestamp prefix, except the progress lines (`Executing task for ...`), which are written without one.
- Colours on the console: green = info, yellow = warning, red = error.

---

## Inactive code (not reachable from the CLI)

These exist in the source but are not usable from the command line:

- **`help`** (`HelpRunner`): not in the runner list, and its `Run` body is empty. Typing `help` does nothing.
- **`folders`** (`FoldersManagerRunner`): not in the runner list. It creates 10 000 empty folders `0`..`9999` in `O:\MLGPP\Pony.Games\s`. It is a one-off helper and is commented out in `Program.cs`.

To make either one reachable, add it to the `runners` list in `Program.cs`.

---

## Bugs

Each bug states how it was found:

- **Verified**: reproduced by running the built program against throwaway test folders (not the real games folder).
- **Code reading**: found in the source, not reproduced by running.

### Bug 1: `hash calc` never hashes the highest-numbered folder *(verified)*

- **Repro:** folders `11`, `12`, `13`, each with one file. Run `hash calc "<path>"`.
- **Actual:** hash files are created for `11` and `12` only. `13` gets none.
- **Cause:** `Parallel.ForAsync(games[0], games[^1], ...)` treats the upper bound as exclusive (confirmed in the framework XML docs: `toExclusive`).
- **Expected:** all three folders are hashed.

### Bug 2: `hash calc` crashes when the numbering has a gap *(verified)*

- **Repro:** folders `11`, `13`, `14`. Run `hash calc "<path>"`.
- **Actual:** id `12` is inside the range but has no folder. `Directory.GetFiles` on the missing folder throws, and the run ends with an unhandled exception.
- **Expected:** missing ids are skipped.

### Bug 3: `hash calc -f` does not recognise the flag *(verified)*

- **Repro:** `hash calc -f`.
- **Actual:** `-f` is taken as the games folder path, giving `Could not find a part of the path '...\-f'`.
- **Expected:** `-f` is the force flag. Workaround: `hash calc base -f`.

### Bug 4: `hash find` without an argument crashes *(code reading)*

- **Repro:** `run hash find`.
- **Actual:** unhandled `ArgumentException` ("no file to check").
- **Expected:** a usage error message.

### Bug 5: `hash find` and `hash coll` ignore any path *(code reading)* — **fixed**

- **Repro:** change the games folder (e.g. to `D:\games`) and run `hash find` or `hash coll`.
- **Was:** both always read `O:\MLGPP\g\`.
- **Now:** both use `--g_path` (see [Global options](#global-options)), the same as `hash calc`. Verified with a test folder.

### Bug 6: no task name crashes the program *(verified)* — **fixed**

- **Repro:** `run` on its own, or an empty line in interactive mode.
- **Was:** unhandled `IndexOutOfRangeException`.
- **Now:** prints `error: no task given`.

### Bug 7: end of input in interactive mode crashes *(code reading)*

- **Repro:** interactive mode with stdin closed (no line at all).
- **Actual:** `Console.ReadLine()` returns `null`, and the code dereferences it (`Program.cs`, the `Split` call), causing a null reference exception.
- **Expected:** a clean exit.

### Bug 8: unknown task names are silently ignored *(verified)*

- **Repro:** `nope` in interactive mode.
- **Actual:** prints nothing after `Waiting for the command`.
- **Expected:** an error such as `unknown task 'nope'`.

### Bug 9: the exit code is always `0` *(verified)*

- **Repro:** `echo nope | MLPGPPParser.dll`, then check the exit code.
- **Actual:** exit code `0`, including after errors such as the ones above.
- **Expected:** a non-zero exit code on failure, so scripts can detect it.

### Bug 10: config `logFolderPath` is not used *(verified by search)*

- **Repro:** change `logFolderPath` in `config.json`.
- **Actual:** logs still go to `O:\MLGPP\logs`. `Logger.Initialize` is called with a hard-coded path in `Program.cs`, and nothing reads the config field.
- **Expected:** logs go to the configured folder.

### Bug 11: a `null` config is overwritten, malformed JSON crashes *(code reading)*

- **Repro:** put `null` into `Resources\config.json`, then run `parser`.
- **Actual:** the file is replaced with the placeholder template. Malformed JSON instead crashes the program and leaves the file alone.
- **Expected:** the user's file is not overwritten; both cases report a clear error.

### Bug 12: the stored hash may belong to an unexpected file *(code reading)*

- **Repro:** a game folder with several top-level files.
- **Actual:** the hash is computed for whichever file `Directory.GetFiles` returns first. The API does not guarantee that order.
- **Expected:** a defined file (e.g. the main executable, or sorted by name).

---

## Quoting tips for Windows

- Quote paths that contain spaces: `"O:\MLGPP\other folder"`. This works in both direct and interactive mode.
- In interactive mode, a quote can sit inside an argument: `--g_path="D:\my games"` is one argument. Backslashes are never escapes, so Windows paths are typed as they are.
- An unclosed `"` in interactive mode stops the command with `unclosed " in command`.
- **Do not end a quoted path with a backslash.** `"O:\MLGPP\g\"` is parsed incorrectly by Windows argument parsing (the `\"` is read as an escaped quote). Write `"O:\MLGPP\g"` instead. The program adds the trailing `\` itself where it needs it.
- In PowerShell, put `--` before arguments when calling `dotnet run`, as shown above.

---

## Cheat sheet

```text
# Hash numbered folders in the default games folder (new hashes only)
run hash calc

# Recalculate every hash in the default games folder
run hash calc base -f

# Hash folders in another games folder
run hash calc "O:\MLGPP\other"

# Recalculate in another games folder
run hash calc "O:\MLGPP\other" -f

# Check one file against all stored hashes
run hash find "C:\path\to\file.exe"

# Check a whole folder (recursive)
run hash find "C:\path\to\folder"

# Find identical game folders
run hash coll

# Compare two games folders by hash (A = --g_path or default, B = argument)
run hash find_g "D:\games_backup"
run hash find_g "D:\games_backup" --g_path="O:\MLGPP\g"

# Use another games folder for any command (global option)
run hash calc --g_path="D:\games"
run data pack --g_path="D:\games"
run hash coll --g_path="D:\games"

# Pack each game's content into one zip (default folder)
run data pack

# Unpack each game's zip back into its folder
run data unpack "O:\MLGPP\other"

# Delete all data in the games listed in a file (asks for confirmation)
run data remove "C:\lists\old_games.txt"

# Same, without the prompt (for scripts)
run data remove "C:\lists\old_games.txt" -y

# Move data down into empty folders after a removal (changes game ids!)
run data shift

# Same, but leave buckets 0..12 alone and only shift from bucket 13 up
run data shift start_from=13

# Create bucket folders 0..9999 (only the missing ones)
run data add_buckets=10000

# Renumber all buckets so the first one becomes 10 (0->10, 1->11, ...)
run data start_from 10 --g_path="D:\games"

# Recreate deleted bucket folders in another games folder
run data add_buckets=50 --g_path="D:\games"

# Build the games JSON (run from the folder that has Resources\config.json)
run parser
```
