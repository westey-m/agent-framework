// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides configuration options for <see cref="LoopAgent"/>.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class LoopAgentOptions
{
    /// <summary>
    /// Gets or sets the global safety cap on the number of times the wrapped agent is invoked in a single loop run,
    /// or <see langword="null"/> to use <see cref="LoopAgent.DefaultMaxIterations"/>.
    /// </summary>
    /// <remarks>
    /// This is an absolute upper bound that applies regardless of the configured <see cref="LoopEvaluator"/> set. An
    /// evaluator may stop the loop earlier, but no evaluator can cause the loop to exceed this cap, so raise this value
    /// if you intend to allow longer loops.
    /// </remarks>
    public int? MaxIterations { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether each re-invocation restarts from a clean context: the original input
    /// messages plus an aggregated feedback log, rather than the latest feedback appended to the prior conversation.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// This rebuilds the input <em>messages</em> each iteration. <see cref="LoopAgent"/> additionally creates a new
    /// session per iteration only when the loop owns the session; when the caller supplies a session it is reused (and
    /// a warning is logged), so the agent or its providers may still retain conversation history. For a truly fresh
    /// context per iteration, run the loop without supplying a session. This setting has no effect on iterations where
    /// an evaluator returns explicit messages via <see cref="LoopEvaluation.ContinueWithMessages"/>.
    /// </remarks>
    public bool FreshContextPerIteration { get; set; }
}
