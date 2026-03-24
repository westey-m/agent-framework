// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;

/// <summary>
/// String constants for the <c>type</c> discriminator field on Vercel AI SDK UI Message Chunks.
/// </summary>
internal static class UIMessageChunkTypes
{
    // Lifecycle
    internal const string Start = "start";
    internal const string Finish = "finish";
    internal const string StartStep = "start-step";
    internal const string FinishStep = "finish-step";
    internal const string Abort = "abort";

    // Text
    internal const string TextStart = "text-start";
    internal const string TextDelta = "text-delta";
    internal const string TextEnd = "text-end";

    // Reasoning
    internal const string ReasoningStart = "reasoning-start";
    internal const string ReasoningDelta = "reasoning-delta";
    internal const string ReasoningEnd = "reasoning-end";

    // Tool input
    internal const string ToolInputStart = "tool-input-start";
    internal const string ToolInputDelta = "tool-input-delta";
    internal const string ToolInputAvailable = "tool-input-available";
    internal const string ToolInputError = "tool-input-error";

    // Tool output
    internal const string ToolOutputAvailable = "tool-output-available";
    internal const string ToolOutputError = "tool-output-error";
    internal const string ToolOutputDenied = "tool-output-denied";

    // Tool approval
    internal const string ToolApprovalRequest = "tool-approval-request";

    // Sources
    internal const string SourceUrl = "source-url";
    internal const string SourceDocument = "source-document";

    // Files
    internal const string File = "file";
    internal const string ReasoningFile = "reasoning-file";

    // Other
    internal const string Error = "error";
    internal const string Custom = "custom";
    internal const string MessageMetadata = "message-metadata";
}
