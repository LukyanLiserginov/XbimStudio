using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;
using XplorerPlugin.AiAgent.Tools;

namespace XplorerPlugin.AiAgent.Reports
{
    /// <summary>A row of report data extracted from the model.</summary>
    internal class ReportRow
    {
        public int ExpressId;
        public string GlobalId;
        public string Type;
        public string Name;
        public string Storey;
        public List<KeyValuePair<string, string>> Properties = new List<KeyValuePair<string, string>>();
    }

    /// <summary>Collected report data ready for rendering.</summary>
    internal class ReportData
    {
        public string Title;
        public string ModelFile;
        public string Schema;
        public DateTime Generated = DateTime.Now;
        public string Filters;
        public List<ReportRow> Rows = new List<ReportRow>();
        public Dictionary<string, int> TypeCounts = new Dictionary<string, int>();
    }

    /// <summary>Collects report rows from an IFC model and renders them as Markdown, CSV or HTML.</summary>
    internal class ReportBuilder
    {
        private readonly IModel _model;
        private readonly string _modelFile;

        public ReportBuilder(IModel model, string modelFile)
        {
            _model = model;
            _modelFile = modelFile;
        }

        public ReportData Collect(string entityType, string nameContains, string storey, bool includeProperties, int maxRows)
        {
            var data = new ReportData
            {
                ModelFile = _modelFile,
                Schema = _model?.SchemaVersion.ToString() ?? ""
            };
            if (_model == null)
                return data;

            IEnumerable<IIfcProduct> q = _model.Instances.OfType<IIfcProduct>();
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                var want = entityType.Trim();
                q = q.Where(p => IfcQuery.TypeNameMatches((IPersistEntity)p, want));
                filters.Add("type=" + want);
            }
            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                var needle = nameContains.Trim();
                q = q.Where(p =>
                {
                    var name = p.Name?.ToString();
                    return name != null && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                });
                filters.Add("name~\"" + needle + "\"");
            }
            if (!string.IsNullOrWhiteSpace(storey))
            {
                var sneedle = storey.Trim();
                q = q.Where(p =>
                {
                    var n = p.IsContainedIn?.Name?.ToString();
                    return n != null && n.IndexOf(sneedle, StringComparison.OrdinalIgnoreCase) >= 0;
                });
                filters.Add("storey=" + sneedle);
            }
            data.Filters = filters.Count > 0 ? string.Join(", ", filters) : "all products";

