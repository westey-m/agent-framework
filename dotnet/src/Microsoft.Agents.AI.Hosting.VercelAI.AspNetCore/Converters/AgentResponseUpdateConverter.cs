// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Converters;

/// <summary>
/// Converts a stream of <see cref="AgentResponseUpdate"/> from the Agent Framework
/// into a stream of <see cref="UIMessageChunk"/> in the Vercel AI SDK UI Message Stream format.
/// </summary>
internal static class AgentResponseUpdateConverter
{
    /// <summary>
    /// Transforms an <see cref="IAsyncEnumerable{AgentResponseUpdate}"/> into an
    /// <see cref="IAsyncEnumerable{UIMessageChunk}"/> compatible with the Vercel AI SDK.
    /// </summary>
    /// <remarks>
    /// Step boundaries are detected via <see cref="AgentResponseUpdate.FinishReason"/>.
    /// Each non-null finish reason closes the current step, so multi-step tool-call flows
    /// (LLM → tool → LLM) emit separate <c>start-step</c>/<c>finish-step</c> pairs per
    /// LLM round-trip as required by the Vercel AI SDK stream protocol.
    /// </remarks>
    internal static async IAsyncEnumerable<UIMessageChunk> AsVercelAIChunkStreamAsync(
        this IAsyncEnumerable<AgentResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageId = Guid.NewGuid().ToString("N");
        var textPartId = Guid.NewGuid().ToString("N");
        var reasoningPartId = Guid.NewGuid().ToString("N");

        // Track state for text/reasoning streaming and step boundaries
        bool textStarted = false;
        bool reasoningStarted = false;
        bool insideStep = false;
        ChatFinishReason? lastFinishReason = null;

        // Emit start
        yield return new StartChunk { MessageId = messageId };

        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                        // Close any open text part before reasoning
                        if (textStarted)
                        {
                            yield return new TextEndChunk { Id = textPartId };
                            textStarted = false;
                            textPartId = Guid.NewGuid().ToString("N");
                        }

                        if (!insideStep)
                        {
                            yield return new StartStepChunk();
                            insideStep = true;
                        }

                        if (!reasoningStarted)
                        {
                            yield return new ReasoningStartChunk { Id = reasoningPartId };
                            reasoningStarted = true;
                        }

                        yield return new ReasoningDeltaChunk { Id = reasoningPartId, Delta = reasoning.Text };
                        break;

                    case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                        // Close any open reasoning part before text
                        if (reasoningStarted)
                        {
                            yield return new ReasoningEndChunk { Id = reasoningPartId };
                            reasoningStarted = false;
                            reasoningPartId = Guid.NewGuid().ToString("N");
                        }

                        if (!insideStep)
                        {
                            yield return new StartStepChunk();
                            insideStep = true;
                        }

                        if (!textStarted)
                        {
                            yield return new TextStartChunk { Id = textPartId };
                            textStarted = true;
                        }

                        yield return new TextDeltaChunk { Id = textPartId, Delta = textContent.Text };
                        break;

                    case FunctionCallContent functionCall:
                        // Close any open text or reasoning part before tool events
                        if (textStarted)
                        {
                            yield return new TextEndChunk { Id = textPartId };
                            textStarted = false;
                            textPartId = Guid.NewGuid().ToString("N");
                        }

                        if (reasoningStarted)
                        {
                            yield return new ReasoningEndChunk { Id = reasoningPartId };
                            reasoningStarted = false;
                            reasoningPartId = Guid.NewGuid().ToString("N");
                        }

                        if (!insideStep)
                        {
                            yield return new StartStepChunk();
                            insideStep = true;
                        }

                        yield return new ToolInputStartChunk
                        {
                            ToolCallId = functionCall.CallId,
                            ToolName = functionCall.Name,
                        };

                        // Emit the full arguments as tool-input-available
                        object? inputObject = null;
                        if (functionCall.Arguments is { Count: > 0 })
                        {
                            inputObject = functionCall.Arguments;
                        }

                        yield return new ToolInputAvailableChunk
                        {
                            ToolCallId = functionCall.CallId,
                            ToolName = functionCall.Name,
                            Input = inputObject,
                        };
                        break;

                    case FunctionResultContent functionResult:
                        if (functionResult.Exception is not null)
                        {
                            yield return new ToolOutputErrorChunk
                            {
                                ToolCallId = functionResult.CallId,
                                ErrorText = functionResult.Exception.Message,
                            };
                        }
                        else
                        {
                            object? outputObject = functionResult.Result;

                            // Try to parse string results as JSON for richer client-side rendering
                            if (outputObject is string resultString)
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(resultString);
                                    outputObject = doc.RootElement.Clone();
                                }
                                catch (JsonException)
                                {
                                    // Keep as string if not valid JSON
                                }
                            }

