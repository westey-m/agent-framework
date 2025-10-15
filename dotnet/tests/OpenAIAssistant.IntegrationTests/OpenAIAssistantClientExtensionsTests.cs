// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests.Support;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Assistants;
using OpenAI.Files;
using Shared.IntegrationTests;

namespace OpenAIAssistant.IntegrationTests;

public class OpenAIAssistantClientExtensionsTests
{
    private static readonly OpenAIConfiguration s_config = TestConfiguration.LoadSection<OpenAIConfiguration>();
    private readonly AssistantClient _assistantClient = new OpenAIClient(s_config.ApiKey).GetAssistantClient();
    private readonly OpenAIFileClient _fileClient = new OpenAIClient(s_config.ApiKey).GetOpenAIFileClient();

    [Theory]
    [InlineData("CreateWithChatClientAgentOptionsAsync")]
    [InlineData("CreateWithChatClientAgentOptionsSync")]
    [InlineData("CreateWithParamsAsync")]
    public async Task CreateAIAgentAsync_WithAIFunctionTool_InvokesFunctionAsync(string createMechanism)
    {
        // Arrange
        static string Echo(string text) => $"ECHO:{text}";
        var echoFunc = AIFunctionFactory.Create(Echo, name: "Echo");

        // Act
        var agent = createMechanism switch
        {
            "CreateWithChatClientAgentOptionsAsync" => await this._assistantClient.CreateAIAgentAsync(
                model: s_config.ChatModelId!,
                options: new ChatClientAgentOptions(
                    instructions: "Always call the Echo function and return its result.",
                    tools: [echoFunc])),
            "CreateWithChatClientAgentOptionsSync" => this._assistantClient.CreateAIAgent(
                model: s_config.ChatModelId!,
                options: new ChatClientAgentOptions(
                    instructions: "Always call the Echo function and return its result.",
                    tools: [echoFunc])),
            "CreateWithParamsAsync" => await this._assistantClient.CreateAIAgentAsync(
                model: s_config.ChatModelId!,
                instructions: "Always call the Echo function and return its result.",
                tools: [echoFunc]),
            _ => throw new InvalidOperationException($"Unknown create mechanism: {createMechanism}")
        };

        try
        {
            // Trigger function call.
            var response = await agent.RunAsync("Hello world");
            var text = response.ToString();

            // Assert
            Assert.Contains("ECHO:Hello world", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await this._assistantClient.DeleteAssistantAsync(agent.Id);
        }
    }

    [Theory]
    [InlineData("CreateWithChatClientAgentOptionsAsync")]
    [InlineData("CreateWithChatClientAgentOptionsSync")]
    [InlineData("CreateWithParamsAsync")]
    public async Task CreateAIAgentAsync_WithHostedCodeInterpreter_RunsCodeAsync(string createMechanism)
    {
        // Arrange
        const string Instructions = "Use the Code Interpreter Tool to run the uploaded python file and respond only with the secret number.";

        // Create a python file that prints a known value.
        var codeFilePath = Path.GetTempFileName() + "openai_secret_number.py";
        File.WriteAllText(
            path: codeFilePath,
            contents: "print(\"OPENAI_SECRET=13579\")" // Deterministic output we will look for.
        );

        // Upload file to OpenAI Assistants file store for use with the Code Interpreter.
        var uploadResult = await this._fileClient.UploadFileAsync(codeFilePath, FileUploadPurpose.Assistants);
        string uploadedFileId = uploadResult.Value.Id;
        var codeInterpreterTool = new HostedCodeInterpreterTool() { Inputs = [new HostedFileContent(uploadedFileId)] };

        var agent = createMechanism switch
        {
            "CreateWithChatClientAgentOptionsAsync" => await this._assistantClient.CreateAIAgentAsync(
                model: s_config.ChatModelId!,
                options: new ChatClientAgentOptions(
                    instructions: Instructions,
                    tools: [codeInterpreterTool])),
            "CreateWithChatClientAgentOptionsSync" => this._assistantClient.CreateAIAgent(
                model: s_config.ChatModelId!,
                options: new ChatClientAgentOptions(
                    instructions: Instructions,
                    tools: [codeInterpreterTool])),
            "CreateWithParamsAsync" => await this._assistantClient.CreateAIAgentAsync(
                model: s_config.ChatModelId!,
                instructions: Instructions,
                tools: [codeInterpreterTool]),
            _ => throw new InvalidOperationException($"Unknown create mechanism: {createMechanism}")
        };

        try
        {
            var response = await agent.RunAsync("What is the OPENAI_SECRET number?");
            var text = response.ToString();
            Assert.Contains("13579", text);
        }
        finally
        {
            await this._assistantClient.DeleteAssistantAsync(agent.Id);
            await this._fileClient.DeleteFileAsync(uploadedFileId);
            File.Delete(codeFilePath);
        }
    }
}
