// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting;

/// <summary>
/// This store implementation does not have any store under the hood and operates with empty threads.
/// It is the "noop" store, and could be used if you are keeping the session contents on the client side for example.
/// </summary>
public sealed class NoopAgentSessionStore : AgentSessionStore
{
    /// <inheritdoc/>
    public override ValueTask SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, CancellationToken cancellationToken = default)
    {
        return new ValueTask();
    }

    /// <inheritdoc/>
    public override ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId, CancellationToken cancellationToken = default)
    {
        return agent.GetNewSessionAsync(cancellationToken);
    }
}
