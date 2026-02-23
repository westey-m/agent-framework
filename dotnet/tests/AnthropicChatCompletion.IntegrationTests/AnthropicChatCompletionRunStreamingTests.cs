// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public abstract class SkipAllRunStreaming(Func<AnthropicChatCompletionFixture> func) : RunStreamingTests<AnthropicChatCompletionFixture>(func)
{
    public override Task RunWithChatMessageReturnsExpectedResultAsync()
    {
        Assert.SkipWhen(AnthropicChatCompletionFixture.SkipReason is not null, AnthropicChatCompletionFixture.SkipReason!);
        return base.RunWithChatMessageReturnsExpectedResultAsync();
    }

    public override Task RunWithNoMessageDoesNotFailAsync()
    {
        Assert.SkipWhen(AnthropicChatCompletionFixture.SkipReason is not null, AnthropicChatCompletionFixture.SkipReason!);
        return base.RunWithNoMessageDoesNotFailAsync();
    }

    public override Task RunWithChatMessagesReturnsExpectedResultAsync()
    {
        Assert.SkipWhen(AnthropicChatCompletionFixture.SkipReason is not null, AnthropicChatCompletionFixture.SkipReason!);
        return base.RunWithChatMessagesReturnsExpectedResultAsync();
    }

    public override Task RunWithStringReturnsExpectedResultAsync()
    {
        Assert.SkipWhen(AnthropicChatCompletionFixture.SkipReason is not null, AnthropicChatCompletionFixture.SkipReason!);
        return base.RunWithStringReturnsExpectedResultAsync();
    }

    public override Task SessionMaintainsHistoryAsync()
    {
        Assert.SkipWhen(AnthropicChatCompletionFixture.SkipReason is not null, AnthropicChatCompletionFixture.SkipReason!);
        return base.SessionMaintainsHistoryAsync();
    }
}

public class AnthropicBetaChatCompletionRunStreamingTests()
    : SkipAllRunStreaming(() => new(useReasoningChatModel: false, useBeta: true));

public class AnthropicBetaChatCompletionReasoningRunStreamingTests()
    : SkipAllRunStreaming(() => new(useReasoningChatModel: true, useBeta: true));

public class AnthropicChatCompletionRunStreamingTests()
    : SkipAllRunStreaming(() => new(useReasoningChatModel: false, useBeta: false));

public class AnthropicChatCompletionReasoningRunStreamingTests()
    : SkipAllRunStreaming(() => new(useReasoningChatModel: true, useBeta: false));
