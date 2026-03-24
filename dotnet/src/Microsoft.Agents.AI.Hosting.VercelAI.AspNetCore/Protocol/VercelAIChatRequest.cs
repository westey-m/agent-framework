// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;

/// <summary>
/// Represents the JSON request body sent by the Vercel AI SDK default chat transport.
/// </summary>
/// <remarks>
/// <para>
/// When the client uses <c>prepareSendMessagesRequest</c> to send only the last message
/// (recommended for server-managed sessions), the request body contains a single <see cref="Message"/>
/// instead of the full <see cref="Messages"/> array.
/// </para>
/// <para>
/// See <see href="https://ai-sdk.dev/docs/ai-sdk-ui/storing-messages#sending-only-the-last-message"/>
/// and <see href="https://github.com/vercel/ai/blob/main/packages/ai/src/ui/http-chat-transport.ts"/>
/// for the upstream documentation and TypeScript definition.
/// </para>
/// </remarks>
internal sealed class VercelAIChatRequest
{
    /// <summary>The chat session identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The messages in the conversation (full-history mode).</summary>
    [JsonPropertyName("messages")]
    public List<VercelAIMessage>? Messages { get; set; }

    /// <summary>
    /// A single new message (single-message mode, used with <c>prepareSendMessagesRequest</c>).
    /// </summary>
    /// <remarks>
    /// See <see href="https://ai-sdk.dev/docs/ai-sdk-ui/storing-messages#sending-only-the-last-message"/>.
    /// </remarks>
    [JsonPropertyName("message")]
    public VercelAIMessage? Message { get; set; }

    /// <summary>What triggered this request (<c>submit-message</c> or <c>regenerate-message</c>).</summary>
    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    /// <summary>The message identifier when regenerating.</summary>
    [JsonPropertyName("messageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MessageId { get; set; }
}

/// <summary>
/// A single message in the Vercel AI SDK UIMessage format.
/// </summary>
internal sealed class VercelAIMessage
{
    /// <summary>Unique message identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The role: <c>user</c>, <c>assistant</c>, or <c>system</c>.</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>The structured parts of the message.</summary>
    [JsonPropertyName("parts")]
    public List<VercelAIMessagePart>? Parts { get; set; }

    /// <summary>Optional message metadata.</summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Metadata { get; set; }
}

/// <summary>
/// A part within a <see cref="VercelAIMessage"/>. The <see cref="Type"/> property discriminates the part kind.
/// </summary>
internal sealed class VercelAIMessagePart
{
    /// <summary>The part type (<c>text</c>, <c>file</c>, <c>tool-invocation</c>, <c>reasoning</c>, etc.).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    // Text parts
    /// <summary>Text content (for <c>text</c> parts).</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    // File parts
    /// <summary>File URL (for <c>file</c> parts).</summary>
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    /// <summary>IANA media type (for <c>file</c> parts).</summary>
    [JsonPropertyName("mediaType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    // Tool invocation parts — carried as opaque JSON to support all states
    /// <summary>Tool call ID (for tool invocation parts).</summary>
    [JsonPropertyName("toolCallId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    /// <summary>Tool name (for tool invocation parts).</summary>
    [JsonPropertyName("toolName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }

    /// <summary>Tool invocation state (for tool invocation parts).</summary>
    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; set; }

    /// <summary>Tool input (for tool invocation parts).</summary>
    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }

    /// <summary>Tool output (for tool invocation parts with <c>output-available</c> state).</summary>
    [JsonPropertyName("output")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Output { get; set; }
}
