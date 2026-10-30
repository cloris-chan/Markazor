using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Markazor.Content;

internal sealed class MarkdownFrontMatter(IReadOnlyList<string> lines, string body, bool hasFrontMatter)
{
    private readonly Dictionary<string, string> values = ReadValues(lines);

    public IReadOnlyList<string> Lines { get; } = lines;

    public string Body { get; } = body;

    public bool HasFrontMatter { get; } = hasFrontMatter;

    public DateTimeOffset PublishedAtUtc => DateTimeOffset.TryParse(Get("publishedAt") ?? Get("date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset date)
        ? date : new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string? Get(string key) => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static MarkdownFrontMatter Parse(string markdown)
    {
        using StringReader reader = new(markdown);
        if (reader.ReadLine() != "---")
        {
            return new([], markdown, false);
        }

        List<string> lines = [];
        string? line;
        while ((line = reader.ReadLine()) is not null && line != "---")
        {
            lines.Add(line);
        }

        return line is null ? new([], markdown, false) : new(lines, reader.ReadToEnd().TrimStart('\r', '\n'), true);
    }

    public string[] GetTags()
    {
        string text = (Get("tags") ?? string.Empty).Trim().Trim('[', ']');
        List<string> tags = [];
        int start = 0;
        char quote = '\0';
        for (int index = 0; index <= text.Length; index++)
        {
            if (index == text.Length || (text[index] == ',' && quote == '\0'))
            {
                string tag = Unquote(text.Substring(start, index - start).Trim());
                if (tag.Length > 0)
                {
                    tags.Add(tag);
                }

                start = index + 1;
            }
            else if (quote == '"' && text[index] == '\\')
            {
                index++;
            }
            else if (quote == '\'' && text[index] == '\'' && index + 1 < text.Length && text[index + 1] == '\'')
            {
                index++;
            }
            else if (text[index] == quote)
            {
                quote = '\0';
            }
            else if (quote == '\0' && text.Substring(start, index - start).Trim().Length == 0 && (text[index] == '"' || text[index] == '\''))
            {
                quote = text[index];
            }
        }

        return tags.ToArray();
    }

    private static Dictionary<string, string> ReadValues(IReadOnlyList<string> lines)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            int separator = line.IndexOf(':', 0);
            if (separator > 0)
            {
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                values[key] = string.Equals(key, "tags", StringComparison.OrdinalIgnoreCase) ? value : Unquote(value);
            }
        }

        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != value[value.Length - 1])
        {
            return value;
        }

        char quote = value[0];
        if (quote != '"' && quote != '\'')
        {
            return value;
        }

        StringBuilder result = new(value.Length - 2);
        for (int index = 1; index < value.Length - 1; index++)
        {
            char character = value[index];
            if (index + 1 < value.Length - 1 && (
                (quote == '"' && character == '\\' && (value[index + 1] == '\\' || value[index + 1] == '"'))
                || (quote == '\'' && character == '\'' && value[index + 1] == '\'')))
            {
                character = value[++index];
            }

            result.Append(character);
        }

        return result.ToString();
    }
}
