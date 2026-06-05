// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Unit tests for the <see cref="LoopEvaluation"/> class.
/// </summary>
public class LoopEvaluationTests
{
    /// <summary>
    /// Verify that Stop produces an evaluation that does not re-invoke and carries no feedback.
    /// </summary>
    [Fact]
    public void Stop_DoesNotReinvoke_AndHasNoFeedback()
    {
        // Act
        var evaluation = LoopEvaluation.Stop();

        // Assert
        Assert.False(evaluation.ShouldReinvoke);
        Assert.Null(evaluation.Feedback);
    }

    /// <summary>
    /// Verify that Continue with no argument re-invokes and carries no feedback.
    /// </summary>
    [Fact]
    public void Continue_NoFeedback_ReinvokesWithNullFeedback()
    {
        // Act
        var evaluation = LoopEvaluation.Continue();

        // Assert
        Assert.True(evaluation.ShouldReinvoke);
        Assert.Null(evaluation.Feedback);
    }

    /// <summary>
    /// Verify that Continue with feedback re-invokes and carries the supplied feedback.
    /// </summary>
    [Fact]
    public void Continue_WithFeedback_ReinvokesWithFeedback()
    {
        // Act
        var evaluation = LoopEvaluation.Continue("do more work");

        // Assert
        Assert.True(evaluation.ShouldReinvoke);
        Assert.Equal("do more work", evaluation.Feedback);
    }
}
