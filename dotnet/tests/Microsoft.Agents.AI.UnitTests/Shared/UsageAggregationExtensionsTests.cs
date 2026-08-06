// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Unit tests for <see cref="UsageAggregationExtensions"/>.
/// </summary>
public class UsageAggregationExtensionsTests
{
    /// <summary>
    /// Verify that merging two null usage values returns null.
    /// </summary>
    [Fact]
    public void MergeUsage_BothInputsNull_ReturnsNull()
    {
        // Arrange, Act
        UsageDetails? result = UsageAggregationExtensions.MergeUsage(null, null);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verify that merging one null usage value returns a new copy of the non-null usage value.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MergeUsage_OneInputNull_ReturnsNewCopy(bool currentIsNull)
    {
        // Arrange
        UsageDetails usage = CreateUsage(2, 3, 5, new() { ["cached"] = 7 });
        UsageSnapshot before = UsageSnapshot.Capture(usage);

        // Act
        UsageDetails? result = currentIsNull
            ? UsageAggregationExtensions.MergeUsage(null, usage)
            : UsageAggregationExtensions.MergeUsage(usage, null);

        // Assert
        Assert.NotNull(result);
        Assert.NotSame(usage, result);
        Assert.NotSame(usage.AdditionalCounts, result!.AdditionalCounts);
        Assert.Equal(2, result.InputTokenCount);
        Assert.Equal(3, result.OutputTokenCount);
        Assert.Equal(5, result.TotalTokenCount);
        Assert.Equal(7, result.AdditionalCounts!["cached"]);
        Assert.Equal(before, UsageSnapshot.Capture(usage));
    }

    /// <summary>
    /// Verify that token counts are summed while preserving null as not reported.
    /// </summary>
    [Fact]
    public void MergeUsage_TokenCounts_SumsNullAware()
    {
        // Arrange
        UsageDetails current = CreateUsage(input: 2, output: null, total: null);
        UsageDetails incoming = CreateUsage(input: 11, output: 5, total: null);

        // Act
        UsageDetails? result = UsageAggregationExtensions.MergeUsage(current, incoming);

        // Assert
        Assert.NotNull(result);
        Assert.NotSame(current, result);
        Assert.NotSame(incoming, result);
        Assert.Equal(13, result!.InputTokenCount);
        Assert.Equal(5, result.OutputTokenCount);
        Assert.Null(result.TotalTokenCount);
    }

    /// <summary>
    /// Verify that additional counts are summed per key and preserve disjoint keys.
    /// </summary>
    [Fact]
    public void MergeUsage_AdditionalCounts_SumsOverlappingAndUnionsDisjointKeys()
    {
        // Arrange
        UsageDetails current = CreateUsage(input: null, output: null, total: null, new() { ["cached"] = 2, ["reasoning"] = 3 });
        UsageDetails incoming = CreateUsage(input: null, output: null, total: null, new() { ["cached"] = 11, ["audio"] = 29 });

        // Act
        UsageDetails? result = UsageAggregationExtensions.MergeUsage(current, incoming);

        // Assert
        Assert.NotNull(result);
        Assert.NotSame(current.AdditionalCounts, result!.AdditionalCounts);
        Assert.NotSame(incoming.AdditionalCounts, result.AdditionalCounts);
        Assert.Equal(13, result.AdditionalCounts!["cached"]);
        Assert.Equal(3, result.AdditionalCounts["reasoning"]);
        Assert.Equal(29, result.AdditionalCounts["audio"]);
    }

    /// <summary>
    /// Verify that additional counts are handled when one or both sides do not report any keys.
    /// </summary>
    [Fact]
    public void MergeUsage_AdditionalCounts_HandlesNullDictionaries()
    {
        // Arrange
        UsageDetails withCounts = CreateUsage(input: null, output: null, total: null, new() { ["cached"] = 7 });
        UsageDetails withoutCounts = CreateUsage(input: 1, output: 2, total: 3);

        // Act
        UsageDetails? oneSide = UsageAggregationExtensions.MergeUsage(withoutCounts, withCounts);
        UsageDetails? bothSides = UsageAggregationExtensions.MergeUsage(withoutCounts, CreateUsage(input: null, output: null, total: null));

        // Assert
        Assert.NotNull(oneSide);
        Assert.Equal(7, oneSide!.AdditionalCounts!["cached"]);
        Assert.NotNull(bothSides);
        Assert.Null(bothSides!.AdditionalCounts);
    }

    /// <summary>
    /// Verify that merging does not mutate either input usage or additional-count dictionary.
    /// </summary>
    [Fact]
    public void MergeUsage_DoesNotMutateInputs()
    {
        // Arrange
        UsageDetails current = CreateUsage(2, 3, 5, new() { ["cached"] = 7, ["reasoning"] = 11 });
        UsageDetails incoming = CreateUsage(13, null, 17, new() { ["cached"] = 19, ["audio"] = 23 });
        UsageSnapshot currentBefore = UsageSnapshot.Capture(current);
        UsageSnapshot incomingBefore = UsageSnapshot.Capture(incoming);

        // Act
        UsageDetails? result = UsageAggregationExtensions.MergeUsage(current, incoming);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(currentBefore, UsageSnapshot.Capture(current));
        Assert.Equal(incomingBefore, UsageSnapshot.Capture(incoming));
    }

    /// <summary>
    /// Verify that accumulating usage replaces the running aggregate with new combined instances.
    /// </summary>
    [Fact]
    public void AccumulateUsage_SeveralAccumulations_UpdatesAggregate()
    {
        // Arrange
        UsageDetails? aggregate = null;
        UsageDetails first = CreateUsage(2, 3, 5, new() { ["cached"] = 7 });
        UsageDetails second = CreateUsage(11, null, 16, new() { ["cached"] = 13, ["audio"] = 17 });
        UsageDetails third = CreateUsage(null, 29, null);

        // Act
        UsageAggregationExtensions.AccumulateUsage(ref aggregate, first);
        UsageDetails firstAggregate = aggregate!;
        UsageAggregationExtensions.AccumulateUsage(ref aggregate, null);
        UsageDetails secondAggregate = aggregate!;
        UsageAggregationExtensions.AccumulateUsage(ref aggregate, second);
        UsageAggregationExtensions.AccumulateUsage(ref aggregate, third);

        // Assert
        Assert.NotSame(first, firstAggregate);
        Assert.NotSame(firstAggregate, secondAggregate);
        Assert.Equal(13, aggregate!.InputTokenCount);
        Assert.Equal(32, aggregate.OutputTokenCount);
        Assert.Equal(21, aggregate.TotalTokenCount);
        Assert.Equal(20, aggregate.AdditionalCounts!["cached"]);
        Assert.Equal(17, aggregate.AdditionalCounts["audio"]);
    }

    /// <summary>
    /// Verify that every strongly-typed counter exposed by <see cref="UsageDetails"/> is summed, not just the
    /// three headline token counts. Providers such as the GitHub Copilot agent report
    /// <see cref="UsageDetails.CachedInputTokenCount"/>, and reasoning tokens are common for OpenAI-family
    /// models, so dropping any of these would silently lose provider-reported data.
    /// </summary>
    [Fact]
    public void MergeUsage_SumsAllStronglyTypedCounters()
    {
        // Arrange
        UsageDetails current = new()
        {
            InputTokenCount = 1,
            OutputTokenCount = 2,
            TotalTokenCount = 3,
            CachedInputTokenCount = 4,
            ReasoningTokenCount = 5,
            InputAudioTokenCount = 6,
            InputTextTokenCount = 7,
            OutputAudioTokenCount = 8,
            OutputTextTokenCount = 9,
        };
        UsageDetails incoming = new()
        {
            InputTokenCount = 10,
            OutputTokenCount = 20,
            TotalTokenCount = 30,
            CachedInputTokenCount = 40,
            ReasoningTokenCount = 50,
            InputAudioTokenCount = 60,
            InputTextTokenCount = 70,
            OutputAudioTokenCount = 80,
            OutputTextTokenCount = 90,
        };

        // Act
        UsageDetails? result = UsageAggregationExtensions.MergeUsage(current, incoming);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(11, result!.InputTokenCount);
        Assert.Equal(22, result.OutputTokenCount);
        Assert.Equal(33, result.TotalTokenCount);
        Assert.Equal(44, result.CachedInputTokenCount);
        Assert.Equal(55, result.ReasoningTokenCount);
        Assert.Equal(66, result.InputAudioTokenCount);
        Assert.Equal(77, result.InputTextTokenCount);
        Assert.Equal(88, result.OutputAudioTokenCount);
        Assert.Equal(99, result.OutputTextTokenCount);
    }

    /// <summary>
    /// Verify that the extra strongly-typed counters survive a merge where only one side reports them, which
    /// is the common case when a single iteration of a loop reports cached or reasoning tokens.
    /// </summary>
    [Fact]
    public void MergeUsage_OneSideOnlyReportsExtraCounters_PreservesThem()
    {
        // Arrange
        UsageDetails current = new() { InputTokenCount = 5 };
        UsageDetails incoming = new() { InputTokenCount = 6, CachedInputTokenCount = 3, ReasoningTokenCount = 4 };

        // Act
        UsageDetails? result = UsageAggregationExtensions.MergeUsage(current, incoming);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(11, result!.InputTokenCount);
        Assert.Equal(3, result.CachedInputTokenCount);
        Assert.Equal(4, result.ReasoningTokenCount);
    }

    /// <summary>
    /// <see cref="UsageAggregationExtensions.MergeUsage"/> intentionally mirrors the semantics of
    /// <see cref="UsageDetails.Add"/> (which is what <c>FunctionInvokingChatClient</c> uses to aggregate usage
    /// across its own function-calling turns) while avoiding that method's in-place mutation. This asserts the
    /// two agree for every combination of reported and unreported counters.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MergeUsage_MatchesUsageDetailsAddSemantics(bool currentReported, bool incomingReported)
    {
        // Arrange
        UsageDetails? current = currentReported ? CreateFullyPopulatedUsage(1) : null;
        UsageDetails? incoming = incomingReported ? CreateFullyPopulatedUsage(100) : null;

        UsageDetails expected = new();
        if (current is not null)
        {
            expected.Add(current);
        }

        if (incoming is not null)
        {
            expected.Add(incoming);
        }

        // Act
        UsageDetails? actual = UsageAggregationExtensions.MergeUsage(current, incoming);

        // Assert
        Assert.NotNull(actual);
        foreach (var property in GetTokenCountProperties())
        {
            Assert.Equal((long?)property.GetValue(expected), (long?)property.GetValue(actual));
        }

        Assert.Equal(
            expected.AdditionalCounts?.OrderBy(static e => e.Key).Select(static e => $"{e.Key}:{e.Value}") ?? [],
            actual.AdditionalCounts?.OrderBy(static e => e.Key).Select(static e => $"{e.Key}:{e.Value}") ?? []);
    }

    /// <summary>
    /// Guards against a future <see cref="UsageDetails"/> counter being added upstream without being summed here.
    /// Because every fix site replaces the inner response's usage with a merged instance, an unmerged counter
    /// would be silently dropped even on single-iteration runs.
    /// </summary>
    [Fact]
    public void MergeUsage_SumsEveryTokenCountPropertyExposedByUsageDetails()
    {
        // Arrange
        UsageDetails current = CreateFullyPopulatedUsage(1);
        UsageDetails incoming = CreateFullyPopulatedUsage(100);

        // Act
        UsageDetails? merged = UsageAggregationExtensions.MergeUsage(current, incoming);

        // Assert
        var properties = GetTokenCountProperties().ToList();
        Assert.NotEmpty(properties);
        Assert.NotNull(merged);
        foreach (var property in properties)
        {
            long? currentValue = (long?)property.GetValue(current);
            long? incomingValue = (long?)property.GetValue(incoming);
            Assert.Equal(currentValue + incomingValue, (long?)property.GetValue(merged));
        }
    }

    private static IEnumerable<PropertyInfo> GetTokenCountProperties()
        => typeof(UsageDetails)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.PropertyType == typeof(long?) && p.CanRead && p.CanWrite);

