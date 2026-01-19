using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// OpenAI Chat Completions API implementation with streaming and function calling.
/// Uses the same report_technical_response function pattern as the Realtime API.
/// </summary>
public sealed class OpenAiChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private readonly ILogger<OpenAiChatCompletionService> _logger;

    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private const string DefaultSystemInstructions = """
        You are a C# programming expert assistant helping in a technical interview.

        MANDATORY BEHAVIOR:
        When answering programming questions, you MUST call the report_technical_response function.
        You MUST ALWAYS provide both parameters:
        1. answer - your explanation
        2. console_code - complete C# code

        NEVER call the function with only 'answer'. ALWAYS include 'console_code'.
        If no code is needed, set console_code to: "// No code example needed"

        The console_code must be a complete, runnable C# program with Main method when code is relevant.
        """;

    private static readonly object FunctionDefinition = new
    {
        type = "function",
        function = new
        {
            name = "report_technical_response",
            description = "Answer programming questions. MUST include both 'answer' and 'console_code' parameters - never omit console_code.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    answer = new
                    {
                        type = "string",
                        description = "Explanation of the concept"
                    },
                    console_code = new
                    {
                        type = "string",
                        description = "Complete C# console application code. REQUIRED - must always be provided. Use '// No code needed' if not applicable."
                    }
                },
                required = new[] { "answer", "console_code" }
            }
        }
    };

    public OpenAiChatCompletionService(
        string apiKey,
        string model = "gpt-4o",
        int maxTokens = 2048,
        double temperature = 0.3,
        ILogger<OpenAiChatCompletionService>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
        _maxTokens = maxTokens;
        _temperature = temperature;
        _logger = logger ?? NullLogger<OpenAiChatCompletionService>.Instance;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<ChatResponse> GenerateResponseAsync(
        string question,
        string? conversationContext,
        IReadOnlyList<ContextChunk>? contextChunks,
        string? systemInstructions,
        Action<string>? onDelta,
        CancellationToken ct = default)
    {
        var messages = BuildMessages(question, conversationContext, contextChunks, systemInstructions);

        var requestBody = new
        {
            model = _model,
            messages,
            tools = new[] { FunctionDefinition },
            tool_choice = new { type = "function", function = new { name = "report_technical_response" } },
            temperature = _temperature,
            max_tokens = _maxTokens,
            stream = true
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending chat completion request for question: {Question}",
            question.Length > 100 ? question[..100] + "..." : question);

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl)
        {
            Content = content
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await ProcessStreamingResponse(response, onDelta, ct).ConfigureAwait(false);
    }

    private List<object> BuildMessages(
        string question,
        string? conversationContext,
        IReadOnlyList<ContextChunk>? contextChunks,
        string? systemInstructions)
    {
        var messages = new List<object>();

        // System message
        var sysInstr = string.IsNullOrWhiteSpace(systemInstructions)
            ? DefaultSystemInstructions
            : systemInstructions;
        messages.Add(new { role = "system", content = sysInstr });

        // Context chunks (CV, job spec)
        if (contextChunks is { Count: > 0 })
        {
            foreach (var chunk in contextChunks)
            {
                messages.Add(new { role = "user", content = $"[CONTEXT] {chunk.Label}\n{chunk.Text}" });
            }
        }

        // Conversation context for follow-up understanding
        if (!string.IsNullOrWhiteSpace(conversationContext))
        {
            messages.Add(new { role = "user", content = $"[RECENT CONVERSATION]\n{conversationContext}" });
        }

        // The actual question
        messages.Add(new { role = "user", content = question });

        return messages;
    }

    private async Task<ChatResponse> ProcessStreamingResponse(
        HttpResponseMessage response,
        Action<string>? onDelta,
        CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var functionArgs = new StringBuilder();
        var textContent = new StringBuilder();
        var functionName = "";
        var inFunctionCall = false;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var delta = choices[0].GetProperty("delta");

                // Handle tool calls (function arguments)
                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    inFunctionCall = true;
                    var toolCall = toolCalls[0];

                    if (toolCall.TryGetProperty("function", out var func))
                    {
                        if (func.TryGetProperty("name", out var name))
                        {
                            functionName = name.GetString() ?? "";
                        }

                        if (func.TryGetProperty("arguments", out var args))
                        {
                            var argDelta = args.GetString() ?? "";
                            functionArgs.Append(argDelta);

                            // Stream the raw delta for UI feedback
                            if (!string.IsNullOrEmpty(argDelta))
                            {
                                onDelta?.Invoke(argDelta);
                            }
                        }
                    }
                }

                // Handle regular content (fallback if no function call)
                if (delta.TryGetProperty("content", out var content))
                {
                    var textDelta = content.GetString() ?? "";
                    if (!string.IsNullOrEmpty(textDelta))
                    {
                        textContent.Append(textDelta);
                        onDelta?.Invoke(textDelta);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse streaming chunk");
            }
        }

        // Parse the response
        if (inFunctionCall && functionArgs.Length > 0)
        {
            return ParseFunctionCallResponse(functionName, functionArgs.ToString());
        }

        // Fallback: extract from text content
        return new ChatResponse
        {
            Answer = textContent.ToString(),
            Code = "// No code example provided",
            FunctionName = "report_technical_response",
            WasFunctionCall = false
        };
    }

    private ChatResponse ParseFunctionCallResponse(string functionName, string args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            var root = doc.RootElement;

            var answer = root.TryGetProperty("answer", out var answerProp)
                ? answerProp.GetString() ?? ""
                : "";

            var code = root.TryGetProperty("console_code", out var codeProp)
                ? codeProp.GetString() ?? "// No code example provided"
                : "// No code example provided";

            _logger.LogDebug("Parsed function call: {FunctionName}, answer: {AnswerLen} chars, code: {CodeLen} chars",
                functionName, answer.Length, code.Length);

            return new ChatResponse
            {
                Answer = answer,
                Code = code,
                FunctionName = functionName,
                WasFunctionCall = true
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse function call arguments");

            // Attempt repair
            var repaired = AttemptJsonRepair(args);
            if (repaired != null)
            {
                return repaired;
            }

            return new ChatResponse
            {
                Answer = args, // Use raw args as answer
                Code = "// Failed to parse function call",
                FunctionName = functionName,
                WasFunctionCall = true
            };
        }
    }

    private ChatResponse? AttemptJsonRepair(string args)
    {
        try
        {
            // Try adding missing closing brace
            var trimmed = args.Trim();
            if (!trimmed.EndsWith("}"))
            {
                trimmed += "}";
            }
            if (!trimmed.StartsWith("{"))
            {
                trimmed = "{" + trimmed;
            }

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var answer = root.TryGetProperty("answer", out var answerProp)
                ? answerProp.GetString() ?? ""
                : "";

            var code = root.TryGetProperty("console_code", out var codeProp)
                ? codeProp.GetString() ?? "// No code example provided"
                : "// No code example provided";

            _logger.LogInformation("Successfully repaired malformed function arguments");

            return new ChatResponse
            {
                Answer = answer,
                Code = code,
                FunctionName = "report_technical_response",
                WasFunctionCall = true
            };
        }
        catch
        {
            return null;
        }
    }
}