                            yield return new ToolOutputAvailableChunk
                            {
                                ToolCallId = functionResult.CallId,
                                Output = outputObject,
                            };
                        }

                        break;

                    case DataContent dataContent when dataContent.MediaType is not null:
                        if (!insideStep)
                        {
                            yield return new StartStepChunk();
                            insideStep = true;
                        }

                        if (textStarted)
                        {
                            yield return new TextEndChunk { Id = textPartId };
                            textStarted = false;
                            textPartId = Guid.NewGuid().ToString("N");
                        }

                        if (reasoningStarted)
                        {
                            yield return new ReasoningEndChunk { Id = reasoningPartId };
                            reasoningStarted = false;
                            reasoningPartId = Guid.NewGuid().ToString("N");
                        }

                        yield return new FileChunk
                        {
                            Url = dataContent.Uri?.ToString() ?? string.Empty,
                            MediaType = dataContent.MediaType,
                        };
                        break;

                    case UriContent uriContent when uriContent.Uri is not null:
                        if (!insideStep)
                        {
                            yield return new StartStepChunk();
                            insideStep = true;
                        }

                        if (textStarted)
                        {
                            yield return new TextEndChunk { Id = textPartId };
                            textStarted = false;
                            textPartId = Guid.NewGuid().ToString("N");
                        }

                        if (reasoningStarted)
                        {
                            yield return new ReasoningEndChunk { Id = reasoningPartId };
                            reasoningStarted = false;
                            reasoningPartId = Guid.NewGuid().ToString("N");
                        }

                        yield return new FileChunk
                        {
                            Url = uriContent.Uri.ToString(),
                            MediaType = uriContent.MediaType ?? "application/octet-stream",
                        };
                        break;
                }
            }

            // Detect step boundaries: when FinishReason is set, the current LLM call has ended
            if (update.FinishReason is not null)
            {
                lastFinishReason = update.FinishReason;

                if (textStarted)
                {
                    yield return new TextEndChunk { Id = textPartId };
                    textStarted = false;
                    textPartId = Guid.NewGuid().ToString("N");
                }

                if (reasoningStarted)
                {
                    yield return new ReasoningEndChunk { Id = reasoningPartId };
                    reasoningStarted = false;
                    reasoningPartId = Guid.NewGuid().ToString("N");
                }

                if (insideStep)
                {
                    yield return new FinishStepChunk();
                    insideStep = false;
                }
            }
        }

        // Close any open text or reasoning part (safety net for streams without FinishReason)
        if (textStarted)
        {
            yield return new TextEndChunk { Id = textPartId };
        }

        if (reasoningStarted)
        {
            yield return new ReasoningEndChunk { Id = reasoningPartId };
        }

        // Close step if still open
        if (insideStep)
        {
            yield return new FinishStepChunk();
        }

        // Emit finish with the actual finish reason from the stream
        yield return new FinishChunk { FinishReason = lastFinishReason?.Value ?? "stop" };
    }
}
