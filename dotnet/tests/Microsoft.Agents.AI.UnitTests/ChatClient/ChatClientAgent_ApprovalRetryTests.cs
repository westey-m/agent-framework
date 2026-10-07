// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Contains unit tests covering the recovery path for the failure described in
/// https://github.com/microsoft/agent-framework/issues/8575: when the run that carries the
/// <see cref="ToolApprovalResponseContent"/> back to the agent fails, the chat history keeps the
/// <see cref="ToolApprovalRequestContent"/> without a response, so the same approval response has to remain
/// deliverable. The record the framework keeps of a surfaced request is therefore retired only once the run that
/// consumed it has succeeded.
/// </summary>
public class ChatClientAgent_ApprovalRetryTests
{
    /// <summary>
    /// Verifies that the approval response can be sent again after the run carrying it failed, and that the record
    /// is retired once that run has succeeded, so an approval is still honored only once.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenApprovalResumeFails_SameApprovalResponseCanBeSentAgainAsync()
    {
        // Arrange
        var callCount = 0;
        var chatClient = CreateChatClient((_, _, _) =>
        {
            callCount++;
            return callCount switch
            {
                1 => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call1", "GetWeather")])])),
                2 => throw new HttpRequestException("Simulated transient failure while resuming the approval."),
                _ => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "The weather is sunny.")])),
            };
        });

        var agent = CreateAgent(chatClient);
        var session = await agent.CreateSessionAsync();

        // Act — Turn 1 surfaces the approval request and records it.
        var firstResponse = await agent.RunAsync("What's the weather?", session);
        var approvalRequests = GetApprovalRequests(firstResponse);

        // Act — Turn 2 carries the approval response, and fails at the transport.
        await Assert.ThrowsAsync<HttpRequestException>(() => agent.RunAsync(CreateApprovalResponses(approvalRequests), session));

        var recordedAfterFailedTurn = HasPendingApprovalRequests(session);

        // Act — Turn 3 sends the very same approval response again.
        var retriedResponse = await agent.RunAsync(CreateApprovalResponses(approvalRequests), session);

        // Assert
        Assert.Single(approvalRequests);
        Assert.True(recordedAfterFailedTurn, "A failed run must not discard the record of the surfaced approval request.");
        Assert.Equal("The weather is sunny.", retriedResponse.Text);
        Assert.False(HasPendingApprovalRequests(session), "The record is retired once the run that consumed it has succeeded.");
    }

    /// <summary>
    /// Verifies that the approval response also remains deliverable when the host reloads the session from a store
    /// after the failed run, which is the shape the issue was reported in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A store writes the state of a session when a run completes, so the state restored here is the one written at
    /// the end of the successful first turn: the failed resume persisted neither the chat history nor the session
    /// state. The approval response is built from the request as the store returns it, which is what a host that
    /// survives a process restart works from.
    /// </para>
    /// <para>
    /// The tool runs a second time, because the run that already invoked it persisted nothing: neither the chat
    /// history nor any other record of that invocation survived, so replaying the approval is a retry of a call the
    /// conversation has no result for. Tools whose effects must not be duplicated need to be idempotent.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenApprovalResumeFails_RestoredSessionCanSendApprovalResponseAgainAsync()
    {
        // Arrange
        var toolInvocations = 0;
        var callCount = 0;
        var chatClient = CreateChatClient((_, _, _) =>
        {
            callCount++;
            return callCount switch
            {
                1 => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call1", "GetWeather")])])),
                2 => throw new HttpRequestException("Simulated transient failure while resuming the approval."),
                _ => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "The weather is sunny.")])),
            };
        });

        var agent = CreateAgent(chatClient, () => { toolInvocations++; return "Sunny, 22\u00B0C"; });
        var session = await agent.CreateSessionAsync();

        var firstResponse = await agent.RunAsync("What's the weather?", session);
        var persistedState = await agent.SerializeSessionAsync(session);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => agent.RunAsync(CreateApprovalResponses(GetApprovalRequests(firstResponse)), session));

        var toolInvocationsAfterFailedTurn = toolInvocations;

        // Act — the host reloads the session from the store and answers the request it holds.
        var restoredSession = await agent.DeserializeSessionAsync(persistedState);
        var restoredRequests = ChatClientAgentTestHelper.GetPersistedHistory(agent, restoredSession)
            .SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();

        var retriedResponse = await agent.RunAsync(CreateApprovalResponses(restoredRequests), restoredSession);

        // Assert
        Assert.Equal("The weather is sunny.", retriedResponse.Text);
        Assert.Equal(1, toolInvocationsAfterFailedTurn);
        Assert.Equal(2, toolInvocations);

        // The approval request and the result of the call it authorized are both in the history, so nothing is left
        // unanswered.
        var history = ChatClientAgentTestHelper.GetPersistedHistory(agent, restoredSession);
        Assert.Contains(history.SelectMany(m => m.Contents), c => c is ToolApprovalRequestContent);
        Assert.Contains(history.SelectMany(m => m.Contents), c => c is FunctionResultContent);
    }

    /// <summary>
    /// Verifies that the streaming path keeps the record of a surfaced approval request when the run fails, so the
    /// approval response remains deliverable there too.
    /// </summary>
    [Fact]
    public async Task RunStreamingAsync_WhenApprovalResumeFails_SameApprovalResponseCanBeSentAgainAsync()
    {
        // Arrange
        var callCount = 0;
        var chatClient = CreateStreamingChatClient(() =>
        {
            callCount++;
            return callCount switch
            {
                1 => [new ChatResponseUpdate(ChatRole.Assistant, new AIContent[] { new FunctionCallContent("call1", "GetWeather") })],
                2 => throw new HttpRequestException("Simulated transient failure while resuming the approval."),
                _ => [new ChatResponseUpdate(ChatRole.Assistant, "The weather is sunny.")],
            };
        });

        var agent = CreateAgent(chatClient);
        var session = await agent.CreateSessionAsync();

        var firstResponse = await agent.RunStreamingAsync("What's the weather?", session).ToAgentResponseAsync();
        var approvalRequests = GetApprovalRequests(firstResponse);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await agent.RunStreamingAsync(CreateApprovalResponses(approvalRequests), session).ToAgentResponseAsync());

        var recordedAfterFailedTurn = HasPendingApprovalRequests(session);

        // Act
        var retriedResponse = await agent.RunStreamingAsync(CreateApprovalResponses(approvalRequests), session).ToAgentResponseAsync();

        // Assert
        Assert.True(recordedAfterFailedTurn, "A failed run must not discard the record of the surfaced approval request.");
        Assert.Equal("The weather is sunny.", retriedResponse.Text);
        Assert.False(HasPendingApprovalRequests(session), "The record is retired once the run that consumed it has succeeded.");
    }

    /// <summary>
    /// Verifies that a rejection can also be sent again after a failed run, and that the rejected call is settled
    /// without the tool being executed.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenRejectionResumeFails_RejectionCanBeSentAgainAsync()
    {
        // Arrange
        var toolInvocations = 0;
        var callCount = 0;
        var chatClient = CreateChatClient((_, _, _) =>
        {
            callCount++;
            return callCount switch
            {
                1 => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call1", "GetWeather")])])),
                2 => throw new HttpRequestException("Simulated transient failure while resuming the approval."),
                _ => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Understood, I will not check the weather.")])),
            };
        });

        var agent = CreateAgent(chatClient, () => { toolInvocations++; return "Sunny, 22°C"; });
        var session = await agent.CreateSessionAsync();

        var firstResponse = await agent.RunAsync("What's the weather?", session);
        var approvalRequests = GetApprovalRequests(firstResponse);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => agent.RunAsync(CreateApprovalResponses(approvalRequests, approved: false), session));

        // Act
        var retriedResponse = await agent.RunAsync(CreateApprovalResponses(approvalRequests, approved: false), session);

        // Assert
        Assert.Equal("Understood, I will not check the weather.", retriedResponse.Text);
        Assert.Equal(0, toolInvocations);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task RunAsync_WhenApprovalIsAbandoned_RestoredSessionCanContinueAsync(
        bool streaming, bool perServiceCallPersistence, bool failFirstAttempt)
    {
        // Arrange
        int toolInvocations = 0;
        List<List<ChatMessage>> serviceInputs = [];
        var client = CreateChatClient((messages, _, _) =>
        {
            serviceInputs.Add(messages.ToList());
            if (failFirstAttempt && serviceInputs.Count == 2)
            {
                throw new HttpRequestException("Simulated failure while rejecting an unanswered approval.");
            }

            return Task.FromResult(serviceInputs.Count == 1
                ? new ChatResponse([new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call1", "GetWeather")])])
                : new ChatResponse([new ChatMessage(ChatRole.Assistant, "Here is a joke.")]));
        });

        var agent = CreateAgent(client, () => { toolInvocations++; return "Sunny"; }, perServiceCallPersistence);
        var session = await agent.CreateSessionAsync();

        Task<AgentResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentSession currentSession) => streaming
            ? agent.RunStreamingAsync(messages, currentSession).ToAgentResponseAsync()
            : agent.RunAsync(messages, currentSession);

        var firstResponse = await RunAsync([new ChatMessage(ChatRole.User, "What's the weather?")], session);
        var request = Assert.Single(GetApprovalRequests(firstResponse));
        session = await agent.DeserializeSessionAsync(await agent.SerializeSessionAsync(session));

        // Act
        if (failFirstAttempt)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync([new ChatMessage(ChatRole.User, "Tell me a joke instead.")], session));
            Assert.True(HasPendingApprovalRequests(session));
            Assert.True(session.StateBag.TryGetValue<List<ToolApprovalRequestContent>>(
                ApprovalResponseBindingChatClient.StateBagKey, out var pending, AgentJsonUtilities.DefaultOptions));
            Assert.False(Assert.IsType<FunctionCallContent>(Assert.Single(pending!).ToolCall).InformationalOnly);
        }

        var response = await RunAsync([new ChatMessage(ChatRole.User, "Tell me a joke instead.")], session);
        await RunAsync([new ChatMessage(ChatRole.User, "Another joke, please.")], session);
        await RunAsync([new ChatMessage(ChatRole.User, [request.CreateResponse(approved: true)])], session);

        // Assert
        Assert.Equal("Here is a joke.", response.Text);
        Assert.Equal(0, toolInvocations);
        Assert.False(HasPendingApprovalRequests(session));
        Assert.Contains(serviceInputs[1], m => m.Text == "Tell me a joke instead.");
        var rejection = Assert.Single(serviceInputs[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call1", rejection.CallId);
        Assert.Contains("rejected", rejection.Result?.ToString());
        var history = ChatClientAgentTestHelper.GetPersistedHistory(agent, session);
        Assert.Single(history.SelectMany(m => m.Contents).OfType<FunctionResultContent>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RunAsync_PartiallyAnsweredBatch_RejectsMissingAnswerAndPreservesAutomaticApprovalAsync(
        bool streaming, bool perServiceCallPersistence)
    {
        // Arrange
        List<string> invokedTools = [];
        int serviceCalls = 0;
        List<ChatMessage> resumedMessages = [];
        var client = CreateChatClient((messages, _, _) =>
        {
            if (++serviceCalls == 1)
            {
                return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("call1", "Approved"),
                    new FunctionCallContent("call2", "Unanswered"),
                    new FunctionCallContent("call3", "Automatic"),
                    new FunctionCallContent("call4", "Rejected")
                ])]));
            }

            resumedMessages = messages.ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done.")]));
        });
        AIFunction CreateTool(string name) => AIFunctionFactory.Create(() => { invokedTools.Add(name); return name; }, name);
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Tools =
                [
                    new ApprovalRequiredAIFunction(CreateTool("Approved")),
                    new ApprovalRequiredAIFunction(CreateTool("Unanswered")),
                    CreateTool("Automatic"),
                    new ApprovalRequiredAIFunction(CreateTool("Rejected"))
                ]
            },
            RequirePerServiceCallChatHistoryPersistence = perServiceCallPersistence
        });
        var session = await agent.CreateSessionAsync();
        Task<AgentResponse> RunAsync(IEnumerable<ChatMessage> messages) => streaming
            ? agent.RunStreamingAsync(messages, session).ToAgentResponseAsync()
            : agent.RunAsync(messages, session);
        var first = await RunAsync([new ChatMessage(ChatRole.User, "Use the tools.")]);
        var requests = GetApprovalRequests(first);
        Assert.Equal(3, requests.Count);
        session = await agent.DeserializeSessionAsync(await agent.SerializeSessionAsync(session));
        var approved = requests.Single(r => r.ToolCall.CallId == "call1");
        var rejected = requests.Single(r => r.ToolCall.CallId == "call4");

        // Act
        await RunAsync([new ChatMessage(ChatRole.User, [approved.CreateResponse(true), rejected.CreateResponse(false)])]);

        // Assert
        Assert.Equal(2, invokedTools.Count);
        Assert.Contains("Approved", invokedTools);
        Assert.Contains("Automatic", invokedTools);
        var results = resumedMessages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        Assert.Equal(4, results.Count);
        Assert.Contains("rejected", results.Single(r => r.CallId == "call2").Result?.ToString());
        Assert.Contains("rejected", results.Single(r => r.CallId == "call4").Result?.ToString());
        Assert.False(HasPendingApprovalRequests(session));
    }

    private static bool HasPendingApprovalRequests(AgentSession session)
        => session.StateBag.TryGetValue<List<ToolApprovalRequestContent>>(
            ApprovalResponseBindingChatClient.StateBagKey,
            out var pending,
            AgentJsonUtilities.DefaultOptions)
            && pending is { Count: > 0 };

    private static ChatClientAgent CreateAgent(IChatClient chatClient, Func<string>? getWeather = null, bool perServiceCallPersistence = false)
        => new(
            chatClient,
            options: new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(getWeather ?? (() => "Sunny, 22°C"), "GetWeather"))]
                },
                ChatHistoryProvider = new InMemoryChatHistoryProvider(),
                RequirePerServiceCallChatHistoryPersistence = perServiceCallPersistence
            },
            services: new ServiceCollection().BuildServiceProvider());

    private static List<ChatMessage> CreateApprovalResponses(List<ToolApprovalRequestContent> approvalRequests, bool approved = true)
        => approvalRequests.ConvertAll(request =>
            new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]));

    private static List<ToolApprovalRequestContent> GetApprovalRequests(AgentResponse response)
        => response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();

    private static IChatClient CreateChatClient(
        Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> onGetResponse)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(onGetResponse);
        mock.Setup(c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
                RespondStreamingAsync(messages, options, ct));
        return mock.Object;

        async IAsyncEnumerable<ChatResponseUpdate> RespondStreamingAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken ct)
        {
            var response = await onGetResponse(messages, options, ct);
            foreach (var update in response.ToChatResponseUpdates())
            {
                ct.ThrowIfCancellationRequested();
                yield return update;
            }
        }
    }

    private static IChatClient CreateStreamingChatClient(Func<List<ChatResponseUpdate>> onGetStreamingResponse)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken cancellationToken)
                => ToAsyncEnumerableAsync(onGetStreamingResponse(), cancellationToken));
        return mock.Object;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerableAsync(
        List<ChatResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return update;
        }
    }
}
