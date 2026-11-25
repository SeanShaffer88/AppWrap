namespace AppWrap.Core.PortableProfiles;

/// <summary>A non-fatal parse diagnostic collected while parsing an INI document.</summary>
public sealed record IniParseIssue(int Line, string Message);

/// <summary>Base node of the lossless ordered INI AST. Each node keeps its raw text (without its line terminator) and its 1-based line span.</summary>
public abstract class IniNode(string rawText, int line)
{
    public string RawText { get; internal set; } = rawText;
    public int LineStart { get; internal set; } = line;
    public int LineEnd { get; internal set; } = line;
    internal string Terminator { get; set; } = "";
}

public sealed class IniCommentNode(string rawText, int line) : IniNode(rawText, line);
public sealed class IniBlankLineNode(string rawText, int line) : IniNode(rawText, line);
public sealed class IniRawLineNode(string rawText, int line) : IniNode(rawText, line);

public sealed class IniSectionNode(string rawText, int line, string name) : IniNode(rawText, line)
{
    public string Name { get; } = name;
}

public sealed class IniEntryNode(string rawText, int line, string? section, string rawKey, string key, string rawValue) : IniNode(rawText, line)
{
    public string? Section { get; internal set; } = section;
    /// <summary>The raw key text exactly as it appeared before '=' (preserves spelling/spacing).</summary>
    public string RawKey { get; } = rawKey;
    public string Key { get; } = key;
    public string RawValue { get; internal set; } = rawValue;
    /// <summary>Typed view: trimmed value with one pair of outer double quotes stripped.</summary>
    public string Value => StripOuterQuotes(RawValue.Trim());

    internal static string StripOuterQuotes(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}

/// <summary>
/// Lossless ordered INI document. <see cref="Serialize"/> reproduces the original text
/// byte-for-byte when the document is unedited. Line-ending handling: each node's original
/// terminator is retained, so CRLF, LF and mixed endings round-trip exactly; nodes added by
/// edits use the document's dominant newline style (first terminator seen, else CRLF).
/// The parser never throws on content; problems are collected in <see cref="ParseIssues"/>.
/// It throws <see cref="UserInputException"/> only for size bounds (&gt; 1 MB or &gt; 20,000 lines).
/// </summary>
public sealed class IniDocument
{
    public const int MaxBytes = 1_000_000;
    public const int MaxLines = 20_000;

    private readonly List<IniNode> _nodes = [];
    private readonly List<IniParseIssue> _issues = [];
    private string _newLine = "\r\n";

    private IniDocument() { }

    public IReadOnlyList<IniNode> Nodes => _nodes;
    public IReadOnlyList<IniParseIssue> ParseIssues => _issues;
    public string NewLine => _newLine;

    public static IniDocument Empty => Parse("");

    public static IniDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxBytes)
            throw new UserInputException("INI text is too large (up to 1 MB).");
        var doc = new IniDocument();
        if (text.Length == 0) return doc;

