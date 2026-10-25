using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace XplorerPlugin.AiAgent.Llm
{
    /// <summary>A single chat message in the OpenAI protocol.</summary>
    public class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<ToolCall> ToolCalls { get; set; }

        [JsonPropertyName("tool_call_id")]
        public string ToolCallId { get; set; }
    }

    /// <summary>A tool call requested by the model.</summary>
    public class ToolCall
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public FunctionCall Function { get; set; }
    }

    public class FunctionCall
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>JSON-encoded arguments string as sent by the model.</summary>
        [JsonPropertyName("arguments")]
        public string Arguments { get; set; }
    }

    /// <summary>A tool (function) exposed to the model.</summary>
    public class ToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>JSON schema of the arguments, as a raw JSON string.</summary>
        public string ParametersSchema { get; set; }
    }

    /// <summary>Result of one chat completion (streaming aggregated).</summary>
    public class CompletionResult
    {
        public string Content { get; set; }
        public List<ToolCall> ToolCalls { get; set; }
        public string FinishReason { get; set; }

        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }
}
