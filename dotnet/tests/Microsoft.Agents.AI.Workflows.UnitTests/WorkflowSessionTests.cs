// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Workflows.UnitTests;

/// <summary>
/// Verifies that <see cref="WorkflowSession"/> recognizes an external request as an envelope
/// only when the request's port declares a type that matches the type recorded on the request,
/// resolving that type from the live workflow ports rather than the recorded assembly name.
/// </summary>
public class WorkflowSessionTests
{
    private sealed class ApprovalStartExecutor() : ChatProtocolExecutor("start", new() { AutoSendTurnToken = false })
    {
        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
            base.ConfigureProtocol(protocolBuilder).SendsMessage<ToolApprovalRequestContent>();

        protected override async ValueTask TakeTurnAsync(
            List<ChatMessage> messages,
            IWorkflowContext context,
            bool? emitEvents,
            CancellationToken cancellationToken = default)
        {
            if (messages.Count > 0)
            {
                ToolApprovalRequestContent request = new("approval", new FunctionCallContent("call", "test_tool"));
                await context.SendMessageAsync(request, cancellationToken: cancellationToken);
            }
        }
    }

    private sealed class ApprovalResponseExecutor<T>(string id, Action onInvocation) : Executor<T, string>(id)
    {
        public override ValueTask<string> HandleAsync(
            T message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            onInvocation();
            return new("completed");
        }
    }

    private sealed class DownstreamFailureExecutor() : Executor<string>("failure")
    {
        public override ValueTask HandleAsync(
            string message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("downstream failure");
    }

    private sealed class TestEnvelope : IExternalRequestEnvelope
    {
        AIContent? IExternalRequestEnvelope.GetInnerRequestContent() => null;

        object IExternalRequestEnvelope.CreateResponse(IList<ChatMessage> messages) => messages;
    }

    [Fact]
    public void ResolveEnvelopeType_ReturnsPortTypeWhenPortDeclaresMatchingType()
    {
        Type live = typeof(TestEnvelope);
        RequestPort port = new("port-1", live, typeof(object));
        Dictionary<string, RequestPort> ports = new() { [port.Id] = port };
        RequestPortInfo portInfo = new(new TypeId(live), new TypeId(typeof(object)), port.Id);

        Assert.Equal(live, WorkflowSession.ResolveEnvelopeType(portInfo, ports));
    }

    [Fact]
    public void ResolveEnvelopeType_ResolvesAcrossAssemblyVersionMutation()
    {
        Type live = typeof(TestEnvelope);
        RequestPort port = new("port-1", live, typeof(object));
        Dictionary<string, RequestPort> ports = new() { [port.Id] = port };

        string simpleAssemblyName = live.Assembly.GetName().Name!;
        string mutatedAssemblyName = $"{simpleAssemblyName}, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null";
        RequestPortInfo portInfo = new(new TypeId(mutatedAssemblyName, live.FullName!), new TypeId(typeof(object)), port.Id);

        Assert.Equal(live, WorkflowSession.ResolveEnvelopeType(portInfo, ports));
    }

    [Fact]
    public void ResolveEnvelopeType_ResolvesAcrossGenericArgumentVersionMutation()
    {
        Type live = typeof(List<ChatMessage>);
        RequestPort port = new("port-1", live, typeof(object));
        Dictionary<string, RequestPort> ports = new() { [port.Id] = port };

        string outerSimpleName = live.Assembly.GetName().Name!;
        string innerSimpleName = typeof(ChatMessage).Assembly.GetName().Name!;
        string mutatedTypeName = $"System.Collections.Generic.List`1[[Microsoft.Extensions.AI.ChatMessage, {innerSimpleName}, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null]]";
        RequestPortInfo portInfo = new(new TypeId(outerSimpleName, mutatedTypeName), new TypeId(typeof(object)), port.Id);

        Assert.Equal(live, WorkflowSession.ResolveEnvelopeType(portInfo, ports));
    }

    [Fact]
    public void ResolveEnvelopeType_ReturnsNullWhenPortIdIsUnknown()
    {
        Dictionary<string, RequestPort> ports = new()
        {
            ["port-1"] = new RequestPort("port-1", typeof(TestEnvelope), typeof(object)),
        };
        RequestPortInfo portInfo = new(new TypeId(typeof(TestEnvelope)), new TypeId(typeof(object)), "missing-port");

        Assert.Null(WorkflowSession.ResolveEnvelopeType(portInfo, ports));
    }

