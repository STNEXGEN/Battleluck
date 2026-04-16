// GenerateREADME.cs — BattleLuck
// Scans Commands/*.cs for [Command(...)] attributes and FlowController.cs for
// flow action cases, then rewrites the ## Commands and ## Flow Actions sections
// of README.md in-place.
//
// Run manually:
//   dotnet script GenerateREADME.cs -- Commands README.md
// Or hook into a post-build target in BattleLuck.csproj.

using System.Text;
using System.Text.RegularExpressions;

internal static class GenerateREADME
{
    // ── Config ───────────────────────────────────────────────────────────────

    static string CommandsPath { get; set; } = string.Empty;
    static string ReadMePath   { get; set; } = string.Empty;

    // Section headers as they appear (or should appear) in README.md
    const string COMMANDS_HEADER     = "## Commands";
    const string FLOW_ACTIONS_HEADER = "## Flow Actions";

    // ── Regex ────────────────────────────────────────────────────────────────

    // Matches [Command("name"  or  name: "name"  and optional description/adminOnly/usage)
    static readonly Regex _commandRegex = new(
        @"\[Command\((?<args>.*?)\)\]",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // Matches case "some.action": inside a switch statement
    static readonly Regex _caseRegex = new(
        @"case\s+""(?<action>[a-z0-9_.]+)""\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Key-value pairs inside attribute arg list:  key: "value"  or  key: true
    static readonly Regex _argPairRegex = new(
        @"\b(?<key>\w+)\s*:\s*(?<value>""[^""]*""|[^,\)\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Entry point ──────────────────────────────────────────────────────────

    public static void Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Console.WriteLine("GenerateREADME skipped during GitHub Actions build.");
            return;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: GenerateREADME <CommandsPath> <ReadMePath>");
            return;
        }

        CommandsPath = args[0];
        ReadMePath   = args[1];

        try
        {
            Generate();
            Console.WriteLine("README generated successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating README: {ex.Message}");
        }
    }

    // ── Generation ───────────────────────────────────────────────────────────

    static void Generate()
    {
        var groups       = CollectCommands();
        var flowActions  = CollectFlowActions();

        var commandsSection    = BuildCommandsSection(groups);
        var flowActionsSection = BuildFlowActionsSection(flowActions);

        UpdateReadme(commandsSection, flowActionsSection);
    }

    // ── Command collection ───────────────────────────────────────────────────

    // Returns: dict of groupLabel → list of (name, adminOnly, description, usageHint)
    static Dictionary<string, List<(string name, bool adminOnly, string description, string usageHint)>>
        CollectCommands()
    {
        var groups = new Dictionary<string, List<(string, bool, string, string)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(CommandsPath, "*.cs").OrderBy(f => f))
        {
            string content = File.ReadAllText(file);

            // Derive group label from filename: "AdminCommands.cs" → "Admin"
            string groupLabel = DeriveGroupLabel(Path.GetFileNameWithoutExtension(file));
            if (!groups.TryGetValue(groupLabel, out var list))
            {
                list = [];
                groups[groupLabel] = list;
            }

            foreach (Match m in _commandRegex.Matches(content))
            {
                string attrArgs   = m.Groups["args"].Value;
                string name       = GetStringArg(attrArgs, "name");
                bool   adminOnly  = GetBoolArg(attrArgs, "adminOnly");
                string desc       = GetStringArg(attrArgs, "description");
                string usageHint  = GetStringArg(attrArgs, "usage");

                // Some descriptions embed "Usage: .cmd <params>" — extract it
                if (string.IsNullOrEmpty(usageHint))
                {
                    var usageInDesc = Regex.Match(desc, @"[Uu]sage:\s*(?<u>\S.*?)$");
                    if (usageInDesc.Success)
                    {
                        usageHint = usageInDesc.Groups["u"].Value.Trim();
                        desc = desc[..usageInDesc.Index].TrimEnd(' ', '.', ',');
                    }
                }

                if (!string.IsNullOrEmpty(name))
                    list.Add((name, adminOnly, desc, usageHint));
            }

            // Sort each group alphabetically (Item1 = name)
            groups[groupLabel] = [..groups[groupLabel].OrderBy(c => c.Item1, StringComparer.OrdinalIgnoreCase)];
        }

        return groups;
    }

    // ── Flow action collection ────────────────────────────────────────────────

    // Scans Services/FlowController.cs for  case "action.name":  entries
    static List<(string action, string comment)> CollectFlowActions()
    {
        var result = new List<(string, string)>();

        // Find FlowController.cs relative to the Commands folder
        string servicesDir = Path.Combine(Path.GetDirectoryName(CommandsPath)!, "Services");
        string flowFile    = Path.Combine(servicesDir, "FlowController.cs");

        if (!File.Exists(flowFile))
            return result;

        string content  = File.ReadAllText(flowFile);
        var    seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Walk line-by-line so we can grab the trailing // comment on the same or next line
        string[] lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var cm = _caseRegex.Match(lines[i]);
            if (!cm.Success) continue;

            string action = cm.Groups["action"].Value;

            // Skip internal/helper cases and duplicates
            if (action.Equals("default", StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(action)) continue;

            // Try to grab inline comment  // ...  or description from surrounding context
            string comment = string.Empty;
            var inlineComment = Regex.Match(lines[i], @"//\s*(?<c>.+)$");
            if (inlineComment.Success)
                comment = inlineComment.Groups["c"].Value.Trim();

            result.Add((action, comment));
        }

        return result;
    }

