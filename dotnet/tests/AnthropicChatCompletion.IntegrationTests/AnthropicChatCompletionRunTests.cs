// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public abstract class SkipAllRun(Func<AnthropicChatCompletionFixture> func) : RunTests<AnthropicChatCompletionFixture>(func)
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

public class AnthropicBetaChatCompletionRunTests()
    : SkipAllRun(() => new(useReasoningChatModel: false, useBeta: true));

public class AnthropicBetaChatCompletionReasoningRunTests()
    : SkipAllRun(() => new(useReasoningChatModel: true, useBeta: true));

public class AnthropicChatCompletionRunTests()
    : SkipAllRun(() => new(useReasoningChatModel: false, useBeta: false));

public class AnthropicChatCompletionReasoningRunTests()
    : SkipAllRun(() => new(useReasoningChatModel: true, useBeta: false));
