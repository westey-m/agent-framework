// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Suppress experimental API warnings for Responses API usage.

using System.Text;
using Harness.Shared.Console;
using Harness.Shared.Console.Observers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace SampleApp;

/// <summary>
/// Displays web search activity in the scroll area. Shows search queries,
/// page opens, and find-in-page actions as they stream in from the API.
/// </summary>
internal sealed class OpenAIResponsesWebSearchDisplayObserver : ConsoleObserver
{
    private const int MaxQueryDisplayLength = 120;

    /// <inheritdoc/>
    public override async Task OnContentAsync(IUXStateDriver ux, AIContent content, AIAgent agent, AgentSession session)
    {
        if (content is WebSearchToolResultContent resultContent
            && resultContent.RawRepresentation is WebSearchCallResponseItem wscri)
        {
            await WriteActionAsync(ux, wscri, resultContent.Outputs);
        }
    }

    private static async Task WriteActionAsync(IUXStateDriver ux, WebSearchCallResponseItem wscri, IList<AIContent>? outputs)
    {
        WebSearchAction? action = wscri.Action;
        if (action is null)
        {
            await ux.WriteInfoLineAsync("🌐 Web Search Tool (no action details)", ConsoleColor.DarkCyan);
            return;
        }

        switch (action)
        {
            case WebSearchFindInPageAction findInPage:
                await WriteFindInPageAsync(ux, findInPage);
                break;

            case WebSearchOpenPageAction openPage:
                await WriteOpenPageAsync(ux, openPage);
                break;

            default:
                // "search" action type — the concrete class is internal to the SDK,
                // so we extract queries from the raw JSON representation.
                await WriteSearchAsync(ux, wscri, outputs);
                break;
        }
    }

    private static async Task WriteSearchAsync(IUXStateDriver ux, WebSearchCallResponseItem wscri, IList<AIContent>? outputs)
    {
        var queries = GetQueriesFromItem(wscri);

        if (queries is null || queries.Count == 0)
        {
            await ux.WriteInfoLineAsync("🌐 Web Search Tool: search", ConsoleColor.DarkCyan);
            return;
        }

        var sb = new StringBuilder();
        sb.Append("🌐 Web Search Tool: search");

        // Show the search queries.
        bool hasResults = outputs is { Count: > 0 };
        for (int i = 0; i < queries.Count; i++)
        {
            string connector = (i < queries.Count - 1 || hasResults) ? "├─" : "└─";
            string query = Truncate(queries[i], MaxQueryDisplayLength);
            sb.Append($"\n   {connector} \"{query}\"");
        }

        // Show search result sources (URLs + titles) when available.
        // This requires IncludedResponseProperty.WebSearchCallActionSources on the request options,
        // which is only supported by the direct OpenAI API — Azure AI Foundry does not currently return sources.
        if (hasResults)
        {
            sb.Append("\n   │");
            for (int i = 0; i < outputs!.Count; i++)
            {
                string connector = i < outputs.Count - 1 ? "├─" : "└─";
                string line = FormatOutput(outputs[i]);
                sb.Append($"\n   {connector} {line}");
            }
        }

        await ux.WriteInfoLineAsync(sb.ToString(), ConsoleColor.DarkCyan);
    }

    private static async Task WriteOpenPageAsync(IUXStateDriver ux, WebSearchOpenPageAction openPage)
    {
        string url = openPage.Uri?.AbsoluteUri ?? "(unknown)";
        await ux.WriteInfoLineAsync(
            $"🌐 Web Search Tool: open page\n   └─ {url}",
            ConsoleColor.DarkCyan);
    }

    private static async Task WriteFindInPageAsync(IUXStateDriver ux, WebSearchFindInPageAction findInPage)
    {
        string url = findInPage.Uri?.AbsoluteUri ?? "(unknown)";
        string pattern = findInPage.Pattern ?? "(unknown)";

        await ux.WriteInfoLineAsync(
            $"🌐 Web Search Tool: find in page\n   ├─ \"{Truncate(pattern, MaxQueryDisplayLength)}\"\n   └─ {url}",
            ConsoleColor.DarkCyan);
    }

    /// <summary>
    /// Formats a single search result output for display.
    /// </summary>
    private static string FormatOutput(AIContent output)
    {
        if (output is UriContent uriContent)
        {
            string url = uriContent.Uri?.AbsoluteUri ?? "(unknown)";

            // Try to extract a title from the raw JSON of the source.
            // The SDK's WebSearchActionUriSource doesn't expose a title property,
            // but the API may include one in the raw response.
            string? title = GetTitleFromRawRepresentation(uriContent.RawRepresentation)
                ?? (uriContent.AdditionalProperties?.TryGetValue("title", out var t) is true ? t?.ToString() : null);

            return title is not null
                ? $"{Truncate(title, MaxQueryDisplayLength)} — {url}"
                : url;
        }

        return output.ToString() ?? "(unknown output)";
    }

    /// <summary>
    /// Attempts to extract a "title" field from a raw representation object by serializing it to JSON.
    /// </summary>
    private static string? GetTitleFromRawRepresentation(object? rawRepresentation)
    {
        if (rawRepresentation is null)
        {
            return null;
        }

        try
        {
            var data = System.ClientModel.Primitives.ModelReaderWriter.Write(rawRepresentation);
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("title", out var titleEl)
                && titleEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return titleEl.GetString();
            }
        }
        catch
        {
            // Serialization may not be supported for this object type.
        }

        return null;
    }

    /// <summary>
    /// Extracts query strings from a <see cref="WebSearchCallResponseItem"/> by
    /// reading the "queries" array from the serialized JSON, since the search
    /// action type is internal to the OpenAI SDK.
    /// </summary>
    private static List<string>? GetQueriesFromItem(WebSearchCallResponseItem wscri)
    {
        try
        {
            var data = System.ClientModel.Primitives.ModelReaderWriter.Write(wscri);
            using var doc = System.Text.Json.JsonDocument.Parse(data);

            if (!doc.RootElement.TryGetProperty("action", out var actionEl))
            {
                return null;
            }

            // Try the "queries" array first (multiple search queries).
            if (actionEl.TryGetProperty("queries", out var queriesEl)
                && queriesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var queries = new List<string>();
                foreach (var q in queriesEl.EnumerateArray())
                {
                    string? text = q.GetString();
                    if (text is not null)
                    {
                        queries.Add(text);
                    }
                }

                return queries;
            }

            // Fall back to the single "query" field.
            if (actionEl.TryGetProperty("query", out var queryEl)
                && queryEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                string? text = queryEl.GetString();
                return text is not null ? [text] : null;
            }
        }
        catch
        {
            // Serialization may fail (e.g. the null-URI bug on find_in_page actions).
        }

        return null;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 1), "…");
}
