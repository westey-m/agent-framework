// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Suppress experimental API warnings for Responses API usage.

using System.Text.Json;
using Harness.Shared.Console.Observers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace Harness.Shared.Console.OpenAI;

/// <summary>
/// Detects and displays error/incomplete status from OpenAI Responses API streaming updates.
/// Handles <see cref="StreamingResponseFailedUpdate"/> and <see cref="StreamingResponseIncompleteUpdate"/>
/// which are not surfaced as <see cref="ErrorContent"/> by the chat client.
/// </summary>
/// <remarks>
/// Note: <see cref="StreamingResponseErrorUpdate"/> is already handled by the SDK — it produces
/// an <see cref="ErrorContent"/> which is displayed by <see cref="ErrorDisplayObserver"/>.
/// This observer covers the cases where the SDK does not produce <see cref="ErrorContent"/>.
/// </remarks>
public sealed class OpenAIResponsesErrorObserver : ConsoleObserver
{
    /// <inheritdoc/>
    public override async Task OnResponseUpdateAsync(IUXStateDriver ux, AgentResponseUpdate update, AIAgent agent, AgentSession session)
    {
        // AgentResponseUpdate.RawRepresentation is the ChatResponseUpdate,
        // whose RawRepresentation is the underlying StreamingResponseUpdate.
        object? rawUpdate = (update.RawRepresentation as ChatResponseUpdate)?.RawRepresentation
            ?? update.RawRepresentation;

        switch (rawUpdate)
        {
            case StreamingResponseFailedUpdate failedUpdate:
                // Only display if the response has error details populated.
                // When error is null, a follow-up StreamingResponseErrorUpdate typically
                // carries the real error — the SDK surfaces that as ErrorContent,
                // which is displayed by ErrorDisplayObserver.
                if (failedUpdate.Response?.Error is { } error)
                {
                    string errorMessage = error.Message ?? "Unknown error";
                    string? errorCode = error.Code.ToString();
                    string errorText = $"❌ Response failed: {errorMessage}";
                    if (!string.IsNullOrEmpty(errorCode))
                    {
                        errorText += $" (code: {errorCode})";
                    }

                    await ux.WriteInfoLineAsync(errorText, ConsoleColor.Red);
                }

                break;

            case StreamingResponseIncompleteUpdate incompleteUpdate:
                string? reason = incompleteUpdate.Response?.IncompleteStatusDetails?.Reason?.ToString();
                if (string.Equals(reason, "content_filter", StringComparison.OrdinalIgnoreCase))
                {
                    await ux.WriteInfoLineAsync(
                        "🛡️ The service's built-in content filter guardrails were triggered and the response was cut short.",
                        ConsoleColor.Yellow);

                    await WriteContentFilterDetailsAsync(ux, incompleteUpdate);
                }
                else
                {
                    string incompleteText = $"⚠️ Response incomplete: {reason ?? "unknown reason"}";
                    await ux.WriteInfoLineAsync(incompleteText, ConsoleColor.Yellow);
                }

                break;
        }
    }

    /// <summary>
    /// Extracts and displays content filter details from the serialized response JSON.
    /// Parses the <c>content_filters[].content_filter_results</c> to show which specific
    /// categories were triggered (e.g. hate, sexual, violence, self_harm, protected_material).
    /// </summary>
    private static async Task WriteContentFilterDetailsAsync(IUXStateDriver ux, StreamingResponseIncompleteUpdate incompleteUpdate)
    {
        try
        {
            var data = System.ClientModel.Primitives.ModelReaderWriter.Write(incompleteUpdate);
            using var doc = JsonDocument.Parse(data.ToString());
            var root = doc.RootElement;

            // Navigate into the nested response object if present.
            JsonElement responseElement = root.TryGetProperty("response", out var resp) ? resp : root;

            if (!responseElement.TryGetProperty("content_filters", out var filtersArray)
                || filtersArray.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var filter in filtersArray.EnumerateArray())
            {
                if (!filter.TryGetProperty("content_filter_results", out var results)
                    || results.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // Collect category data for aligned output.
                var categories = new List<(string Name, bool Filtered, string? Severity)>();
                foreach (var category in results.EnumerateObject())
                {
                    if (category.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    bool filtered = category.Value.TryGetProperty("filtered", out var f) && f.GetBoolean();
                    string? severity = category.Value.TryGetProperty("severity", out var s) ? s.GetString() : null;
                    categories.Add((category.Name, filtered, severity));
                }

                // Calculate column widths for alignment.
                int maxNameLen = categories.Count > 0 ? categories.Max(c => c.Name.Length) : 0;

                foreach (var (name, filtered, severity) in categories)
                {
                    string paddedName = name.PadRight(maxNameLen);
                    string icon = filtered ? "❌" : "✅";
                    string statusText = filtered ? "Filtered    " : "Not Filtered";
                    string severityText = severity is not null ? $"   Severity: {severity}" : "";
                    ConsoleColor color = filtered ? ConsoleColor.Red : ConsoleColor.DarkGray;

                    await ux.WriteInfoLineAsync(
                        $"    {icon} {paddedName}  {statusText}{severityText}",
                        color);
                }
            }
        }
        catch
        {
            // Parsing not critical — skip silently if it fails.
        }
    }
}
