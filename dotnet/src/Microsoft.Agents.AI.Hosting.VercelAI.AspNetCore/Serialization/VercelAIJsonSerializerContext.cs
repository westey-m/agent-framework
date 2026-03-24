// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Serialization;

/// <summary>
/// Source-generated JSON serialization context for the Vercel AI SDK protocol types.
/// Enables AOT-safe serialization without runtime reflection.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VercelAIChatRequest))]
[JsonSerializable(typeof(VercelAIMessage))]
[JsonSerializable(typeof(VercelAIMessagePart))]
[JsonSerializable(typeof(UIMessageChunk))]
[JsonSerializable(typeof(StartChunk))]
[JsonSerializable(typeof(FinishChunk))]
[JsonSerializable(typeof(StartStepChunk))]
[JsonSerializable(typeof(FinishStepChunk))]
[JsonSerializable(typeof(AbortChunk))]
[JsonSerializable(typeof(TextStartChunk))]
[JsonSerializable(typeof(TextDeltaChunk))]
[JsonSerializable(typeof(TextEndChunk))]
[JsonSerializable(typeof(ReasoningStartChunk))]
[JsonSerializable(typeof(ReasoningDeltaChunk))]
[JsonSerializable(typeof(ReasoningEndChunk))]
[JsonSerializable(typeof(ToolInputStartChunk))]
[JsonSerializable(typeof(ToolInputDeltaChunk))]
[JsonSerializable(typeof(ToolInputAvailableChunk))]
[JsonSerializable(typeof(ToolInputErrorChunk))]
[JsonSerializable(typeof(ToolOutputAvailableChunk))]
[JsonSerializable(typeof(ToolOutputErrorChunk))]
[JsonSerializable(typeof(ToolOutputDeniedChunk))]
[JsonSerializable(typeof(ToolApprovalRequestChunk))]
[JsonSerializable(typeof(SourceUrlChunk))]
[JsonSerializable(typeof(SourceDocumentChunk))]
[JsonSerializable(typeof(FileChunk))]
[JsonSerializable(typeof(ReasoningFileChunk))]
[JsonSerializable(typeof(ErrorChunk))]
[JsonSerializable(typeof(CustomChunk))]
[JsonSerializable(typeof(MessageMetadataChunk))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal sealed partial class VercelAIJsonSerializerContext : JsonSerializerContext
{
    /// <summary>
    /// Gets a <see cref="VercelAIJsonSerializerContext"/> instance configured with
    /// relaxed options suitable for deserializing incoming requests (case-insensitive, etc.).
    /// </summary>
    internal static VercelAIJsonSerializerContext Relaxed
    {
        get => field ??= new VercelAIJsonSerializerContext(
        new JsonSerializerOptions(Default.Options)
        {
            PropertyNameCaseInsensitive = true,
        });

        private set;
    }
}
