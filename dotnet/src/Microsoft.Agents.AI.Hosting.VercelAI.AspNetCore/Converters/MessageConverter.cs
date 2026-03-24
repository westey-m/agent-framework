// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Converters;

/// <summary>
/// Converts Vercel AI SDK <see cref="VercelAIMessage"/> objects to <see cref="ChatMessage"/> objects
/// that can be consumed by the Agent Framework.
/// </summary>
internal static class MessageConverter
{
    /// <summary>
    /// Converts a list of <see cref="VercelAIMessage"/> to a list of <see cref="ChatMessage"/>.
    /// </summary>
    internal static List<ChatMessage> ToChatMessages(this IList<VercelAIMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return [];
        }

        var result = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
        {
            result.Add(message.ToChatMessage());
        }

        return result;
    }

    internal static ChatMessage ToChatMessage(this VercelAIMessage message)
    {
        var role = message.Role switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => new ChatRole(message.Role),
        };

        var contents = new List<AIContent>();

        if (message.Parts is not null)
        {
            foreach (var part in message.Parts)
            {
                switch (part.Type)
                {
                    case "text" when part.Text is not null:
                        contents.Add(new TextContent(part.Text));
                        break;

                    case "file" when part.Url is not null && part.MediaType is not null:
                        // Convert data URLs to binary content, remote URLs to UriContent
                        if (part.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            contents.Add(new DataContent(part.Url, part.MediaType));
                        }
                        else
                        {
                            contents.Add(new UriContent(new Uri(part.Url), part.MediaType));
                        }

                        break;

                    default:
                        // Tool invocations from assistant messages are reconstructed as
                        // FunctionCallContent / FunctionResultContent when needed.
                        // For now, skip unknown part types — they are protocol-level
                        // concepts that don't map to ChatMessage content.
                        if (IsToolInvocationPart(part) && role == ChatRole.Assistant)
                        {
                            AddToolContents(part, contents);
                        }

                        break;
                }
            }
        }

        // Ensure at least one text content if no parts were converted
        if (contents.Count == 0)
        {
            contents.Add(new TextContent(string.Empty));
        }

        return new ChatMessage(role, contents);
    }

    private static bool IsToolInvocationPart(VercelAIMessagePart part) =>
        part.ToolCallId is not null && part.ToolName is not null;

    private static void AddToolContents(VercelAIMessagePart part, List<AIContent> contents)
    {
        if (part.State is "output-available" or "output-error")
        {
            // The assistant already called this tool and got a result — represent as
            // FunctionCallContent (the call) + FunctionResultContent (the result).
            Dictionary<string, object?>? arguments = null;
            if (part.Input is JsonElement inputElement && inputElement.ValueKind == JsonValueKind.Object)
            {
                arguments = new Dictionary<string, object?>();
                foreach (var prop in inputElement.EnumerateObject())
                {
                    arguments[prop.Name] = prop.Value.Clone();
                }
            }

            contents.Add(new FunctionCallContent(part.ToolCallId!, part.ToolName!, arguments));

            object? result = part.Output is JsonElement outputElement ? outputElement.GetRawText() : null;
            contents.Add(new FunctionResultContent(part.ToolCallId!, result));
        }
        else if (part.State is "input-available")
        {
            // Tool call that hasn't been executed yet
            Dictionary<string, object?>? arguments = null;
            if (part.Input is JsonElement inputElement && inputElement.ValueKind == JsonValueKind.Object)
            {
                arguments = new Dictionary<string, object?>();
                foreach (var prop in inputElement.EnumerateObject())
                {
                    arguments[prop.Name] = prop.Value.Clone();
                }
            }

            contents.Add(new FunctionCallContent(part.ToolCallId!, part.ToolName!, arguments));
        }
    }
}
