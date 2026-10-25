using System;
using System.Text.Json.Nodes;

namespace XplorerPlugin.AiAgent.Agent
{
    /// <summary>A tool (function) exposed to the LLM with its local implementation.</summary>
    public class AgentTool
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>JSON schema string of the tool arguments.</summary>
        public string ParametersSchema { get; set; }

        /// <summary>Executes the tool locally. Receives parsed arguments, returns a result string (usually JSON).</summary>
        public Func<JsonNode, string> Execute { get; set; }

        public static string ArgString(JsonNode args, string name, string defaultValue = null)
        {
            var v = args?[name]?.GetValue<string>();
            return string.IsNullOrEmpty(v) ? defaultValue : v;
        }

        public static int ArgInt(JsonNode args, string name, int defaultValue)
        {
            var n = args?[name];
            if (n == null)
                return defaultValue;
            try
            {
                return n.GetValue<int>();
            }
            catch (Exception)
            {
                var s = n.ToString();
                return int.TryParse(s, out var v) ? v : defaultValue;
            }
        }
    }
}
