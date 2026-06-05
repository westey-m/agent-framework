// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// A <see cref="DelegatingAIAgent"/> that re-invokes the wrapped agent in a loop until the configured
/// <see cref="LoopEvaluator"/> set decides to stop.
/// </summary>
/// <remarks>
/// <para>
/// After each run of the wrapped agent, the configured evaluators are asked whether to re-invoke the agent and what
/// feedback to carry forward. This enables patterns such as iterative refinement, working through a task list, or
/// judging whether the original request was answered. Out-of-the-box evaluators include
/// <see cref="AIJudgeLoopEvaluator"/>, <see cref="CompletionMarkerLoopEvaluator"/>, and
/// <see cref="DelegateLoopEvaluator"/>.
/// </para>
/// <para>
/// When multiple evaluators are supplied they are evaluated in order after each iteration. The first evaluator that
/// asks to re-invoke wins: its feedback drives the next iteration and the remaining evaluators are not evaluated. The
/// loop stops only when every evaluator asks to stop. Consequently, evaluator order is priority order and
/// <see cref="LoopEvaluation.Stop"/> means "this evaluator does not request continuation" rather than a veto that
/// terminates the loop; place stop-only guards accordingly.
/// </para>
/// <para>
/// The caller's initial messages are sent to the wrapped agent exactly once. By default (when
/// <see cref="LoopAgentOptions.FreshContextPerIteration"/> is <see langword="false"/>) the loop reuses a single session
/// and sends only the winning evaluator's feedback as the next input, letting the agent continue from session history.
/// When <see cref="LoopAgentOptions.FreshContextPerIteration"/> is <see langword="true"/>, each re-invocation restarts
/// from the original input messages plus an aggregated feedback log, and a loop-owned session is recreated each
/// iteration. An evaluator may instead supply the exact next messages via
/// <see cref="LoopEvaluation.ContinueWithMessages"/>, bypassing this construction.
/// </para>
/// <para>
/// The loop is bounded by a global safety cap (<see cref="LoopAgentOptions.MaxIterations"/>) regardless of the
/// evaluators. If an iteration produces a pending tool-approval request, the loop stops and returns that response to
/// the caller rather than attempting to resolve the approval automatically.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class LoopAgent : DelegatingAIAgent
{
    /// <summary>The default value used for <see cref="LoopAgentOptions.MaxIterations"/> when none is specified.</summary>
    public const int DefaultMaxIterations = 10;

    private readonly IReadOnlyList<LoopEvaluator> _evaluators;
    private readonly int _maxIterations;
    private readonly bool _freshContextPerIteration;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopAgent"/> class with a single evaluator.
    /// </summary>
    /// <param name="innerAgent">The underlying agent to invoke in a loop.</param>
    /// <param name="evaluator">The <see cref="LoopEvaluator"/> that decides whether to re-invoke the agent.</param>
    /// <param name="options">Optional configuration for the loop. When <see langword="null"/>, defaults are used.</param>
    /// <param name="loggerFactory">Optional factory used to create the loop's logger.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="innerAgent"/> or <paramref name="evaluator"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException"><see cref="LoopAgentOptions.MaxIterations"/> is less than 1.</exception>
    public LoopAgent(AIAgent innerAgent, LoopEvaluator evaluator, LoopAgentOptions? options = null, ILoggerFactory? loggerFactory = null)
        : this(innerAgent, [Throw.IfNull(evaluator)], options, loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopAgent"/> class with one or more evaluators.
    /// </summary>
    /// <param name="innerAgent">The underlying agent to invoke in a loop.</param>
    /// <param name="evaluators">
    /// The ordered set of <see cref="LoopEvaluator"/> that decide whether to re-invoke the agent. They are evaluated in
    /// order after each iteration and the first that asks to re-invoke wins.
    /// </param>
    /// <param name="options">Optional configuration for the loop. When <see langword="null"/>, defaults are used.</param>
    /// <param name="loggerFactory">Optional factory used to create the loop's logger.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="innerAgent"/> or <paramref name="evaluators"/> is <see langword="null"/>, or <paramref name="evaluators"/> contains a <see langword="null"/> element.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="evaluators"/> is empty.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException"><see cref="LoopAgentOptions.MaxIterations"/> is less than 1.</exception>
    public LoopAgent(AIAgent innerAgent, IEnumerable<LoopEvaluator> evaluators, LoopAgentOptions? options = null, ILoggerFactory? loggerFactory = null)
        : base(innerAgent)
    {
        _ = Throw.IfNull(evaluators);
        LoopEvaluator[] evaluatorArray = evaluators.ToArray();
        if (evaluatorArray.Length == 0)
        {
            throw new System.ArgumentException("At least one evaluator must be supplied.", nameof(evaluators));
        }

        foreach (LoopEvaluator item in evaluatorArray)
        {
            _ = Throw.IfNull(item, nameof(evaluators));
        }

        this._evaluators = evaluatorArray;

        this._maxIterations = Throw.IfLessThan(options?.MaxIterations ?? DefaultMaxIterations, 1);
        this._freshContextPerIteration = options?.FreshContextPerIteration ?? false;
        this._logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LoopAgent>();
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        // Capture the caller's initial messages (sent once) and ensure the loop always runs against a session.
        IReadOnlyList<ChatMessage> initialMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        bool sessionProvidedByCaller = session is not null;
        session ??= await this.InnerAgent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        this.WarnIfFreshContextWithCallerSession(sessionProvidedByCaller);

        LoopContext? context = null;
        List<string?> feedbackLog = [];
        IEnumerable<ChatMessage> currentMessages = initialMessages;
        int iteration = 0;

        while (true)
        {
            // Run the wrapped agent using the context's session once it exists (it may have been replaced for a fresh
            // context), otherwise the resolved session for the first run.
            AgentSession activeSession = context?.Session ?? session;
            AgentResponse response = await this.InnerAgent.RunAsync(currentMessages, activeSession, options, cancellationToken).ConfigureAwait(false);
            iteration++;

            // Create the context after the first run (so LastResponse is never null) and reuse it thereafter.
            context ??= new LoopContext(this.InnerAgent, session, initialMessages, response, options) { Feedback = feedbackLog };

            context.Iteration = iteration;
            context.LastResponse = response;

            // Stop and surface the response when the agent is waiting for a tool approval.
            if (HasPendingApprovalRequests(response))
            {
                return response;
            }

            // Enforce the global safety cap regardless of what the evaluators want.
            if (iteration >= this._maxIterations)
            {
                this.LogMaxIterationsReached(iteration);
                return response;
            }

            // Ask the evaluators whether to continue; stop when none of them request a re-invocation.
            LoopNextStep step = await this.EvaluateAndBuildNextAsync(context, feedbackLog, sessionProvidedByCaller, cancellationToken).ConfigureAwait(false);
            if (!step.ShouldContinue)
            {
                return response;
            }

            currentMessages = step.Messages;
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        // Capture the caller's initial messages (sent once) and ensure the loop always runs against a session.
        IReadOnlyList<ChatMessage> initialMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        bool sessionProvidedByCaller = session is not null;
        session ??= await this.InnerAgent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        this.WarnIfFreshContextWithCallerSession(sessionProvidedByCaller);

        LoopContext? context = null;
        List<string?> feedbackLog = [];
        IEnumerable<ChatMessage> currentMessages = initialMessages;
        int iteration = 0;

        while (true)
        {
            // Stream this iteration's updates to the caller while collecting them so the iteration's full
            // response can be aggregated for evaluation (true per-iteration streaming). Uses the context's
            // session once it exists (it may have been replaced for a fresh context), otherwise the resolved session.
            AgentSession activeSession = context?.Session ?? session;
            List<AgentResponseUpdate> updates = [];
            await foreach (var update in this.InnerAgent.RunStreamingAsync(currentMessages, activeSession, options, cancellationToken).ConfigureAwait(false))
            {
                updates.Add(update);
                yield return update;
            }

            // Aggregate this iteration's updates and record the result on the context.
            iteration++;
            AgentResponse response = updates.ToAgentResponse();

            // Create the context after the first run (so LastResponse is never null) and reuse it thereafter.
            context ??= new LoopContext(this.InnerAgent, session, initialMessages, response, options) { Feedback = feedbackLog };

            context.Iteration = iteration;
            context.LastResponse = response;

            // Stop when the agent is waiting for a tool approval.
            if (HasPendingApprovalRequests(response))
            {
                yield break;
            }

            // Enforce the global safety cap regardless of what the evaluators want.
            if (iteration >= this._maxIterations)
            {
                this.LogMaxIterationsReached(iteration);
                yield break;
            }

            // Ask the evaluators whether to continue; stop when none of them request a re-invocation.
            LoopNextStep step = await this.EvaluateAndBuildNextAsync(context, feedbackLog, sessionProvidedByCaller, cancellationToken).ConfigureAwait(false);
            if (!step.ShouldContinue)
            {
                yield break;
            }

            currentMessages = step.Messages;
        }
    }

    /// <summary>
    /// Evaluates the evaluators in order and, for the first one that requests a re-invocation, builds the next input
    /// according to the loop's feedback and fresh-context policy.
    /// </summary>
    private async ValueTask<LoopNextStep> EvaluateAndBuildNextAsync(LoopContext context, List<string?> feedbackLog, bool sessionProvidedByCaller, CancellationToken cancellationToken)
    {
        // Evaluate in order; the first evaluator that requests a re-invocation wins.
        LoopEvaluation? winner = null;
        foreach (LoopEvaluator evaluator in this._evaluators)
        {
            LoopEvaluation evaluation = await evaluator.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            if (evaluation.ShouldReinvoke)
            {
                winner = evaluation;
                break;
            }
        }

        // Every evaluator asked to stop.
        if (winner is null)
        {
            return LoopNextStep.Stop();
        }

        // An evaluator supplied explicit messages: send them verbatim, bypassing feedback/fresh construction.
        if (winner.Messages is not null)
        {
            return LoopNextStep.Continue(winner.Messages);
        }

        // Record one feedback entry for this re-invoked iteration (null when none) so the last element always
        // corresponds to the latest re-invoked iteration.
        feedbackLog.Add(string.IsNullOrWhiteSpace(winner.Feedback) ? null : winner.Feedback);

        // Start the next iteration from a brand-new session when a fresh context is requested and the loop owns the
        // session, so no prior conversation history leaks across iterations.
        if (this._freshContextPerIteration && !sessionProvidedByCaller)
        {
            context.Session = await context.Agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        return LoopNextStep.Continue(this.BuildNextMessages(context, feedbackLog));
    }

    private List<ChatMessage> BuildNextMessages(LoopContext context, List<string?> feedback)
    {
        var messages = new List<ChatMessage>();

        if (this._freshContextPerIteration)
        {
            // Fresh context: re-send the original task plus an aggregated log of all feedback recorded so far.
            messages.AddRange(context.InitialMessages);

            ChatMessage? feedbackMessage = BuildAggregatedFeedbackMessage(feedback);
            if (feedbackMessage is not null)
            {
                messages.Add(feedbackMessage);
            }
        }
        else
        {
            // Reused session: send only the latest feedback verbatim (the session already retains earlier turns). When
            // the latest iteration produced no feedback, send no messages and let the agent continue from history.
            string? latest = feedback.Count > 0 ? feedback[feedback.Count - 1] : null;
            if (!string.IsNullOrWhiteSpace(latest))
            {
                messages.Add(new ChatMessage(ChatRole.User, latest));
            }
        }

        return messages;
    }

    private static ChatMessage? BuildAggregatedFeedbackMessage(IReadOnlyList<string?> feedback)
    {
        var body = new StringBuilder("## Feedback\n");
        bool any = false;
        foreach (string? entry in feedback)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                body.Append("\n- ").Append(entry);
                any = true;
            }
        }

        return any ? new ChatMessage(ChatRole.User, body.ToString()) : null;
    }

    private static bool HasPendingApprovalRequests(AgentResponse response)
    {
        foreach (ChatMessage message in response.Messages)
        {
            foreach (AIContent content in message.Contents)
            {
                if (content is ToolApprovalRequestContent)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void LogMaxIterationsReached(int iteration)
    {
        if (this._logger.IsEnabled(LogLevel.Information))
        {
            this._logger.LogInformation("LoopAgent reached the maximum of {MaxIterations} iterations and stopped.", iteration);
        }
    }

    /// <summary>
    /// Warns when a fresh context is requested but the caller owns the session: the message input is replayed fresh
    /// each iteration, but the session (and its history) is not replaced.
    /// </summary>
    private void WarnIfFreshContextWithCallerSession(bool sessionProvidedByCaller)
    {
        if (this._freshContextPerIteration && sessionProvidedByCaller && this._logger.IsEnabled(LogLevel.Warning))
        {
            this._logger.LogWarning(
                "LoopAgent.FreshContextPerIteration rebuilds the input messages each iteration but does not replace a " +
                "caller-supplied session, so the session may still retain conversation history across iterations. For a " +
                "truly fresh context per iteration, run the loop without supplying a session.");
        }
    }

    /// <summary>Represents the loop's decision for the next iteration: stop, or continue with a set of messages.</summary>
    private readonly struct LoopNextStep
    {
        private LoopNextStep(bool shouldContinue, IReadOnlyList<ChatMessage> messages)
        {
            this.ShouldContinue = shouldContinue;
            this.Messages = messages;
        }

        public bool ShouldContinue { get; }

        public IReadOnlyList<ChatMessage> Messages { get; }

        public static LoopNextStep Stop() => new(shouldContinue: false, []);

        public static LoopNextStep Continue(IReadOnlyList<ChatMessage> messages) => new(shouldContinue: true, messages);
    }
}
