using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace XplorerPlugin.AiAgent.Llm
{
    /// <summary>
    /// Minimal OpenAI-compatible chat completions client built on HttpClient.
    /// Supports tool (function) calling and SSE streaming.
    /// Works with api.openai.com and local OpenAI-compatible servers (llama.cpp etc).
    /// </summary>
    public class OpenAiClient : IDisposable
    {
        private static readonly HttpClient Http = new HttpClient();

        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        public double Temperature { get; set; } = 0.2;

        private string CompletionsUrl
        {
            get
            {
                var b = (BaseUrl ?? "").TrimEnd('/');
                if (b.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                    return b;
                return b + "/chat/completions";
            }
        }

        /// <summary>
        /// Sends a chat completion request. When <paramref name="onContentDelta"/> is provided
        /// the request is streamed and partial content is reported through the callback
        /// (invoked on a background thread).
        /// </summary>
        public async Task<CompletionResult> CompleteAsync(
            IList<ChatMessage> messages,
            IList<ToolDefinition> tools,
            Action<string> onContentDelta,
            CancellationToken ct)
        {
            var payload = BuildPayload(messages, tools, onContentDelta != null);

            using (var request = new HttpRequestMessage(HttpMethod.Post, CompletionsUrl))
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(ApiKey))
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ApiKey);

                using (var response = await Http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException(
                            $"LLM endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(errBody, 800)}");
                    }

                    if (onContentDelta != null)
                    {
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            return ParseStreaming(reader, onContentDelta, ct);
                    }

                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseNonStreaming(body);
                }
            }
        }

        private string BuildPayload(IList<ChatMessage> messages, IList<ToolDefinition> tools, bool stream)
        {
            var root = new JsonObject
            {
                ["model"] = Model,
                ["messages"] = SerializeMessages(messages),
                ["temperature"] = Temperature,
                ["stream"] = stream
            };
            if (tools != null && tools.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var t in tools)
                {
                    var parameters = string.IsNullOrEmpty(t.ParametersSchema)
                        ? (JsonNode)new JsonObject { ["type"] = "object" }
                        : JsonNode.Parse(t.ParametersSchema);
                    arr.Add(new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Description,
                            ["parameters"] = parameters
                        }
                    });
                }
                root["tools"] = arr;
                root["tool_choice"] = "auto";
            }
            return root.ToJsonString();
        }

        private static JsonArray SerializeMessages(IList<ChatMessage> messages)
        {
            var arr = new JsonArray();
            foreach (var m in messages)
            {
                var node = new JsonObject { ["role"] = m.Role };
                if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    // assistant message carrying tool_calls: content may be null
                    if (!string.IsNullOrEmpty(m.Content))
                        node["content"] = m.Content;
                    else
                        node["content"] = null;
                    var tc = new JsonArray();
                    foreach (var call in m.ToolCalls)
                    {
                        tc.Add(new JsonObject
                        {
                            ["id"] = call.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = call.Function.Name,
                                ["arguments"] = call.Function.Arguments ?? "{}"
                            }
                        });
                    }
                    node["tool_calls"] = tc;
                }
                else if (m.Role == "tool")
                {
                    node["tool_call_id"] = m.ToolCallId;
                    node["content"] = m.Content ?? "";
                }
                else
                {
                    node["content"] = m.Content ?? "";
                }
                arr.Add(node);
            }
            return arr;
        }

        private static CompletionResult ParseNonStreaming(string body)
        {
            var doc = JsonNode.Parse(body);
            var choice = doc?["choices"]?[0];
            var msg = choice?["message"];
            var result = new CompletionResult
            {
                Content = msg?["content"]?.GetValue<string>(),
                FinishReason = choice?["finish_reason"]?.GetValue<string>()
            };
            var toolCalls = msg?["tool_calls"]?.AsArray();
            if (toolCalls != null && toolCalls.Count > 0)
            {
                result.ToolCalls = new List<ToolCall>();
                foreach (var t in toolCalls)
                {
                    result.ToolCalls.Add(new ToolCall
                    {
                        Id = t?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                        Function = new FunctionCall
                        {
                            Name = t?["function"]?["name"]?.GetValue<string>(),
                            Arguments = t?["function"]?["arguments"]?.GetValue<string>() ?? "{}"
                        }
                    });
                }
            }
            return result;
        }

        private static CompletionResult ParseStreaming(StreamReader reader, Action<string> onContentDelta, CancellationToken ct)
        {
            var result = new CompletionResult();
            var toolCalls = new SortedDictionary<int, ToolCall>();
            var sb = new StringBuilder();
            string line;
            while (!ct.IsCancellationRequested && (line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;
                var data = line.Substring(5).Trim();
                if (data == "[DONE]")
                    break;

                JsonNode doc;
                try
                {
                    doc = JsonNode.Parse(data);
                }
                catch (JsonException)
                {
                    continue; // tolerate malformed keep-alive chunks
                }

                var choice = doc?["choices"]?[0];
                if (choice == null)
                    continue;

                var delta = choice["delta"];
                var content = delta?["content"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(content))
                {
                    sb.Append(content);
                    onContentDelta(content);
                }

                var tcArr = delta?["tool_calls"]?.AsArray();
                if (tcArr != null)
                {
                    foreach (var t in tcArr)
                    {
                        var index = t?["index"]?.GetValue<int>() ?? 0;
                        if (!toolCalls.TryGetValue(index, out var call))
                        {
                            call = new ToolCall
                            {
                                Id = t?["id"]?.GetValue<string>(),
                                Function = new FunctionCall()
                            };
                            toolCalls[index] = call;
                        }
                        var id = t?["id"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(id))
                            call.Id = id;
                        var name = t?["function"]?["name"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name))
                            call.Function.Name = name;
                        var args = t?["function"]?["arguments"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(args))
                            call.Function.Arguments = (call.Function.Arguments ?? "") + args;
                    }
                }

                var finish = choice["finish_reason"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(finish))
                    result.FinishReason = finish;
            }

            result.Content = sb.ToString();
            if (toolCalls.Count > 0)
            {
                result.ToolCalls = new List<ToolCall>(toolCalls.Values);
                foreach (var c in result.ToolCalls)
                {
                    if (string.IsNullOrEmpty(c.Id))
                        c.Id = Guid.NewGuid().ToString("N");
                    if (c.Function.Arguments == null)
                        c.Function.Arguments = "{}";
                }
            }
            return result;
        }

        private static string Truncate(string s, int max)
        {
            if (s == null)
                return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        public void Dispose()
        {
            // HttpClient is static and shared; nothing to dispose per instance
        }
    }
}
