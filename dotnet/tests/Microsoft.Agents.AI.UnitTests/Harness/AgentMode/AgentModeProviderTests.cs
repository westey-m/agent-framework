// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Unit tests for the <see cref="AgentModeProvider"/> class.
/// </summary>
public class AgentModeProviderTests
{
    #region ProvideAIContextAsync Tests

    /// <summary>
    /// Verify that the provider returns tools and instructions.
    /// </summary>
    [Fact]
    public async Task ProvideAIContextAsync_ReturnsToolsAndInstructionsAsync()
    {
        // Arrange
        var provider = new AgentModeProvider();
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        // Act
        AIContext result = await provider.InvokingAsync(context);

        // Assert
        Assert.NotNull(result.Instructions);
        Assert.NotNull(result.Tools);
        Assert.Equal(2, result.Tools!.Count());
    }

    /// <summary>
    /// Verify that the instructions include the current mode.
    /// </summary>
    [Fact]
    public async Task ProvideAIContextAsync_InstructionsIncludeCurrentModeAsync()
    {
        // Arrange
        var provider = new AgentModeProvider();
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        // Act
        AIContext result = await provider.InvokingAsync(context);

        // Assert
        Assert.Contains("plan", result.Instructions);
    }

    #endregion

    #region SetMode Tool Tests

    /// <summary>
    /// Verify that SetMode changes the mode.
    /// </summary>
    [Fact]
    public async Task SetMode_ChangesModeAsync()
    {
        // Arrange
        var (tools, state) = await CreateToolsWithStateAsync();
        AIFunction setMode = GetTool(tools, "SetMode");

        // Act
        await setMode.InvokeAsync(new AIFunctionArguments() { ["mode"] = "execute" });

        // Assert
        Assert.Equal("execute", state.CurrentMode);
    }

    /// <summary>
    /// Verify that SetMode returns a confirmation message.
    /// </summary>
    [Fact]
    public async Task SetMode_ReturnsConfirmationAsync()
    {
        // Arrange
        var (tools, _) = await CreateToolsWithStateAsync();
        AIFunction setMode = GetTool(tools, "SetMode");

        // Act
        object? result = await setMode.InvokeAsync(new AIFunctionArguments() { ["mode"] = "execute" });

        // Assert
        Assert.Equal("Mode changed to \"execute\".", GetStringResult(result));
    }

    #endregion

    #region GetMode Tool Tests

    /// <summary>
    /// Verify that GetMode returns the default mode.
    /// </summary>
    [Fact]
    public async Task GetMode_ReturnsDefaultModeAsync()
    {
        // Arrange
        var (tools, _) = await CreateToolsWithStateAsync();
        AIFunction getMode = GetTool(tools, "GetMode");

        // Act
        object? result = await getMode.InvokeAsync(new AIFunctionArguments());

        // Assert
        Assert.Equal("plan", GetStringResult(result));
    }

    /// <summary>
    /// Verify that GetMode returns the mode after SetMode.
    /// </summary>
    [Fact]
    public async Task GetMode_ReturnsUpdatedModeAfterSetAsync()
    {
        // Arrange
        var (tools, _) = await CreateToolsWithStateAsync();
        AIFunction setMode = GetTool(tools, "SetMode");
        AIFunction getMode = GetTool(tools, "GetMode");

        // Act
        await setMode.InvokeAsync(new AIFunctionArguments() { ["mode"] = "execute" });
        object? result = await getMode.InvokeAsync(new AIFunctionArguments());

        // Assert
        Assert.Equal("execute", GetStringResult(result));
    }

    #endregion

    #region Public Helper Method Tests

    /// <summary>
    /// Verify that the public GetMode helper returns the default mode.
    /// </summary>
    [Fact]
    public void PublicGetMode_ReturnsDefaultMode()
    {
        // Arrange
        var provider = new AgentModeProvider();
        var session = new ChatClientAgentSession();

        // Act
        string mode = provider.GetMode(session);

        // Assert
        Assert.Equal(AgentModeProvider.ModePlan, mode);
    }

    /// <summary>
    /// Verify that the public SetMode helper changes the mode.
    /// </summary>
    [Fact]
    public void PublicSetMode_ChangesMode()
    {
        // Arrange
        var provider = new AgentModeProvider();
        var session = new ChatClientAgentSession();

        // Act
        provider.SetMode(session, AgentModeProvider.ModeExecute);
        string mode = provider.GetMode(session);

        // Assert
        Assert.Equal(AgentModeProvider.ModeExecute, mode);
    }

    /// <summary>
    /// Verify that public helper changes are reflected in tool results.
    /// </summary>
    [Fact]
    public async Task PublicSetMode_ReflectedInToolResultsAsync()
    {
        // Arrange
        var provider = new AgentModeProvider();
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();

        // Set mode via public helper
        provider.SetMode(session, AgentModeProvider.ModeExecute);

#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        // Act
        AIContext result = await provider.InvokingAsync(context);
        AIFunction getMode = GetTool(result.Tools!, "GetMode");
        object? modeResult = await getMode.InvokeAsync(new AIFunctionArguments());

        // Assert
        Assert.Equal("execute", GetStringResult(modeResult));
        Assert.Contains("execute", result.Instructions);
    }

    #endregion

    #region State Persistence Tests

    /// <summary>
    /// Verify that state persists across invocations.
    /// </summary>
    [Fact]
    public async Task State_PersistsAcrossInvocationsAsync()
    {
        // Arrange
        var provider = new AgentModeProvider();
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        // Act — first invocation changes mode
        AIContext result1 = await provider.InvokingAsync(context);
        AIFunction setMode = GetTool(result1.Tools!, "SetMode");
        await setMode.InvokeAsync(new AIFunctionArguments() { ["mode"] = "execute" });

        // Second invocation should see the updated mode
        AIContext result2 = await provider.InvokingAsync(context);
        AIFunction getMode = GetTool(result2.Tools!, "GetMode");
        object? modeResult = await getMode.InvokeAsync(new AIFunctionArguments());

        // Assert
        Assert.Equal("execute", GetStringResult(modeResult));
        Assert.Contains("execute", result2.Instructions);
    }

    #endregion

    #region Constants Tests

    /// <summary>
    /// Verify that mode constants have expected values.
    /// </summary>
    [Fact]
    public void ModeConstants_HaveExpectedValues()
    {
        // Assert
        Assert.Equal("plan", AgentModeProvider.ModePlan);
        Assert.Equal("execute", AgentModeProvider.ModeExecute);
    }

    #endregion

    #region Helper Methods

    private static async Task<(IEnumerable<AITool> Tools, AgentModeState State)> CreateToolsWithStateAsync()
    {
        var provider = new AgentModeProvider();
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        AIContext result = await provider.InvokingAsync(context);

        // Retrieve the state from the session to verify mutations
        session.StateBag.TryGetValue<AgentModeState>("AgentModeProvider", out var state, AgentJsonUtilities.DefaultOptions);

        return (result.Tools!, state!);
    }

    private static AIFunction GetTool(IEnumerable<AITool> tools, string name)
    {
        return (AIFunction)tools.First(t => t is AIFunction f && f.Name == name);
    }

    private static string GetStringResult(object? result)
    {
        var element = Assert.IsType<JsonElement>(result);
        return element.GetString()!;
    }

    #endregion
}
