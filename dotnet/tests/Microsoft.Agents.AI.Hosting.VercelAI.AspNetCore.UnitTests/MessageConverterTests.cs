// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Converters;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.UnitTests;

public class MessageConverterTests
{
    [Fact]
    public void UserRole_MapsCorrectly()
    {
        var message = new VercelAIMessage
        {
            Role = "user",
            Parts = new List<VercelAIMessagePart> { new() { Type = "text", Text = "hi" } },
        };

        var result = message.ToChatMessage();

        result.Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public void AssistantRole_MapsCorrectly()
    {
        var message = new VercelAIMessage
        {
            Role = "assistant",
            Parts = new List<VercelAIMessagePart> { new() { Type = "text", Text = "hi" } },
        };

        var result = message.ToChatMessage();

        result.Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public void SystemRole_MapsCorrectly()
    {
        var message = new VercelAIMessage
        {
            Role = "system",
            Parts = new List<VercelAIMessagePart> { new() { Type = "text", Text = "hi" } },
        };

        var result = message.ToChatMessage();

        result.Role.Should().Be(ChatRole.System);
    }

    [Fact]
    public void UnknownRole_CreatesCustomRole()
    {
        var message = new VercelAIMessage
        {
            Role = "custom",
            Parts = new List<VercelAIMessagePart> { new() { Type = "text", Text = "hi" } },
        };

        var result = message.ToChatMessage();

        result.Role.Should().Be(new ChatRole("custom"));
    }

    [Fact]
    public void TextPart_CreatesTextContent()
    {
        var message = new VercelAIMessage
        {
            Role = "user",
            Parts = new List<VercelAIMessagePart>
            {
                new() { Type = "text", Text = "Hello world" },
            },
        };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(1);
        result.Contents[0].Should().BeOfType<TextContent>()
            .Which.Text.Should().Be("Hello world");
    }

    [Fact]
    public void FilePart_DataUrl_CreatesDataContent()
    {
        var message = new VercelAIMessage
        {
            Role = "user",
            Parts = new List<VercelAIMessagePart>
            {
                new() { Type = "file", Url = "data:image/png;base64,iVBORw0KGgo=", MediaType = "image/png" },
            },
        };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(1);
        result.Contents[0].Should().BeOfType<DataContent>()
            .Which.MediaType.Should().Be("image/png");
    }

    [Fact]
    public void FilePart_RemoteUrl_CreatesUriContent()
    {
        var message = new VercelAIMessage
        {
            Role = "user",
            Parts = new List<VercelAIMessagePart>
            {
                new() { Type = "file", Url = "https://example.com/image.png", MediaType = "image/png" },
            },
        };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(1);
        result.Contents[0].Should().BeOfType<UriContent>()
            .Which.Uri.Should().Be(new Uri("https://example.com/image.png"));
    }

    [Fact]
    public void ToolInvocationPart_CreatesFunctionCallContent()
    {
        var input = JsonSerializer.SerializeToElement(new { param1 = "value1" });
        var message = new VercelAIMessage
        {
            Role = "assistant",
            Parts = new List<VercelAIMessagePart>
            {
                new()
                {
                    Type = "tool-invocation",
                    ToolCallId = "call-1",
                    ToolName = "myTool",
                    State = "input-available",
                    Input = input,
                },
            },
        };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(1);
        var functionCall = result.Contents[0].Should().BeOfType<FunctionCallContent>().Subject;
        functionCall.CallId.Should().Be("call-1");
        functionCall.Name.Should().Be("myTool");
        functionCall.Arguments.Should().ContainKey("param1");
    }

    [Fact]
    public void ToolInvocationPart_WithResult_CreatesFunctionResultContent()
    {
        var input = JsonSerializer.SerializeToElement(new { query = "test" });
        var output = JsonSerializer.SerializeToElement("tool output");
        var message = new VercelAIMessage
        {
            Role = "assistant",
            Parts = new List<VercelAIMessagePart>
            {
                new()
                {
                    Type = "tool-invocation",
                    ToolCallId = "call-2",
                    ToolName = "myTool",
                    State = "output-available",
                    Input = input,
                    Output = output,
                },
            },
        };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(2);
        result.Contents[0].Should().BeOfType<FunctionCallContent>()
            .Which.CallId.Should().Be("call-2");
        var functionResult = result.Contents[1].Should().BeOfType<FunctionResultContent>().Subject;
        functionResult.CallId.Should().Be("call-2");
        functionResult.Result.Should().NotBeNull();
    }

    [Fact]
    public void ToolInvocationPart_WithoutResult_NoFunctionResultContent()
    {
        var input = JsonSerializer.SerializeToElement(new { key = "val" });
        var message = new VercelAIMessage
        {
            Role = "assistant",
            Parts = new List<VercelAIMessagePart>
            {
                new()
                {
                    Type = "tool-invocation",
                    ToolCallId = "call-3",
                    ToolName = "myTool",
                    State = "input-available",
                    Input = input,
                },
            },
        };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(1);
        result.Contents[0].Should().BeOfType<FunctionCallContent>();
        result.Contents.Should().NotContain(c => c is FunctionResultContent);
    }

    [Fact]
    public void FallbackContent_WhenNoPartsPresent()
    {
        var message = new VercelAIMessage { Role = "user" };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(1);
        result.Contents[0].Should().BeOfType<TextContent>()
            .Which.Text.Should().Be(string.Empty);
    }

    [Fact]
    public void MultipleParts_AllConverted()
    {
        var message = new VercelAIMessage
        {
            Role = "user",
            Parts = new List<VercelAIMessagePart>
            {
                new() { Type = "text", Text = "text1" },
                new() { Type = "text", Text = "text2" },
                new() { Type = "file", Url = "https://example.com/img.png", MediaType = "image/png" },
            },
        };

        var result = message.ToChatMessage();

        result.Contents.Should().HaveCount(3);
        result.Contents[0].Should().BeOfType<TextContent>().Which.Text.Should().Be("text1");
        result.Contents[1].Should().BeOfType<TextContent>().Which.Text.Should().Be("text2");
        result.Contents[2].Should().BeOfType<UriContent>();
    }

    [Fact]
    public void ToChatMessages_ConvertsAll()
    {
        var messages = new List<VercelAIMessage>
        {
            new() { Role = "user" },
            new() { Role = "assistant" },
            new() { Role = "system" },
        };

        var result = messages.ToChatMessages();

        result.Should().HaveCount(3);
        result[0].Role.Should().Be(ChatRole.User);
        result[1].Role.Should().Be(ChatRole.Assistant);
        result[2].Role.Should().Be(ChatRole.System);
    }

    [Fact]
    public void EmptyParts_FallsBackToContent()
    {
        var message = new VercelAIMessage
        {
            Role = "user",
            Parts = new List<VercelAIMessagePart>(),
        };

        var result = message.ToChatMessage();

        // Empty parts list produces no converted content, so fallback adds empty TextContent
        result.Contents.Should().HaveCount(1);
        result.Contents[0].Should().BeOfType<TextContent>()
            .Which.Text.Should().Be(string.Empty);
    }
}
