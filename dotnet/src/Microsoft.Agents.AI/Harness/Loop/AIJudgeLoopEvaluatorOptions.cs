// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides configuration options for <see cref="AIJudgeLoopEvaluator"/>.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class AIJudgeLoopEvaluatorOptions
{
    /// <summary>
    /// Gets or sets the system instructions used to prompt the judge, or <see langword="null"/> to use
    /// <see cref="AIJudgeLoopEvaluator.DefaultInstructions"/>.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Gets or sets the template used to build the feedback produced when the judge decides the original request was
    /// not fully addressed, or <see langword="null"/> to use
    /// <see cref="AIJudgeLoopEvaluator.DefaultFeedbackMessageTemplate"/>.
    /// </summary>
    /// <remarks>
    /// Any occurrence of <see cref="AIJudgeLoopEvaluator.GapAnalysisPlaceholder"/> in the template is replaced with the
    /// judge's gap analysis (or a placeholder when none is available).
    /// </remarks>
    public string? FeedbackMessageTemplate { get; set; }
}
