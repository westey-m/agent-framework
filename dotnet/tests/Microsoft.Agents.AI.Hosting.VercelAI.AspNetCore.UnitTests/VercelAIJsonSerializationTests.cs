// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Serialization;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.UnitTests;

public class VercelAIJsonSerializationTests
{
    [Fact]
    public void StartChunk_HasTypeDiscriminator()
    {
        UIMessageChunk chunk = new StartChunk { MessageId = "msg-1" };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("start");
    }

    [Fact]
    public void TextDeltaChunk_HasTypeAndDelta()
    {
        UIMessageChunk chunk = new TextDeltaChunk { Id = "part-1", Delta = "Hello" };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("text-delta");
        doc.RootElement.GetProperty("id").GetString().Should().Be("part-1");
        doc.RootElement.GetProperty("delta").GetString().Should().Be("Hello");
    }

    [Fact]
    public void ToolInputAvailableChunk_HasInput()
    {
        UIMessageChunk chunk = new ToolInputAvailableChunk
        {
            ToolCallId = "tc-1",
            ToolName = "get_weather",
            Input = new Dictionary<string, object> { ["city"] = "Seattle" },
        };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("tool-input-available");
        doc.RootElement.GetProperty("toolCallId").GetString().Should().Be("tc-1");
        doc.RootElement.GetProperty("toolName").GetString().Should().Be("get_weather");
        doc.RootElement.TryGetProperty("input", out _).Should().BeTrue();
    }

    [Fact]
    public void FinishChunk_HasFinishReason()
    {
        UIMessageChunk chunk = new FinishChunk { FinishReason = "stop" };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("finish");
        doc.RootElement.GetProperty("finishReason").GetString().Should().Be("stop");
    }

    [Fact]
    public void ErrorChunk_HasErrorText()
    {
        UIMessageChunk chunk = new ErrorChunk { ErrorText = "Something went wrong" };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorText").GetString().Should().Be("Something went wrong");
    }

    [Fact]
    public void NullProperties_OmittedFromJson()
    {
        UIMessageChunk chunk = new StartChunk { MessageId = null };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("messageId", out _).Should().BeFalse();
    }

    [Fact]
    public void CamelCaseNaming_Applied()
    {
        UIMessageChunk toolChunk = new ToolInputAvailableChunk
        {
            ToolCallId = "tc-1",
            ToolName = "search",
        };
        UIMessageChunk startChunk = new StartChunk { MessageId = "m-1" };
        UIMessageChunk errorChunk = new ErrorChunk { ErrorText = "err" };
        UIMessageChunk finishChunk = new FinishChunk { FinishReason = "stop" };

        var toolJson = JsonSerializer.Serialize(toolChunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);
        var startJson = JsonSerializer.Serialize(startChunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);
        var errorJson = JsonSerializer.Serialize(errorChunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);
        var finishJson = JsonSerializer.Serialize(finishChunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var toolDoc = JsonDocument.Parse(toolJson);
        toolDoc.RootElement.TryGetProperty("toolCallId", out _).Should().BeTrue();
        toolDoc.RootElement.TryGetProperty("toolName", out _).Should().BeTrue();

        using var startDoc = JsonDocument.Parse(startJson);
        startDoc.RootElement.TryGetProperty("messageId", out _).Should().BeTrue();

        using var errorDoc = JsonDocument.Parse(errorJson);
        errorDoc.RootElement.TryGetProperty("errorText", out _).Should().BeTrue();

        using var finishDoc = JsonDocument.Parse(finishJson);
        finishDoc.RootElement.TryGetProperty("finishReason", out _).Should().BeTrue();
    }

    [Fact]
    public void FileChunk_SerializesCorrectly()
    {
        UIMessageChunk chunk = new FileChunk
        {
            Url = "https://example.com/image.png",
            MediaType = "image/png",
        };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("file");
        doc.RootElement.GetProperty("url").GetString().Should().Be("https://example.com/image.png");
        doc.RootElement.GetProperty("mediaType").GetString().Should().Be("image/png");
    }

    [Fact]
    public void ReasoningDeltaChunk_SerializesCorrectly()
    {
        UIMessageChunk chunk = new ReasoningDeltaChunk { Id = "r-1", Delta = "thinking..." };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("reasoning-delta");
        doc.RootElement.GetProperty("id").GetString().Should().Be("r-1");
        doc.RootElement.GetProperty("delta").GetString().Should().Be("thinking...");
    }

    [Fact]
    public void SourceChunk_SerializesCorrectly()
    {
        UIMessageChunk chunk = new SourceUrlChunk
        {
            SourceId = "src-1",
            Url = "https://example.com/doc",
            Title = "Example Document",
        };

        var json = JsonSerializer.Serialize(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("source-url");
        doc.RootElement.GetProperty("sourceId").GetString().Should().Be("src-1");
        doc.RootElement.GetProperty("url").GetString().Should().Be("https://example.com/doc");
        doc.RootElement.GetProperty("title").GetString().Should().Be("Example Document");
    }

    [Fact]
    public void ChatRequest_Deserialization()
    {
        const string jsonString = """
            {
                "id": "chat-1",
                "messages": [
                    {
                        "id": "msg-1",
                        "role": "user",
                        "parts": [
                            { "type": "text", "text": "Hello" }
                        ]
                    }
                ]
            }
            """;

        var request = JsonSerializer.Deserialize(jsonString, VercelAIJsonSerializerContext.Default.VercelAIChatRequest);

        request.Should().NotBeNull();
        request!.Id.Should().Be("chat-1");
        request.Messages.Should().NotBeNull();
        request.Messages.Should().HaveCount(1);

        var firstMessage = request.Messages![0];
        firstMessage.Role.Should().Be("user");
        firstMessage.Parts.Should().NotBeNull();
        firstMessage.Parts.Should().HaveCount(1);
        firstMessage.Parts![0].Type.Should().Be("text");
        firstMessage.Parts[0].Text.Should().Be("Hello");
    }
}
