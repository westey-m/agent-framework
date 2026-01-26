// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>Provides context for an in-flight agent run.</summary>
public class AgentRunContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRunContext"/> class.
    /// </summary>
    /// <param name="agent">The <see cref="AIAgent"/> that is executing the current run.</param>
    /// <param name="thread">The <see cref="AgentThread"/> that is associated with the current run.</param>
    public AgentRunContext(AIAgent agent, AgentThread thread)
    {
        this.Agent = Throw.IfNull(agent);
        this.Thread = Throw.IfNull(thread);
    }

    /// <summary>Gets or sets the <see cref="AIAgent"/> that is executing the current run.</summary>
    public AIAgent Agent
    {
        get;
        private set => field = Throw.IfNull(value);
    }

    /// <summary>Gets or sets the <see cref="AgentThread"/> that is associated with the current run.</summary>
    public AgentThread Thread
    {
        get;
        private set => field = Throw.IfNull(value);
    }

    /// <summary>Gets or sets the <see cref="AgentRunOptions"/> that was passed to the current run.</summary>
    public AgentRunOptions? RunOptions
    {
        get;
        set;
    }
}
