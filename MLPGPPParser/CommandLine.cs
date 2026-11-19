using System.Text;

namespace MLPGPPParser;

/// <summary>
/// Splits a command typed in interactive mode into arguments.
///
/// Rules:
///  - Whitespace separates arguments, except inside double quotes.
///  - Double quotes group text and are removed: <c>hash calc "D:\my games"</c> gives two arguments.
///  - A quote can be part of an argument: <c>--g_path="D:\my games"</c> gives one argument, <c>--g_path=D:\my games</c>.
///  - Backslashes are plain characters, never escapes, so Windows paths work as typed.
///  - An empty pair of quotes (<c>""</c>) is an empty argument.
/// </summary>
public static class CommandLine {

    /// <returns>The arguments, or null (after logging an error) if a quote is not closed.</returns>
    public static string[]? Split(string line) {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasArgument = false;

        foreach (var c in line) {
            if (c == '"') {
                inQuotes = !inQuotes;
                hasArgument = true;
            } else if (char.IsWhiteSpace(c) && inQuotes == false) {
                if (hasArgument) {
                    args.Add(current.ToString());
                    current.Clear();
                    hasArgument = false;
                }
            } else {
                current.Append(c);
                hasArgument = true;
            }
        }

        if (inQuotes) {
            Logger.LogError("unclosed \" in command, nothing was run");
            return null;
        }

        if (hasArgument) {
            args.Add(current.ToString());
        }

        return args.ToArray();
    }
}
