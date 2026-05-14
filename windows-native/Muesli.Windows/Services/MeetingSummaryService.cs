using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Muesli.Windows.Services;

public static class MeetingSummaryService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    public static string CreateSummary(string transcript, string template = "standard")
    {
        var clean = Regex.Replace(transcript, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        var sentences = Regex.Split(clean, @"(?<=[.!?])\s+")
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .ToList();

        var summarySentences = sentences.Count <= 4
            ? sentences
            : sentences.Take(2).Concat(sentences.TakeLast(2)).ToList();

        var bullets = summarySentences
            .Select(sentence => sentence.Trim().TrimStart('-', '•').Trim())
            .Where(sentence => sentence.Length > 0)
            .Select(sentence => $"- {sentence}");

        var bulletText = string.Join(Environment.NewLine, bullets);
        if (template.Equals("standup", StringComparison.OrdinalIgnoreCase))
        {
            return "## Meeting Summary" + Environment.NewLine + bulletText +
                   Environment.NewLine + Environment.NewLine +
                   "## Updates" + Environment.NewLine + bulletText +
                   Environment.NewLine + Environment.NewLine +
                   "## Blockers" + Environment.NewLine + "None noted." +
                   Environment.NewLine + Environment.NewLine +
                   "## Action Items" + Environment.NewLine + "None noted.";
        }

        if (template.Equals("customer", StringComparison.OrdinalIgnoreCase))
        {
            return "## Meeting Summary" + Environment.NewLine + bulletText +
                   Environment.NewLine + Environment.NewLine +
                   "## Pain Points" + Environment.NewLine + "None noted." +
                   Environment.NewLine + Environment.NewLine +
                   "## Quotes / Signals" + Environment.NewLine + "None noted." +
                   Environment.NewLine + Environment.NewLine +
                   "## Action Items" + Environment.NewLine + "None noted.";
        }

        return "## Meeting Summary" + Environment.NewLine + bulletText;
    }

    public static async Task<string> CreateSummaryAsync(string transcript, string meetingTitle, MuesliSettings settings)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return "";
        }

        var provider = settings.MeetingSummaryProvider.Trim().ToLowerInvariant();
        try
        {
            return provider switch
            {
                "openai" => await SummarizeWithOpenAIAsync(transcript, meetingTitle, settings),
                "openrouter" => await SummarizeWithOpenRouterAsync(transcript, meetingTitle, settings),
                _ => CreateSummary(transcript, EffectiveTemplate(settings))
            };
        }
        catch
        {
            return CreateSummary(transcript, EffectiveTemplate(settings));
        }
    }

    private static async Task<string> SummarizeWithOpenAIAsync(string transcript, string meetingTitle, MuesliSettings settings)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = settings.OpenAIApiKey;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return CreateSummary(transcript, EffectiveTemplate(settings));
        }

        var model = string.IsNullOrWhiteSpace(settings.OpenAIModel) ? "gpt-5.4-mini" : settings.OpenAIModel;
        var body = new
        {
            model,
            input = new object[]
            {
                new { role = "system", content = SummaryInstructions(EffectiveTemplate(settings)) },
                new { role = "user", content = SummaryUserPrompt(transcript, meetingTitle) }
            },
            reasoning = new { effort = "low" },
            text = new { verbosity = "low" },
            max_output_tokens = 2500
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent(body);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return CreateSummary(transcript, EffectiveTemplate(settings));
        }

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var text = ExtractOpenAIText(document.RootElement);
        return string.IsNullOrWhiteSpace(text) ? CreateSummary(transcript, EffectiveTemplate(settings)) : text.Trim();
    }

    private static async Task<string> SummarizeWithOpenRouterAsync(string transcript, string meetingTitle, MuesliSettings settings)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = settings.OpenRouterApiKey;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return CreateSummary(transcript, EffectiveTemplate(settings));
        }

        var model = string.IsNullOrWhiteSpace(settings.OpenRouterModel)
            ? "stepfun/step-3.5-flash:free"
            : settings.OpenRouterModel;
        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = SummaryInstructions(EffectiveTemplate(settings)) },
                new { role = "user", content = SummaryUserPrompt(transcript, meetingTitle) }
            },
            max_tokens = 2500
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "Muesli Windows");
        request.Content = JsonContent(body);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return CreateSummary(transcript, EffectiveTemplate(settings));
        }

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var text = ExtractOpenRouterText(document.RootElement);
        return string.IsNullOrWhiteSpace(text) ? CreateSummary(transcript, EffectiveTemplate(settings)) : text.Trim();
    }

    private static StringContent JsonContent<T>(T value)
    {
        return new StringContent(
            JsonSerializer.Serialize(value),
            Encoding.UTF8,
            "application/json");
    }

    private static string SummaryInstructions(string template = "standard")
    {
        if (!string.IsNullOrWhiteSpace(template) && template.Contains('\n'))
        {
            return template;
        }

        if (template.Equals("standup", StringComparison.OrdinalIgnoreCase))
        {
            return """
            You are a meeting notes assistant. Produce concise markdown notes for a standup.
            Do not invent facts.

            ## Meeting Summary
            Summarize progress and blockers.

            ## Updates
            - List concrete status updates.

            ## Blockers
            - List blockers. If none, write "None noted."

            ## Action Items
            - List action items with owners only if stated. If none, write "None noted."
            """;
        }

        if (template.Equals("customer", StringComparison.OrdinalIgnoreCase))
        {
            return """
            You are a meeting notes assistant. Produce concise markdown notes for a customer discovery call.
            Do not invent facts.

            ## Meeting Summary
            Summarize the customer context and outcome.

            ## Pain Points
            - List pain points and needs.

            ## Quotes / Signals
            - Capture notable signals only if actually present.

            ## Action Items
            - List follow-ups with owners only if stated. If none, write "None noted."
            """;
        }

        if (template.Equals("1 to 1", StringComparison.OrdinalIgnoreCase))
        {
            return """
            Use this structure exactly:

            ## Check-In
            A brief summary of how the conversation opened and the overall tone.

            ## Topics Discussed
            - Main themes raised by either person

            ## Support Needed
            - Blockers, concerns, or asks for help

            ## Commitments
            - [ ] Follow-ups or commitments made by either person

            ## Manager Notes
            - Coaching, feedback, or context that should be remembered
            """;
        }

        if (template.Equals("customer discovery", StringComparison.OrdinalIgnoreCase))
        {
            return """
            Use this structure exactly:

            ## Customer Context
            - Company, role, or situation if mentioned

            ## Problems and Pain Points
            - Explicit frustrations, blockers, or unmet needs

            ## Current Workflow
            - How they currently solve the problem today

            ## Buying Signals
            - Indicators of urgency, budget, timing, or decision process

            ## Next Steps
            - [ ] Follow-up actions, owners, and dates if mentioned
            """;
        }

        if (template.Equals("hiring", StringComparison.OrdinalIgnoreCase))
        {
            return """
            Use this structure exactly:

            ## Candidate Snapshot
            A concise overview of the candidate and relevant background.

            ## Strengths
            - Positive signals from the conversation

            ## Concerns
            - Risks, gaps, or open questions

            ## Role Fit
            - Why they do or do not fit the role as discussed

            ## Decision and Next Steps
            - [ ] Hiring decision, interview progression, or follow-up items
            """;
        }

        if (template.Equals("stand-up", StringComparison.OrdinalIgnoreCase))
        {
            return """
            Use this structure exactly:

            ## Yesterday
            - Work completed or progress since the last update

            ## Today
            - Planned work or priorities for today

            ## Blockers
            - Risks, delays, or dependencies

            ## Coordination Notes
            - Decisions, asks, or cross-team alignment points
            """;
        }

        if (template.Equals("weekly team meeting", StringComparison.OrdinalIgnoreCase))
        {
            return """
            Use this structure exactly:

            ## Weekly Overview
            A concise summary of the most important updates from the meeting.

            ## Progress Updates
            - Key workstreams and status changes

            ## Decisions
            - Decisions made or confirmed

            ## Risks and Open Questions
            - Issues that need attention or follow-up

            ## Action Items
            - [ ] Tasks, owners, and timing if mentioned
            """;
        }

        return """
        You are a meeting notes assistant. Given a raw meeting transcript, produce concise, professional markdown notes.
        Do not invent facts. Prefer concrete takeaways over filler. Capture owners only when they are actually mentioned.
        If a requested section has no content, write "None noted."

        Follow this note template:

        ## Meeting Summary
        Briefly summarize the purpose and outcome of the meeting.

        ## Key Points
        - Capture the most important discussion points.

        ## Decisions
        - List decisions. If none, write "None noted."

        ## Action Items
        - List action items with owners only if stated. If none, write "None noted."
        """;
    }

    private static string EffectiveTemplate(MuesliSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MeetingSummaryPromptOverride)
            ? settings.MeetingSummaryTemplate
            : settings.MeetingSummaryPromptOverride;
    }

    private static string SummaryUserPrompt(string transcript, string meetingTitle)
    {
        return $"Meeting title: {meetingTitle}{Environment.NewLine}{Environment.NewLine}Raw transcript:{Environment.NewLine}{transcript}";
    }

    private static string ExtractOpenAIText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? "";
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in content.EnumerateArray())
            {
                if (entry.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? "";
                }
            }
        }

        return "";
    }

    private static string ExtractOpenRouterText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var first = choices.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined ||
            !first.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content))
        {
            return "";
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? "";
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? "");
            }
        }

        return string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