    /// <summary>
    /// Assigns a distinct value to every settable <see cref="long"/> counter so that a counter which is not
    /// summed by <see cref="UsageAggregationExtensions.MergeUsage"/> produces a detectable mismatch.
    /// </summary>
    private static UsageDetails CreateFullyPopulatedUsage(long seed)
    {
        UsageDetails usage = new();
        long offset = 0;
        foreach (var property in GetTokenCountProperties())
        {
            property.SetValue(usage, seed + offset++);
        }

        usage.AdditionalCounts = new() { ["provider_specific"] = seed };
        return usage;
    }

    /// <summary>
    /// Every settable property other than <see cref="ChatResponse.Usage"/> must survive the copy, otherwise
    /// replacing the inner client's response with an aggregated one would silently discard response metadata.
    /// </summary>
    [Fact]
    public void WithAggregatedUsage_ChatResponse_CopiesEverySettablePropertyExceptUsage()
    {
        // Arrange
        ChatMessage message = new(ChatRole.Assistant, "hello");
        ChatResponse original = new([message])
        {
            ResponseId = "resp-1",
            ConversationId = "conv-1",
            ModelId = "model-1",
            CreatedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            FinishReason = ChatFinishReason.Stop,
            Usage = CreateUsage(1, 1, 2),
            ContinuationToken = new TestContinuationToken(),
            RawRepresentation = new object(),
            AdditionalProperties = new() { ["key"] = "value" },
        };

        UsageDetails aggregated = CreateUsage(10, 20, 30);

        // Act
        ChatResponse copy = original.WithAggregatedUsage(aggregated);

        // Assert
        Assert.NotSame(original, copy);
        Assert.Same(aggregated, copy.Usage);
        AssertAllSettablePropertiesCopied(original, copy);
        Assert.Equal([message], copy.Messages);
    }

