using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XplorerPlugin.AiAgent.Llm;
using XplorerPlugin.AiAgent.Settings;

namespace XplorerPlugin.AiAgent.Agent
{
    /// <summary>
    /// The agent loop: sends the conversation to the LLM, executes requested tools
    /// (on the UI thread via the injected executor) and feeds results back.
    /// </summary>
    internal class AgentRunner
    {
        private readonly OpenAiClient _client;
        private readonly AgentSettings _settings;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly Dictionary<string, AgentTool> _tools = new Dictionary<string, AgentTool>();

        /// <summary>Executes a tool on the UI thread. Receives the tool and parsed arguments.</summary>
        public Func<AgentTool, JsonNode, string> ToolExecutor { get; set; }

        /// <summary>Callback for streamed assistant text (background thread).</summary>
        public Action<string> OnAssistantDelta { get; set; }

        /// <summary>Callback when a tool is invoked: name and a short arguments preview.</summary>
        public Action<string, string> OnToolActivity { get; set; }

        public AgentRunner(OpenAiClient client, AgentSettings settings)
        {
            _client = client;
            _settings = settings;
        }

        public void RegisterTool(AgentTool tool)
        {
            _tools[tool.Name] = tool;
        }

        public void SetSystemPrompt(string text)
        {
            if (_history.Count > 0 && _history[0].Role == "system")
                _history[0].Content = text;
            else
                _history.Insert(0, new ChatMessage { Role = "system", Content = text });
        }

        public void ClearHistory()
        {
            var sys = _history.FirstOrDefault(m => m.Role == "system");
            _history.Clear();
            if (sys != null)
                _history.Add(sys);
        }

        /// <summary>Runs one user turn to completion (including tool call loops).</summary>
        public async Task<string> RunAsync(string userMessage, CancellationToken ct)
        {
            _history.Add(new ChatMessage { Role = "user", Content = userMessage });

            var toolDefs = _tools.Values
                .Select(t => new ToolDefinition { Name = t.Name, Description = t.Description, ParametersSchema = t.ParametersSchema })
                .ToList();

            string finalText = null;
            for (var iteration = 0; iteration <= Math.Max(1, _settings.MaxToolIterations); iteration++)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _client.CompleteAsync(_history, toolDefs, OnAssistantDelta, ct).ConfigureAwait(false);

                if (!result.HasToolCalls)
                {
                    finalText = result.Content ?? "";
                    _history.Add(new ChatMessage { Role = "assistant", Content = finalText });
                    return finalText;
                }

                _history.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = result.Content,
                    ToolCalls = result.ToolCalls
                });

                foreach (var call in result.ToolCalls)
                {
                    ct.ThrowIfCancellationRequested();
                    var argsText = call.Function.Arguments ?? "{}";
                    string preview = argsText.Length > 120 ? argsText.Substring(0, 120) + "..." : argsText;
                    OnToolActivity?.Invoke(call.Function.Name, preview);

                    string toolResult;
                    JsonNode parsedArgs = null;
                    try
                    {
                        parsedArgs = JsonNode.Parse(argsText);
                    }
                    catch (Exception)
                    {
                        parsedArgs = null;
                    }

                    if (parsedArgs == null)
                    {
                        toolResult = "{\"error\":\"Invalid JSON arguments\"}";
                    }
                    else if (!_tools.TryGetValue(call.Function.Name, out var tool) || ToolExecutor == null)
                    {
                        toolResult = "{\"error\":\"Unknown tool '" + call.Function.Name + "'\"}";
                    }
                    else
                    {
                        try
                        {
                            // model access must happen on the UI thread
                            toolResult = ToolExecutor(tool, parsedArgs);
                        }
                        catch (Exception ex)
                        {
                            toolResult = "{\"error\":\"Tool failed: " + Escape(ex.Message) + "\"}";
                        }
                    }

                    toolResult = Truncate(toolResult, Math.Max(1000, _settings.MaxToolResultChars));
                    _history.Add(new ChatMessage
                    {
                        Role = "tool",
                        ToolCallId = call.Id,
                        Content = toolResult
                    });
                }
            }

            finalText = "I stopped after reaching the maximum number of tool iterations. " +
                        "Please narrow down the question so I can answer it with fewer queries.";
            _history.Add(new ChatMessage { Role = "assistant", Content = finalText });
            return finalText;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s ?? "";
            return s.Substring(0, max) + "\n...[truncated]";
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
