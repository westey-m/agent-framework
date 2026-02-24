// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public abstract class SkipAllChatClientRunStreaming(Func<AnthropicChatCompletionFixture> func) : ChatClientAgentRunStreamingTests<AnthropicChatCompletionFixture>(func)
{
    public override Task RunWithFunctionsInvokesFunctionsAndReturnsExpectedResultsAsync()
    {
        Assert.SkipWhen(AnthropicChatCompletionFixture.SkipReason is not null, AnthropicChatCompletionFixture.SkipReason!);
        return base.RunWithFunctionsInvokesFunctionsAndReturnsExpectedResultsAsync();
    }

    public override Task RunWithInstructionsAndNoMessageReturnsExpectedResultAsync()
    {
        Assert.SkipWhen(AnthropicChatCompletionFixture.SkipReason is not null, AnthropicChatCompletionFixture.SkipReason!);
        return base.RunWithInstructionsAndNoMessageReturnsExpectedResultAsync();
    }
}

public class AnthropicBetaChatCompletionChatClientAgentReasoningRunStreamingTests() : SkipAllChatClientRunStreaming(() => new(useReasoningChatModel: true, useBeta: true));

public class AnthropicBetaChatCompletionChatClientAgentRunStreamingTests() : SkipAllChatClientRunStreaming(() => new(useReasoningChatModel: false, useBeta: true));

public class AnthropicChatCompletionChatClientAgentRunStreamingTests() : SkipAllChatClientRunStreaming(() => new(useReasoningChatModel: false, useBeta: false));

public class AnthropicChatCompletionChatClientAgentReasoningRunStreamingTests() : SkipAllChatClientRunStreaming(() => new(useReasoningChatModel: true, useBeta: false));