        // Split retaining per-line terminators for byte-for-byte roundtrip.
        var lines = new List<(string Content, string Terminator)>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && text[i] != '\r' && text[i] != '\n') i++;
            var content = text[start..i];
            var term = "";
            if (i < text.Length)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { term = "\r\n"; i += 2; }
                else { term = text[i].ToString(); i++; }
            }
            lines.Add((content, term));
        }
        if (lines.Count > MaxLines)
            throw new UserInputException("INI text has too many lines (up to 20,000).");
        var firstTerm = lines.Select(l => l.Terminator).FirstOrDefault(t => t.Length > 0);
        if (firstTerm is not null) doc._newLine = firstTerm;

        string? currentSection = null;
        for (var idx = 0; idx < lines.Count; idx++)
        {
            var (content, term) = lines[idx];
            var lineNo = idx + 1;
            IniNode node;
            var trimmed = content.Trim();
            var trimmedStart = content.TrimStart();

            var hasControl = content.Any(c => char.IsControl(c) && c != '\t');
            if (hasControl)
                doc._issues.Add(new IniParseIssue(lineNo, "Line contains embedded control characters."));

            if (trimmed.Length == 0)
            {
                node = new IniBlankLineNode(content, lineNo);
            }
            else if (trimmedStart.StartsWith(';') || trimmedStart.StartsWith('#'))
            {
                node = new IniCommentNode(content, lineNo);
            }
            else if (trimmedStart.StartsWith('['))
            {
                if (trimmed.Length >= 3 && trimmed.EndsWith(']') && trimmed[1..^1].Trim().Length > 0 && !trimmed[1..^1].Contains(']'))
                {
                    var name = trimmed[1..^1].Trim();
                    currentSection = name;
                    node = new IniSectionNode(content, lineNo, name);
                }
                else
                {
                    doc._issues.Add(new IniParseIssue(lineNo, "Malformed section header."));
                    node = new IniRawLineNode(content, lineNo);
                }
            }
            else if (content.Contains('='))
            {
                var eq = content.IndexOf('=');
                var rawKey = content[..eq];
                var key = rawKey.Trim();
                var rawValue = content[(eq + 1)..];
                if (key.Length == 0)
                {
                    doc._issues.Add(new IniParseIssue(lineNo, "Entry has an empty key."));
                    node = new IniRawLineNode(content, lineNo);
                }
                else
                {
                    if (hasControl)
                        doc._issues.Add(new IniParseIssue(lineNo, "Entry key or value contains embedded control characters."));
                    node = new IniEntryNode(content, lineNo, currentSection, rawKey, key, rawValue);
                }
            }
            else
            {
                doc._issues.Add(new IniParseIssue(lineNo, "Line is not an entry, comment, blank line or section header."));
                node = new IniRawLineNode(content, lineNo);
            }
            node.Terminator = term;
            doc._nodes.Add(node);
        }
        return doc;
    }

    public string Serialize()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in _nodes) { sb.Append(node.RawText); sb.Append(node.Terminator); }
        return sb.ToString();
    }

    public IEnumerable<IniSectionNode> Sections => _nodes.OfType<IniSectionNode>();

    public IEnumerable<IniEntryNode> AllEntries => _nodes.OfType<IniEntryNode>();

    public IEnumerable<IniEntryNode> Entries(string section) =>
        AllEntries.Where(e => string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase));

    /// <summary>All typed values (outer quotes stripped) for a key, in document order. Repeated keys are preserved.</summary>
    public IReadOnlyList<string> GetValues(string section, string key) =>
        Entries(section).Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value).ToList();

    public string? GetValue(string section, string key) => GetValues(section, key).FirstOrDefault();

    public bool HasSection(string section) => Sections.Any(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase));

    /// <summary>Updates the first matching entry in place (preserving its raw key spelling) or appends to the section, creating the section if needed.</summary>
    public void SetValue(string section, string key, string value)
    {
        var existing = Entries(section).FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.RawValue = value;
            existing.RawText = existing.RawKey + "=" + value;
            return;
        }
        var entry = new IniEntryNode(key + "=" + value, 0, section, key, key, value) { Terminator = _newLine };
        var sectionNode = Sections.FirstOrDefault(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase));
        if (sectionNode is null)
        {
            AppendNode(new IniSectionNode("[" + section + "]", 0, section));
            AppendNode(entry);
        }
        else
        {
            // Insert after the last node belonging to this section.
            var idx = _nodes.IndexOf(sectionNode);
            var insertAt = idx + 1;
            for (var j = idx + 1; j < _nodes.Count && _nodes[j] is not IniSectionNode; j++) insertAt = j + 1;
            InsertNode(insertAt, entry);
        }
        Renumber();
    }

    private void AppendNode(IniNode node)
    {
        var endedWithNewline = _nodes.Count > 0 && _nodes[^1].Terminator.Length > 0;
        if (_nodes.Count > 0 && _nodes[^1].Terminator.Length == 0) _nodes[^1].Terminator = _newLine;
        node.Terminator = endedWithNewline ? _newLine : "";
        _nodes.Add(node);
        Renumber();
    }

    private void InsertNode(int index, IniNode node)
    {
        if (index >= _nodes.Count)
        {
            AppendNode(node);
            return;
        }
        node.Terminator = _newLine;
        _nodes.Insert(index, node);
        Renumber();
    }

    private void Renumber()
    {
        for (var i = 0; i < _nodes.Count; i++) { _nodes[i].LineStart = i + 1; _nodes[i].LineEnd = i + 1; }
    }
}
