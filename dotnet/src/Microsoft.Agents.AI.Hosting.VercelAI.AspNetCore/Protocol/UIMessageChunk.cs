// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;

/// <summary>
/// Represents a chunk in the Vercel AI SDK UI Message Stream.
/// This is the base type for all chunk variants, discriminated by the type property.
/// </summary>
/// <remarks>
/// See <see href="https://github.com/vercel/ai/blob/main/packages/ai/src/ui-message-stream/ui-message-chunks.ts"/>
/// for the upstream TypeScript definitions.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StartChunk), UIMessageChunkTypes.Start)]
[JsonDerivedType(typeof(FinishChunk), UIMessageChunkTypes.Finish)]
[JsonDerivedType(typeof(StartStepChunk), UIMessageChunkTypes.StartStep)]
[JsonDerivedType(typeof(FinishStepChunk), UIMessageChunkTypes.FinishStep)]
[JsonDerivedType(typeof(TextStartChunk), UIMessageChunkTypes.TextStart)]
[JsonDerivedType(typeof(TextDeltaChunk), UIMessageChunkTypes.TextDelta)]
[JsonDerivedType(typeof(TextEndChunk), UIMessageChunkTypes.TextEnd)]
[JsonDerivedType(typeof(ReasoningStartChunk), UIMessageChunkTypes.ReasoningStart)]
[JsonDerivedType(typeof(ReasoningDeltaChunk), UIMessageChunkTypes.ReasoningDelta)]
[JsonDerivedType(typeof(ReasoningEndChunk), UIMessageChunkTypes.ReasoningEnd)]
[JsonDerivedType(typeof(ToolInputStartChunk), UIMessageChunkTypes.ToolInputStart)]
[JsonDerivedType(typeof(ToolInputDeltaChunk), UIMessageChunkTypes.ToolInputDelta)]
[JsonDerivedType(typeof(ToolInputAvailableChunk), UIMessageChunkTypes.ToolInputAvailable)]
[JsonDerivedType(typeof(ToolInputErrorChunk), UIMessageChunkTypes.ToolInputError)]
[JsonDerivedType(typeof(ToolOutputAvailableChunk), UIMessageChunkTypes.ToolOutputAvailable)]
[JsonDerivedType(typeof(ToolOutputErrorChunk), UIMessageChunkTypes.ToolOutputError)]
[JsonDerivedType(typeof(ToolOutputDeniedChunk), UIMessageChunkTypes.ToolOutputDenied)]
[JsonDerivedType(typeof(ToolApprovalRequestChunk), UIMessageChunkTypes.ToolApprovalRequest)]
[JsonDerivedType(typeof(SourceUrlChunk), UIMessageChunkTypes.SourceUrl)]
[JsonDerivedType(typeof(SourceDocumentChunk), UIMessageChunkTypes.SourceDocument)]
[JsonDerivedType(typeof(FileChunk), UIMessageChunkTypes.File)]
[JsonDerivedType(typeof(ReasoningFileChunk), UIMessageChunkTypes.ReasoningFile)]
[JsonDerivedType(typeof(ErrorChunk), UIMessageChunkTypes.Error)]
[JsonDerivedType(typeof(CustomChunk), UIMessageChunkTypes.Custom)]
[JsonDerivedType(typeof(AbortChunk), UIMessageChunkTypes.Abort)]
[JsonDerivedType(typeof(MessageMetadataChunk), UIMessageChunkTypes.MessageMetadata)]
internal abstract class UIMessageChunk
{
}

// ---------------------------------------------------------------------------
// Lifecycle chunks
// ---------------------------------------------------------------------------

internal sealed class StartChunk : UIMessageChunk
{
    [JsonPropertyName("messageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MessageId { get; set; }

    [JsonPropertyName("messageMetadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? MessageMetadata { get; set; }
}

internal sealed class FinishChunk : UIMessageChunk
{
    [JsonPropertyName("finishReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishReason { get; set; }

    [JsonPropertyName("messageMetadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? MessageMetadata { get; set; }
}

internal sealed class StartStepChunk : UIMessageChunk
{
}

internal sealed class FinishStepChunk : UIMessageChunk
{
}

internal sealed class AbortChunk : UIMessageChunk
{
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

// ---------------------------------------------------------------------------
// Text chunks
// ---------------------------------------------------------------------------

internal sealed class TextStartChunk : UIMessageChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal sealed class TextDeltaChunk : UIMessageChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("delta")]
    public string Delta { get; set; } = string.Empty;
}

internal sealed class TextEndChunk : UIMessageChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Reasoning chunks
// ---------------------------------------------------------------------------

internal sealed class ReasoningStartChunk : UIMessageChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal sealed class ReasoningDeltaChunk : UIMessageChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("delta")]
    public string Delta { get; set; } = string.Empty;
}

internal sealed class ReasoningEndChunk : UIMessageChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Tool input chunks
// ---------------------------------------------------------------------------

internal sealed class ToolInputStartChunk : UIMessageChunk
{
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("providerExecuted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProviderExecuted { get; set; }
}

internal sealed class ToolInputDeltaChunk : UIMessageChunk
{
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    [JsonPropertyName("inputTextDelta")]
    public string InputTextDelta { get; set; } = string.Empty;
}

internal sealed class ToolInputAvailableChunk : UIMessageChunk
{
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public object? Input { get; set; }

    [JsonPropertyName("providerExecuted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProviderExecuted { get; set; }
}

internal sealed class ToolInputErrorChunk : UIMessageChunk
{
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public object? Input { get; set; }

    [JsonPropertyName("errorText")]
    public string ErrorText { get; set; } = string.Empty;

    [JsonPropertyName("providerExecuted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProviderExecuted { get; set; }
}

// ---------------------------------------------------------------------------
// Tool output chunks
// ---------------------------------------------------------------------------

internal sealed class ToolOutputAvailableChunk : UIMessageChunk
{
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public object? Output { get; set; }

    [JsonPropertyName("providerExecuted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProviderExecuted { get; set; }
}

internal sealed class ToolOutputErrorChunk : UIMessageChunk
{
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    [JsonPropertyName("errorText")]
    public string ErrorText { get; set; } = string.Empty;

    [JsonPropertyName("providerExecuted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProviderExecuted { get; set; }
}

internal sealed class ToolOutputDeniedChunk : UIMessageChunk
{
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Tool approval chunk
// ---------------------------------------------------------------------------

internal sealed class ToolApprovalRequestChunk : UIMessageChunk
{
    [JsonPropertyName("approvalId")]
    public string ApprovalId { get; set; } = string.Empty;

    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Source chunks
// ---------------------------------------------------------------------------

internal sealed class SourceUrlChunk : UIMessageChunk
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }
}

internal sealed class SourceDocumentChunk : UIMessageChunk
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Filename { get; set; }
}

// ---------------------------------------------------------------------------
// File chunks
// ---------------------------------------------------------------------------

internal sealed class FileChunk : UIMessageChunk
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;
}

internal sealed class ReasoningFileChunk : UIMessageChunk
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Other chunks
// ---------------------------------------------------------------------------

internal sealed class ErrorChunk : UIMessageChunk
{
    [JsonPropertyName("errorText")]
    public string ErrorText { get; set; } = string.Empty;
}

internal sealed class CustomChunk : UIMessageChunk
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;
}

internal sealed class MessageMetadataChunk : UIMessageChunk
{
    [JsonPropertyName("messageMetadata")]
    public object? MessageMetadata { get; set; }
}