    /// <summary>
    /// Every settable property other than <see cref="AgentResponse.Usage"/> must survive the copy, otherwise
    /// replacing the inner agent's response with an aggregated one would silently discard response metadata.
    /// </summary>
    [Fact]
    public void WithAggregatedUsage_AgentResponse_CopiesEverySettablePropertyExceptUsage()
    {
        // Arrange
        ChatMessage message = new(ChatRole.Assistant, "hello");
        AgentResponse original = new([message])
        {
            AgentId = "agent-1",
            ResponseId = "resp-1",
            CreatedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            FinishReason = ChatFinishReason.Stop,
            Usage = CreateUsage(1, 1, 2),
            ContinuationToken = new TestContinuationToken(),
            RawRepresentation = new object(),
            AdditionalProperties = new() { ["key"] = "value" },
        };

        UsageDetails aggregated = CreateUsage(10, 20, 30);

        // Act
        AgentResponse copy = original.WithAggregatedUsage(aggregated);

        // Assert
        Assert.NotSame(original, copy);
        Assert.Same(aggregated, copy.Usage);
        AssertAllSettablePropertiesCopied(original, copy);
        Assert.Equal([message], copy.Messages);
    }

    /// <summary>
    /// When a run returns a transcript spanning multiple invocations, the supplied messages replace those of
    /// the final response while the remaining metadata is still carried over.
    /// </summary>
    [Fact]
    public void WithAggregatedUsage_AgentResponse_SubstitutesSuppliedMessagesAndRetainsMetadata()
    {
        // Arrange
        AgentResponse original = new([new ChatMessage(ChatRole.Assistant, "last")])
        {
            AgentId = "agent-1",
            RawRepresentation = new object(),
        };

        List<ChatMessage> transcript =
        [
            new(ChatRole.Assistant, "first"),
            new(ChatRole.Assistant, "last"),
        ];

        // Act
        AgentResponse copy = original.WithAggregatedUsage(null, transcript);

        // Assert
        Assert.Equal(transcript, copy.Messages);
        Assert.Equal("agent-1", copy.AgentId);
        Assert.Same(original.RawRepresentation, copy.RawRepresentation);
        Assert.Null(copy.Usage);
    }