    // ── Section builders ─────────────────────────────────────────────────────

    static string BuildCommandsSection(
        Dictionary<string, List<(string name, bool adminOnly, string description, string usageHint)>> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine(COMMANDS_HEADER).AppendLine();

        sb.AppendLine("BattleLuck commands are registered through VampireCommandFramework.");
        sb.AppendLine("🔒 = admin only.").AppendLine();

        foreach (var groupLabel in groups.Keys.OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
        {
            var list = groups[groupLabel];
            if (list.Count == 0) continue;

            sb.AppendLine($"### {groupLabel} Commands").AppendLine();
            sb.AppendLine("| Command | Description |");
            sb.AppendLine("| --- | --- |");

            foreach (var (name, adminOnly, description, usageHint) in list)
            {
                string lock_  = adminOnly ? " 🔒" : string.Empty;
                string cmdStr = string.IsNullOrEmpty(usageHint)
                    ? $"`.{name}`"
                    : $"`.{usageHint}`";
                string desc   = EscapeMarkdown(description);
                sb.AppendLine($"| {cmdStr}{lock_} | {desc} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    static string BuildFlowActionsSection(List<(string action, string comment)> actions)
    {
        var sb = new StringBuilder();
        sb.AppendLine(FLOW_ACTIONS_HEADER).AppendLine();
        sb.AppendLine("Flow actions are strings inside `flow_enter.json` / `flow_exit.json`.");
        sb.AppendLine("Syntax: `actionName:key=value|key2=value2`").AppendLine();
        sb.AppendLine("| Action | Notes |");
        sb.AppendLine("| --- | --- |");

        foreach (var (action, comment) in actions.OrderBy(a => a.action))
            sb.AppendLine($"| `{action}` | {EscapeMarkdown(comment)} |");

        sb.AppendLine();
        return sb.ToString();
    }

    // ── README updater ───────────────────────────────────────────────────────

    static void UpdateReadme(string commandsSection, string flowActionsSection)
    {
        bool inCommands    = false;
        bool inFlowActions = false;
        bool cmdReplaced   = false;
        bool flowReplaced  = false;

        var newContent = new List<string>();

        foreach (string line in File.ReadLines(ReadMePath))
        {
            string trimmed = line.Trim();

            // Hit commands header
            if (trimmed.Equals(COMMANDS_HEADER, StringComparison.OrdinalIgnoreCase))
            {
                inCommands  = true;
                cmdReplaced = true;
                newContent.Add(commandsSection);
                continue;
            }

            // Hit flow actions header
            if (trimmed.Equals(FLOW_ACTIONS_HEADER, StringComparison.OrdinalIgnoreCase))
            {
                inFlowActions = true;
                flowReplaced  = true;
                newContent.Add(flowActionsSection);
                continue;
            }

            // Exit replaced sections on next ## heading
            if (inCommands && trimmed.StartsWith("## ") &&
                !trimmed.Equals(COMMANDS_HEADER, StringComparison.OrdinalIgnoreCase))
                inCommands = false;

            if (inFlowActions && trimmed.StartsWith("## ") &&
                !trimmed.Equals(FLOW_ACTIONS_HEADER, StringComparison.OrdinalIgnoreCase))
                inFlowActions = false;

            if (!inCommands && !inFlowActions)
                newContent.Add(line);
        }

        // Flush if file ended while inside a section
        if (inFlowActions)
        {
            newContent.Add(flowActionsSection);
            inFlowActions = false;
        }

        // Append sections that weren't found
        if (!cmdReplaced)
        {
            newContent.Add(commandsSection);
        }

        if (!flowReplaced)
        {
            newContent.Add(flowActionsSection);
        }

        File.WriteAllLines(ReadMePath, newContent);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static string DeriveGroupLabel(string fileNameWithoutExtension)
    {
        // "AdminCommands" → "Admin", "PlayerCommands" → "Player", etc.
        const string suffix = "Commands";
        if (fileNameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return fileNameWithoutExtension[..^suffix.Length];

        return fileNameWithoutExtension;
    }

    static string GetStringArg(string args, string key)
    {
        // positional first arg (no key):  [Command("name", ...)]
        if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            var pos = Regex.Match(args, @"^\s*""(?<v>[^""]+)""");
            if (pos.Success) return pos.Groups["v"].Value;
        }

        foreach (Match m in _argPairRegex.Matches(args))
        {
            if (!string.Equals(m.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
                continue;

            string raw = m.Groups["value"].Value.Trim();
            return raw.Length > 1 && raw[0] == '"' && raw[^1] == '"'
                ? raw[1..^1]
                : raw;
        }

        return string.Empty;
    }

    static bool GetBoolArg(string args, string key)
    {
        foreach (Match m in _argPairRegex.Matches(args))
            if (string.Equals(m.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
                return bool.TryParse(m.Groups["value"].Value.Trim(), out bool b) && b;

        return false;
    }

    static string EscapeMarkdown(string s) =>
        s.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
