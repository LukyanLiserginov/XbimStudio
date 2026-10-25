using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xbim.Common;
using XplorerPlugin.AiAgent.Agent;
using XplorerPlugin.AiAgent.Reports;
using XplorerPlugin.AiAgent.Settings;

namespace XplorerPlugin.AiAgent.Tools
{
    /// <summary>Report generation tool. Builds a report from the model, saves it to disk and returns the path.</summary>
    internal class ReportTools
    {
        private readonly Func<IModel> _modelAccessor;
        private readonly Func<string> _modelFileAccessor;
        private readonly AgentSettings _settings;

        /// <summary>Raised after a report file is written. Runs on the UI thread.</summary>
        public event Action<string /*path*/, string /*title*/> ReportGenerated;

        public ReportTools(Func<IModel> modelAccessor, Func<string> modelFileAccessor, AgentSettings settings)
        {
            _modelAccessor = modelAccessor;
            _modelFileAccessor = modelFileAccessor;
            _settings = settings;
        }

        public AgentTool GetGenerateReportTool()
        {
            return new AgentTool
            {
                Name = "generate_report",
                Description = "Generate a report file about the model elements and save it to disk. " +
                              "Supported formats: markdown (md), csv, html. " +
                              "Use entityType/nameContains/storey filters to scope the report, or leave them empty for all products. " +
                              "Returns the saved file path. The report is also shown to the user in the chat panel.",
                ParametersSchema =
                    "{\"type\":\"object\",\"properties\":{" +
                    "\"title\":{\"type\":\"string\",\"description\":\"short report title, e.g. 'Door inventory'\"}," +
                    "\"format\":{\"type\":\"string\",\"enum\":[\"markdown\",\"md\",\"csv\",\"html\"],\"description\":\"output format\"}," +
                    "\"entityType\":{\"type\":\"string\",\"description\":\"optional IFC class filter like Wall, Door, Slab\"}," +
                    "\"nameContains\":{\"type\":\"string\",\"description\":\"optional name substring filter\"}," +
                    "\"storey\":{\"type\":\"string\",\"description\":\"optional storey filter\"}," +
                    "\"includeProperties\":{\"type\":\"boolean\",\"description\":\"include property set values per element (markdown/html only)\"}," +
                    "\"maxRows\":{\"type\":\"integer\",\"description\":\"max rows in the report (bounded by settings)\"}" +
                    "},\"required\":[]}",
                Execute = Generate
            };
        }

        private string Generate(JsonNode args)
        {
            var model = _modelAccessor();
            if (model == null)
                return "{\"error\":\"No IFC model is open.\"}";

            var title = AgentTool.ArgString(args, "title", "IFC Model Report");
            var format = (AgentTool.ArgString(args, "format", "markdown") ?? "markdown").ToLowerInvariant();
            if (format == "md")
                format = "markdown";
            if (format != "markdown" && format != "csv" && format != "html")
                format = "markdown";

            var entityType = AgentTool.ArgString(args, "entityType");
            var nameContains = AgentTool.ArgString(args, "nameContains");
            var storey = AgentTool.ArgString(args, "storey");
            var includeProperties = args?["includeProperties"]?.GetValue<bool>() ?? true;
            var maxRows = Math.Min(AgentTool.ArgInt(args, "maxRows", 500), Math.Max(_settings.MaxRows, 500));

            var builder = new ReportBuilder(model, _modelFileAccessor());
            var data = builder.Collect(entityType, nameContains, storey, includeProperties, maxRows);
            data.Title = title;

            string content;
            switch (format)
            {
                case "csv":
                    content = ReportBuilder.RenderCsv(data);
                    break;
                case "html":
                    content = ReportBuilder.RenderHtml(data);
                    break;
                default:
                    content = ReportBuilder.RenderMarkdown(data);
                    break;
            }

            try
            {
                var path = ReportWriter.Save(data, content, format, _settings.EffectiveReportFolder);
                ReportGenerated?.Invoke(path, title);
                return new JsonObject
                {
                    ["savedTo"] = path,
                    ["rows"] = data.Rows.Count,
                    ["format"] = format,
                    ["typeCounts"] = new JsonArray(data.TypeCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(10)
                        .Select(kv => (JsonNode)new JsonObject { ["type"] = kv.Key, ["count"] = kv.Value })
                        .ToArray())
                }.ToJsonString();
            }
            catch (Exception ex)
            {
                return "{\"error\":\"Failed to save report: " + AgentToolEscape(ex.Message) + "\"}";
            }
        }

        private static string AgentToolEscape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
