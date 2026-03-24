// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Converters;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.UnitTests;

public class AgentResponseUpdateConverterTests
{
    private static async Task<List<UIMessageChunk>> CollectChunksAsync(IAsyncEnumerable<AgentResponseUpdate> updates)
    {
        var chunks = new List<UIMessageChunk>();
        await foreach (var chunk in updates.AsVercelAIChunkStreamAsync())
        {
            chunks.Add(chunk);
        }
        return chunks;
    }

    [Fact]
    public async Task TextOnly_EmitsCorrectChunkSequence()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextContent("Hello")], FinishReason = ChatFinishReason.Stop }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        chunks.Should().HaveCount(7);
        chunks[0].Should().BeOfType<StartChunk>();
        chunks[1].Should().BeOfType<StartStepChunk>();
        chunks[2].Should().BeOfType<TextStartChunk>();
        chunks[3].Should().BeOfType<TextDeltaChunk>().Which.Delta.Should().Be("Hello");
        chunks[4].Should().BeOfType<TextEndChunk>();
        chunks[5].Should().BeOfType<FinishStepChunk>();
        chunks[6].Should().BeOfType<FinishChunk>().Which.FinishReason.Should().Be("stop");

        var textStartId = chunks[2].Should().BeOfType<TextStartChunk>().Which.Id;
        chunks[3].Should().BeOfType<TextDeltaChunk>().Which.Id.Should().Be(textStartId);
        chunks[4].Should().BeOfType<TextEndChunk>().Which.Id.Should().Be(textStartId);
    }

    [Fact]
    public async Task MultipleTextDeltas_ShareSameId()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextContent("Hello "), new TextContent("World")], FinishReason = ChatFinishReason.Stop }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var deltas = chunks.OfType<TextDeltaChunk>().ToList();
        deltas.Should().HaveCount(2);
        deltas[0].Delta.Should().Be("Hello ");
        deltas[1].Delta.Should().Be("World");
        deltas[0].Id.Should().Be(deltas[1].Id);
    }

    [Fact]
    public async Task ReasoningContent_EmitsReasoningChunks()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextReasoningContent("thinking...")], FinishReason = ChatFinishReason.Stop }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        chunks.Should().Contain(c => c is ReasoningStartChunk);
        chunks.Should().Contain(c => c is ReasoningDeltaChunk);
        chunks.Should().Contain(c => c is ReasoningEndChunk);

        var reasoningDelta = chunks.OfType<ReasoningDeltaChunk>().Single();
        reasoningDelta.Delta.Should().Be("thinking...");

        var startId = chunks.OfType<ReasoningStartChunk>().Single().Id;
        reasoningDelta.Id.Should().Be(startId);
        chunks.OfType<ReasoningEndChunk>().Single().Id.Should().Be(startId);
    }

    [Fact]
    public async Task ReasoningThenText_ClosesReasoningBeforeText()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextReasoningContent("reason"), new TextContent("answer")], FinishReason = ChatFinishReason.Stop }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var types = chunks.ConvertAll(c => c.GetType());
        int reasoningEndIdx = types.IndexOf(typeof(ReasoningEndChunk));
        int textStartIdx = types.IndexOf(typeof(TextStartChunk));

        reasoningEndIdx.Should().BeGreaterThan(-1);
        textStartIdx.Should().BeGreaterThan(-1);
        reasoningEndIdx.Should().BeLessThan(textStartIdx, "reasoning must close before text starts");
    }

    [Fact]
    public async Task ToolCallFlow_EmitsCorrectSequence()
    {
        var args = new Dictionary<string, object?> { ["query"] = "test" };
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new FunctionCallContent("call-1", "search", args)], FinishReason = ChatFinishReason.ToolCalls }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var toolStart = chunks.OfType<ToolInputStartChunk>().Single();
        toolStart.ToolCallId.Should().Be("call-1");
        toolStart.ToolName.Should().Be("search");

        var toolAvailable = chunks.OfType<ToolInputAvailableChunk>().Single();
        toolAvailable.ToolCallId.Should().Be("call-1");
        toolAvailable.ToolName.Should().Be("search");
        toolAvailable.Input.Should().NotBeNull();
    }

    [Fact]
    public async Task ToolResultWithJsonString_ParsesAsJson()
    {
        const string jsonResult = "{\"key\":\"value\"}";
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new FunctionResultContent("call-1", jsonResult)] }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var output = chunks.OfType<ToolOutputAvailableChunk>().Single();
        output.ToolCallId.Should().Be("call-1");
        output.Output.Should().BeOfType<JsonElement>();
    }

    [Fact]
    public async Task ToolResultWithPlainString_KeepsAsString()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new FunctionResultContent("call-1", "plain text result")] }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var output = chunks.OfType<ToolOutputAvailableChunk>().Single();
        output.ToolCallId.Should().Be("call-1");
        output.Output.Should().BeOfType<string>().Which.Should().Be("plain text result");
    }

    [Fact]
    public async Task ToolResultWithException_EmitsError()
    {
        var result = new FunctionResultContent("call-1", result: null)
        {
            Exception = new InvalidOperationException("tool failed")
        };
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [result] }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var error = chunks.OfType<ToolOutputErrorChunk>().Single();
        error.ToolCallId.Should().Be("call-1");
        error.ErrorText.Should().Be("tool failed");
    }

    [Fact]
    public async Task MultiStepWithFinishReason_EmitsSeparateSteps()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextContent("step 1")], FinishReason = ChatFinishReason.Stop },
            new() { Contents = [new TextContent("step 2")], FinishReason = ChatFinishReason.Stop }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var stepStarts = chunks.OfType<StartStepChunk>().ToList();
        var stepFinishes = chunks.OfType<FinishStepChunk>().ToList();

        stepStarts.Should().HaveCount(2);
        stepFinishes.Should().HaveCount(2);
    }

    [Fact]
    public async Task FinishReasonPropagated_ToFinishChunk()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextContent("text")], FinishReason = ChatFinishReason.ContentFilter }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        chunks.Last().Should().BeOfType<FinishChunk>().Which.FinishReason.Should().Be("content_filter");
    }

    [Fact]
    public async Task EmptyTextContent_IsSkipped()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextContent(""), new TextContent("visible")], FinishReason = ChatFinishReason.Stop }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var deltas = chunks.OfType<TextDeltaChunk>().ToList();
        deltas.Should().HaveCount(1);
        deltas[0].Delta.Should().Be("visible");
    }

    [Fact]
    public async Task FileContent_EmitsFileChunk()
    {
        var dataContent = new DataContent("data:image/png;base64,iVBORw0KGgo=", "image/png");
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [dataContent], FinishReason = ChatFinishReason.Stop }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        var fileChunk = chunks.OfType<FileChunk>().Single();
        fileChunk.MediaType.Should().Be("image/png");
        fileChunk.Url.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StreamWithNoFinishReason_DefaultsToStop()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextContent("text")] }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        chunks.Last().Should().BeOfType<FinishChunk>().Which.FinishReason.Should().Be("stop");
    }

    [Fact]
    public async Task StartChunk_HasMessageId()
    {
        var updates = new List<AgentResponseUpdate>
        {
            new() { Contents = [new TextContent("hi")] }
        };

        var chunks = await CollectChunksAsync(updates.ToAsyncEnumerableAsync());

        chunks[0].Should().BeOfType<StartChunk>().Which.MessageId.Should().NotBeNullOrEmpty();
    }
}
