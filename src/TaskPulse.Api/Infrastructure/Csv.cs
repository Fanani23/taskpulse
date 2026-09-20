using System.Text;

namespace TaskPulse.Api.Infrastructure;

// RFC 4180 without a dependency: comma-separated, fields quoted when they need it, quotes doubled, CRLF or LF rows.
public static class Csv
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.IndexOfAny(['"', ',', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Row(params string?[] fields) => string.Join(",", fields.Select(Escape));

    // Parses a whole document into rows of fields. Tolerant of a UTF-8 BOM, a missing final newline and empty lines.
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿')
        {
            i = 1;
        }

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Count > 1 || row[0].Length > 0)
                    {
                        rows.Add(row);
                    }

                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0)
            {
                rows.Add(row);
            }
        }

        return rows;
    }
}
