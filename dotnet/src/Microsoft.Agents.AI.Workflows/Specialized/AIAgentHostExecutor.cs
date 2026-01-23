// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Workflows.Specialized;

internal sealed class AIAgentHostExecutor : ChatProtocolExecutor
{
    private readonly bool _emitEvents;
    private readonly AIAgent _agent;
    private AgentSession? _session;

    public AIAgentHostExecutor(AIAgent agent, bool emitEvents = false) : base(id: agent.GetDescriptiveId())
    {
        this._agent = agent;
        this._emitEvents = emitEvents;
    }

    private async Task<AgentSession> EnsureSessionAsync(IWorkflowContext context, CancellationToken cancellationToken) =>
        this._session ??= await this._agent.GetNewSessionAsync(cancellationToken).ConfigureAwait(false);

    private const string SessionStateKey = nameof(_session);
    protected internal override async ValueTask OnCheckpointingAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        Task sessionTask = Task.CompletedTask;
        if (this._session is not null)
        {
            JsonElement sessionValue = this._session.Serialize();
            sessionTask = context.QueueStateUpdateAsync(SessionStateKey, sessionValue, cancellationToken: cancellationToken).AsTask();
        }

        Task baseTask = base.OnCheckpointingAsync(context, cancellationToken).AsTask();

        await Task.WhenAll(sessionTask, baseTask).ConfigureAwait(false);
    }

    protected internal override async ValueTask OnCheckpointRestoredAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        JsonElement? sessionValue = await context.ReadStateAsync<JsonElement?>(SessionStateKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (sessionValue.HasValue)
        {
            this._session = await this._agent.DeserializeSessionAsync(sessionValue.Value, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await base.OnCheckpointRestoredAsync(context, cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask TakeTurnAsync(List<ChatMessage> messages, IWorkflowContext context, bool? emitEvents, CancellationToken cancellationToken = default)
    {
        if (emitEvents ?? this._emitEvents)
        {
            // Run the agent in streaming mode only when agent run update events are to be emitted.
            IAsyncEnumerable<AgentResponseUpdate> agentStream = this._agent.RunStreamingAsync(
                messages,
                await this.EnsureSessionAsync(context, cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken);

            List<AgentResponseUpdate> updates = [];

            await foreach (AgentResponseUpdate update in agentStream.ConfigureAwait(false))
            {
                await context.AddEventAsync(new AgentResponseUpdateEvent(this.Id, update), cancellationToken).ConfigureAwait(false);

                // TODO: FunctionCall request handling, and user info request handling.
                // In some sense: We should just let it be handled as a ChatMessage, though we should consider
                // providing some mechanisms to help the user complete the request, or route it out of the
                // workflow.
                updates.Add(update);
            }

            await context.SendMessageAsync(updates.ToAgentResponse().Messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Otherwise, run the agent in non-streaming mode.
            AgentResponse response = await this._agent.RunAsync(
                messages,
                await this.EnsureSessionAsync(context, cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await context.SendMessageAsync(response.Messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