    /// <summary>
    /// The copy must never alias the inner response, since replacing usage on a shared instance is exactly the
    /// mutation hazard these helpers exist to avoid. A substitution request therefore always copies.
    /// </summary>
    [Fact]
    public void WithAggregatedUsage_ReturnsOriginalOnlyWhenUsageAlreadyMatchesAndNoMessagesSupplied()
    {
        // Arrange
        UsageDetails usage = CreateUsage(1, 2, 3);
        ChatResponse chatResponse = new([new ChatMessage(ChatRole.Assistant, "hi")]) { Usage = usage };
        AgentResponse agentResponse = new([new ChatMessage(ChatRole.Assistant, "hi")]) { Usage = usage };

        // Act & Assert
        Assert.Same(chatResponse, chatResponse.WithAggregatedUsage(usage));
        Assert.Same(agentResponse, agentResponse.WithAggregatedUsage(usage));
        Assert.NotSame(chatResponse, chatResponse.WithAggregatedUsage(CreateUsage(1, 2, 3)));
        Assert.NotSame(agentResponse, agentResponse.WithAggregatedUsage(usage, [new ChatMessage(ChatRole.Assistant, "other")]));
    }

    private static void AssertAllSettablePropertiesCopied<T>(T original, T copy)
    {
        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.CanWrite && p.Name != nameof(AgentResponse.Usage) && p.Name != nameof(AgentResponse.Messages))
            .ToList();

        Assert.NotEmpty(properties);
        foreach (var property in properties)
        {
            object? expected = property.GetValue(original);
            Assert.NotNull(expected);
            Assert.Equal(expected, property.GetValue(copy));
        }
    }

    private sealed class TestContinuationToken : ResponseContinuationToken
    {
        public override ReadOnlyMemory<byte> ToBytes() => new([1, 2, 3]);
    }

    private static UsageDetails CreateUsage(long? input, long? output, long? total, AdditionalPropertiesDictionary<long>? additionalCounts = null)
    {
        UsageDetails usage = new()
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            TotalTokenCount = total,
        };

        if (additionalCounts is not null)
        {
            usage.AdditionalCounts = additionalCounts;
        }

        return usage;
    }

    private sealed record UsageSnapshot(long? Input, long? Output, long? Total, string AdditionalCounts)
    {
        public static UsageSnapshot Capture(UsageDetails usage)
            => new(
                usage.InputTokenCount,
                usage.OutputTokenCount,
                usage.TotalTokenCount,
                string.Join(
                    "|",
                    usage.AdditionalCounts?.OrderBy(static entry => entry.Key).Select(static entry => $"{entry.Key}:{entry.Value}") ?? []));
    }
}
