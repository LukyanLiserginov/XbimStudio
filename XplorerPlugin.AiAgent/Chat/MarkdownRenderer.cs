using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace XplorerPlugin.AiAgent.Chat
{
    /// <summary>
    /// Minimal markdown renderer producing a FlowDocument fragment.
    /// Supports: headings (#..###), bullet/numbered lists, fenced code blocks,
    /// bold, italic, inline code and simple pipe tables. Unknown syntax falls back to plain text.
    /// </summary>
    internal static class MarkdownRenderer
    {
        private static readonly FontFamily Mono = new FontFamily("Consolas, Courier New");

        public static void RenderInto(FlowDocument doc, string markdown)
        {
            doc.Blocks.Clear();
            if (string.IsNullOrEmpty(markdown))
                return;

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var i = 0;
            while (i < lines.Length)
            {
                var line = lines[i];

                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal))
                    {
                        sb.AppendLine(lines[i]);
                        i++;
                    }
                    i++; // skip closing fence
                    doc.Blocks.Add(CodeBlock(sb.ToString().TrimEnd('\n')));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                var heading = MatchHeading(line);
                if (heading != null)
                {
                    doc.Blocks.Add(heading);
                    i++;
                    continue;
                }

                if (IsTableStart(lines, i))
                {
                    i = AddTable(doc, lines, i);
                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal) ||
                    line.StartsWith("* ", StringComparison.Ordinal))
                {
                    var items = new System.Collections.Generic.List<string>();
                    while (i < lines.Length &&
                           (lines[i].StartsWith("- ", StringComparison.Ordinal) ||
                            lines[i].StartsWith("* ", StringComparison.Ordinal)))
                    {
                        items.Add(lines[i].Substring(2));
                        i++;
                    }
                    var list = new List();
                    foreach (var item in items)
                    {
                        var para = new Paragraph { Margin = new Thickness(0), LineHeight = 18 };
                        AddInline(para, item);
                        list.ListItems.Add(new ListItem(para));
                    }
                    doc.Blocks.Add(list);
                    continue;
                }

                var text = new StringBuilder(line);
                i++;
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) &&
                       !lines[i].StartsWith("#", StringComparison.Ordinal) &&
                       !lines[i].StartsWith("```", StringComparison.Ordinal) &&
                       !lines[i].StartsWith("- ", StringComparison.Ordinal) &&
                       !lines[i].StartsWith("* ", StringComparison.Ordinal) &&
                       !lines[i].StartsWith("|", StringComparison.Ordinal))
                {
                    text.Append(" ").Append(lines[i]);
                    i++;
                }
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
                AddInline(p, text.ToString());
                doc.Blocks.Add(p);
            }
        }

        private static Paragraph MatchHeading(string line)
        {
            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
                hashes++;
            if (hashes == 0 || hashes > 6 || hashes >= line.Length || line[hashes] != ' ')
                return null;
            var content = line.Substring(hashes + 1).Trim();
            double size = hashes == 1 ? 20 : hashes == 2 ? 17 : 15;
            var para = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
            var run = new Run(content) { FontSize = size, FontWeight = FontWeights.Bold };
            para.Inlines.Add(run);
            return para;
        }

        private static bool IsTableStart(string[] lines, int i)
        {
            return lines[i].TrimStart().StartsWith("|", StringComparison.Ordinal) &&
                   i + 1 < lines.Length &&
                   lines[i + 1].Contains("---");
        }

        private static int AddTable(FlowDocument doc, string[] lines, int start)
        {
            var rows = new System.Collections.Generic.List<string[]>();
            var i = start;
            while (i < lines.Length && lines[i].TrimStart().StartsWith("|", StringComparison.Ordinal))
            {
                if (!lines[i].Contains("---"))
                    rows.Add(SplitRow(lines[i]));
                i++;
            }
            if (rows.Count == 0)
                return i;

            var cols = rows.Max(r => r.Length);
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 8) };
            for (var c = 0; c < cols; c++)
                table.Columns.Add(new TableColumn());

            var headerGroup = new TableRowGroup();
            var header = new TableRow();
            for (var c = 0; c < cols; c++)
            {
                var cell = new TableCell(new Paragraph(new Run(rows[0].Length > c ? rows[0][c] : ""))
                {
                    Margin = new Thickness(0)
                })
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)),
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                header.Cells.Add(cell);
            }
            headerGroup.Rows.Add(header);
            table.RowGroups.Add(headerGroup);

            var bodyGroup = new TableRowGroup();
            for (var r = 1; r < rows.Count; r++)
            {
                var row = new TableRow();
                for (var c = 0; c < cols; c++)
                {
                    var cell = new TableCell(new Paragraph(new Run(rows[r].Length > c ? rows[r][c] : ""))
                    {
                        Margin = new Thickness(0)
                    })
                    {
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(4, 2, 4, 2)
                    };
                    row.Cells.Add(cell);
                }
                bodyGroup.Rows.Add(row);
            }
            table.RowGroups.Add(bodyGroup);
            doc.Blocks.Add(table);
            return i;
        }

        private static string[] SplitRow(string line)
        {
            line = line.Trim();
            if (line.StartsWith("|", StringComparison.Ordinal))
                line = line.Substring(1);
            if (line.EndsWith("|", StringComparison.Ordinal))
                line = line.Substring(0, line.Length - 1);
            return line.Split('|').Select(c => c.Trim().Replace("\\|", "|")).ToArray();
        }

        private static Block CodeBlock(string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontFamily = Mono,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF6)),
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 4, 0, 8)
            };
            return para;
        }

        private static void AddInline(Paragraph para, string text)
        {
            // supports **bold**, *italic*, `code`
            var i = 0;
            var sb = new StringBuilder();
            var mode = ' ';
            void Flush()
            {
                if (sb.Length == 0)
                    return;
                var run = new Run(sb.ToString());
                if (mode == 'b')
                    run.FontWeight = FontWeights.Bold;
                else if (mode == 'i')
                    run.FontStyle = FontStyles.Italic;
                else if (mode == 'c')
                {
                    run.FontFamily = Mono;
                    run.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
                }
                para.Inlines.Add(run);
                sb.Clear();
            }

            while (i < text.Length)
            {
                if (mode == ' ' && i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    Flush();
                    mode = 'b';
                    i += 2;
                    continue;
                }
                if (mode == 'b' && i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    Flush();
                    mode = ' ';
                    i += 2;
                    continue;
                }
                if (mode == ' ' && text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
                {
                    Flush();
                    mode = 'i';
                    i++;
                    continue;
                }
                if (mode == 'i' && text[i] == '*')
                {
                    Flush();
                    mode = ' ';
                    i++;
                    continue;
                }
                if (mode == ' ' && text[i] == '`')
                {
                    Flush();
                    mode = 'c';
                    i++;
                    continue;
                }
                if (mode == 'c' && text[i] == '`')
                {
                    Flush();
                    mode = ' ';
                    i++;
                    continue;
                }
                sb.Append(text[i]);
                i++;
            }
            Flush();
        }
    }
}