    [Fact]
    public void ResolveEnvelopeType_ReturnsNullWhenRecordedTypeDoesNotMatchPortType()
    {
        Dictionary<string, RequestPort> ports = new()
        {
            ["port-1"] = new RequestPort("port-1", typeof(TestEnvelope), typeof(object)),
        };
        RequestPortInfo portInfo = new(new TypeId("Some.Unloaded.Assembly", "Some.Unknown.Type"), new TypeId(typeof(object)), "port-1");

        Assert.Null(WorkflowSession.ResolveEnvelopeType(portInfo, ports));
    }

    [Fact]
    public void TryGetRequestEnvelope_ReturnsEnvelopeWhenPortDeclaresEnvelopeType()
    {
        RequestPort port = new("port-1", typeof(TestEnvelope), typeof(object));
        Dictionary<string, RequestPort> ports = new() { [port.Id] = port };
        ExternalRequest request = ExternalRequest.Create(port, new TestEnvelope());

        Assert.True(WorkflowSession.TryGetRequestEnvelope(request, ports, out IExternalRequestEnvelope? envelope));
        Assert.True(envelope is TestEnvelope);
    }

    [Fact]
    public void TryGetRequestEnvelope_ReturnsFalseWhenPortTypeIsNotEnvelope()
    {
        RequestPort port = new("port-1", typeof(string), typeof(object));
        Dictionary<string, RequestPort> ports = new() { [port.Id] = port };
        ExternalRequest request = ExternalRequest.Create(port, "not-an-envelope");

        Assert.False(WorkflowSession.TryGetRequestEnvelope(request, ports, out IExternalRequestEnvelope? envelope));
        Assert.Null(envelope);
    }

    [Fact]
    public void TryGetRequestEnvelope_ReturnsFalseWhenRecordedTypeDoesNotMatchPortType()
    {
        RequestPort port = new("port-1", typeof(TestEnvelope), typeof(object));
        Dictionary<string, RequestPort> ports = new() { [port.Id] = port };
        RequestPortInfo recordedPortInfo = new(new TypeId("Some.Unloaded.Assembly", "Some.Unknown.Type"), new TypeId(typeof(object)), port.Id);
        ExternalRequest request = new(recordedPortInfo, "req-1", new PortableValue(new TestEnvelope()));

        Assert.False(WorkflowSession.TryGetRequestEnvelope(request, ports, out IExternalRequestEnvelope? envelope));
        Assert.Null(envelope);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task CompletedResponseHandlerIsNotRepeatedAfterDownstreamFailureAsync(bool wrappedResponse, int consumerCount)
    {
        // Arrange
        ApprovalStartExecutor start = new();
        RequestPort port = RequestPort.Create<ToolApprovalRequestContent, ToolApprovalResponseContent>("approval");
        int responseInvocations = 0;
        void onInvocation() => Interlocked.Increment(ref responseInvocations);
        DownstreamFailureExecutor failure = new();
        WorkflowBuilder builder = new WorkflowBuilder(start).AddEdge(start, port);
        for (int i = 0; i < consumerCount; i++)
        {
            Executor responseHandler = wrappedResponse
                ? new ApprovalResponseExecutor<ExternalResponse>($"response-{i}", onInvocation)
                : new ApprovalResponseExecutor<ToolApprovalResponseContent>($"response-{i}", onInvocation);
            builder.AddEdge(port, responseHandler).AddEdge(responseHandler, failure);
        }

        Workflow workflow = builder.Build();
        AIAgent agent = workflow.AsAIAgent(includeExceptionDetails: true);
        AgentSession session = await agent.CreateSessionAsync();
        List<AgentResponseUpdate> initialUpdates = await agent.RunStreamingAsync("start", session).ToListAsync();
        ToolApprovalRequestContent approval = Assert.Single(
            initialUpdates.SelectMany(update => update.Contents).OfType<ToolApprovalRequestContent>());
        ChatMessage response = new(ChatRole.User, [approval.CreateResponse(approved: true)]);

        // Act
        List<AgentResponseUpdate> failedUpdates = await agent.RunStreamingAsync(response, session).ToListAsync();
        JsonElement serializedSession = await agent.SerializeSessionAsync(session);
        AgentSession recoveredSession = await agent.DeserializeSessionAsync(serializedSession);
        List<AgentResponseUpdate> resumedUpdates = await agent.RunStreamingAsync(response, recoveredSession).ToListAsync();

        // Assert
        Assert.Contains(failedUpdates.SelectMany(update => update.Contents).OfType<ErrorContent>(),
            error => error.Message.Contains("downstream failure", StringComparison.Ordinal));
        Assert.Contains(resumedUpdates.SelectMany(update => update.Contents).OfType<ErrorContent>(),
            error => error.Message.Contains("downstream failure", StringComparison.Ordinal));
        Assert.Equal(consumerCount, responseInvocations);
    }
}
