// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Contains extension methods for <see cref="ChatMessage"/>
/// </summary>
public static class ChatMessageExtensions
{
    /// <summary>
    /// Gets the source type of the provided <see cref="ChatMessage"/> in the context of messages passed into an agent run.
    /// </summary>
    /// <param name="message">The <see cref="ChatMessage"/> for which we need the source type.</param>
    /// <returns>An <see cref="AgentRequestMessageSourceType"/> value indicating the source type of the <see cref="ChatMessage"/>. Defaults to <see
    /// cref="AgentRequestMessageSourceType.External"/> if no explicit source is defined.</returns>
    public static AgentRequestMessageSourceType GetAgentRequestMessageSourceType(this ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, out var attribution) is true
            && attribution is AgentRequestMessageSourceAttribution typedAttribution)
        {
            return typedAttribution.SourceType;
        }

        return AgentRequestMessageSourceType.External;
    }

    /// <summary>
    /// Gets the source id of the provided <see cref="ChatMessage"/> in the context of messages passed into an agent run.
    /// </summary>
    /// <param name="message">The <see cref="ChatMessage"/> for which we need the source id.</param>
    /// <returns>An <see cref="string"/> value indicating the source id of the <see cref="ChatMessage"/>. Defaults to <see langword="null"/>
    /// if no explicit source id is defined.</returns>
    public static string? GetAgentRequestMessageSourceId(this ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, out var attribution) is true
            && attribution is AgentRequestMessageSourceAttribution typedAttribution)
        {
            return typedAttribution.SourceId;
        }

        return null;
    }
}