            foreach (var p in q)
            {
                var row = new ReportRow
                {
                    ExpressId = ((IPersistEntity)p).EntityLabel,
                    GlobalId = p.GlobalId.ToString(),
                    Type = IfcQuery.TypeName((IPersistEntity)p),
                    Name = p.Name?.ToString(),
                    Storey = IfcQuery.StoreyOf(p)
                };
                if (includeProperties)
                {
                    var ifcObject = p as IIfcObject;
                    if (ifcObject != null)
                        row.Properties.AddRange(IfcQuery.Properties(ifcObject, 30));
                }
                data.Rows.Add(row);
                data.TypeCounts.TryGetValue(row.Type, out var c);
                data.TypeCounts[row.Type] = c + 1;
                if (data.Rows.Count >= maxRows)
                    break;
            }
            return data;
        }

        public static string RenderMarkdown(ReportData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + (string.IsNullOrEmpty(d.Title) ? "IFC Model Report" : d.Title));
            sb.AppendLine();
            sb.AppendLine("- **Model:** " + (d.ModelFile ?? "(unnamed)"));
            sb.AppendLine("- **Schema:** " + d.Schema);
            sb.AppendLine("- **Generated:** " + d.Generated.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("- **Filters:** " + d.Filters);
            sb.AppendLine("- **Rows:** " + d.Rows.Count);
            sb.AppendLine();
            if (d.TypeCounts.Count > 0)
            {
                sb.AppendLine("## Element types");
                sb.AppendLine();
                sb.AppendLine("| Type | Count |");
                sb.AppendLine("|---|---:|");
                foreach (var kv in d.TypeCounts.OrderByDescending(kv => kv.Value))
                    sb.AppendLine("| " + kv.Key + " | " + kv.Value + " |");
                sb.AppendLine();
            }
            sb.AppendLine("## Elements");
            sb.AppendLine();
            sb.AppendLine("| # | ExpressId | GlobalId | Type | Name | Storey |");
            sb.AppendLine("|---:|---:|---|---|---|---|");
            for (var i = 0; i < d.Rows.Count; i++)
            {
                var r = d.Rows[i];
                sb.AppendLine("| " + (i + 1) + " | " + r.ExpressId + " | " + Md(r.GlobalId) + " | " + Md(r.Type) +
                             " | " + Md(r.Name) + " | " + Md(r.Storey) + " |");
            }
            var withProps = d.Rows.Where(r => r.Properties.Count > 0).ToList();
            if (withProps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Properties");
                sb.AppendLine();
                foreach (var r in withProps)
                {
                    sb.AppendLine("### #" + r.ExpressId + " " + r.Type + " " + (r.Name ?? ""));
                    sb.AppendLine();
                    sb.AppendLine("| Property | Value |");
                    sb.AppendLine("|---|---|");
                    foreach (var kv in r.Properties)
                        sb.AppendLine("| " + Md(kv.Key) + " | " + Md(kv.Value) + " |");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        public static string RenderCsv(ReportData d)
        {
            var sb = new StringBuilder();
            var propKeys = d.Rows.SelectMany(r => r.Properties.Select(kv => kv.Key))
                .Distinct().Take(20).ToList();
            sb.AppendLine(string.Join(",", new[] { "ExpressId", "GlobalId", "Type", "Name", "Storey" }
                .Concat(propKeys).Select(IfcQuery.EscapeCsv)));
            foreach (var r in d.Rows)
            {
                var cells = new List<string>
                {
                    r.ExpressId.ToString(), r.GlobalId ?? "", r.Type ?? "", r.Name ?? "", r.Storey ?? ""
                };
                foreach (var key in propKeys)
                {
                    var v = r.Properties.FirstOrDefault(kv => kv.Key == key).Value;
                    cells.Add(v ?? "");
                }
                sb.AppendLine(string.Join(",", cells.Select(IfcQuery.EscapeCsv)));
            }
            return sb.ToString();
        }

        public static string RenderHtml(ReportData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"utf-8\"/><title>" + IfcQuery.HtmlEncode(d.Title) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;}");
            sb.AppendLine("h1{font-size:22px;} h2{font-size:17px;margin-top:28px;}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;margin-top:8px;font-size:13px;}");
            sb.AppendLine("th,td{border:1px solid #ccc;padding:5px 8px;text-align:left;vertical-align:top;}");
            sb.AppendLine("th{background:#f0f2f5;position:sticky;top:0;}");
            sb.AppendLine("tr:nth-child(even){background:#fafafa;}");
            sb.AppendLine(".meta{color:#555;font-size:13px;}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>" + IfcQuery.HtmlEncode(d.Title ?? "IFC Model Report") + "</h1>");
            sb.AppendLine("<p class=\"meta\">Model: <b>" + IfcQuery.HtmlEncode(d.ModelFile ?? "(unnamed)") + "</b><br/>");
            sb.AppendLine("Schema: " + IfcQuery.HtmlEncode(d.Schema) + "<br/>");
            sb.AppendLine("Generated: " + d.Generated.ToString("yyyy-MM-dd HH:mm:ss") + "<br/>");
            sb.AppendLine("Filters: " + IfcQuery.HtmlEncode(d.Filters) + "<br/>");
            sb.AppendLine("Rows: " + d.Rows.Count + "</p>");

            if (d.TypeCounts.Count > 0)
            {
                sb.AppendLine("<h2>Element types</h2><table><tr><th>Type</th><th>Count</th></tr>");
                foreach (var kv in d.TypeCounts.OrderByDescending(kv => kv.Value))
                    sb.AppendLine("<tr><td>" + IfcQuery.HtmlEncode(kv.Key) + "</td><td>" + kv.Value + "</td></tr>");
                sb.AppendLine("</table>");
            }

            sb.AppendLine("<h2>Elements</h2><table><tr><th>#</th><th>ExpressId</th><th>GlobalId</th><th>Type</th><th>Name</th><th>Storey</th></tr>");
            for (var i = 0; i < d.Rows.Count; i++)
            {
                var r = d.Rows[i];
                sb.AppendLine("<tr><td>" + (i + 1) + "</td><td>" + r.ExpressId + "</td><td>" + IfcQuery.HtmlEncode(r.GlobalId) +
                              "</td><td>" + IfcQuery.HtmlEncode(r.Type) + "</td><td>" + IfcQuery.HtmlEncode(r.Name) +
                              "</td><td>" + IfcQuery.HtmlEncode(r.Storey) + "</td></tr>");
            }
            sb.AppendLine("</table>");

            var withProps = d.Rows.Where(r => r.Properties.Count > 0).ToList();
            if (withProps.Count > 0)
            {
                sb.AppendLine("<h2>Properties</h2>");
                foreach (var r in withProps)
                {
                    sb.AppendLine("<h3>#" + r.ExpressId + " " + IfcQuery.HtmlEncode(r.Type) + " " + IfcQuery.HtmlEncode(r.Name) + "</h3>");
                    sb.AppendLine("<table><tr><th>Property</th><th>Value</th></tr>");
                    foreach (var kv in r.Properties)
                        sb.AppendLine("<tr><td>" + IfcQuery.HtmlEncode(kv.Key) + "</td><td>" + IfcQuery.HtmlEncode(kv.Value) + "</td></tr>");
                    sb.AppendLine("</table>");
                }
            }
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Md(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
        }
    }

    /// <summary>Saves rendered report files with safe unique names.</summary>
    internal static class ReportWriter
    {
        public static string Save(ReportData data, string content, string format, string folder)
        {
            Directory.CreateDirectory(folder);
            var ext = format == "csv" ? "csv" : format == "html" ? "html" : "md";
            var baseName = Sanitize(string.IsNullOrEmpty(data.Title) ? "ifc-report" : data.Title);
            var modelPart = Sanitize(Path.GetFileNameWithoutExtension(data.ModelFile ?? "model"));
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(folder, modelPart + "_" + baseName + "_" + stamp + "." + ext);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-');
            var result = sb.ToString().Trim('-');
            while (result.Contains("--"))
                result = result.Replace("--", "-");
            return string.IsNullOrEmpty(result) ? "report" : result;
        }
    }
}
