// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.UnitTests.Harness.FileMemory;

public class FileMemoryProviderTests
{
    #region Constructor Validation

    [Fact]
    public void Constructor_NullFileStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FileMemoryProvider(null!));
    }

    [Fact]
    public void Constructor_WithDefaults_Succeeds()
    {
        // Act
        var provider = new FileMemoryProvider(new InMemoryAgentFileStore());

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithStateInitializer_Succeeds()
    {
        // Act
        var provider = new FileMemoryProvider(
            new InMemoryAgentFileStore(),
            _ => new FileMemoryState { WorkingFolder = "custom" });

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region ProvideAIContextAsync Tests

    [Fact]
    public async Task ProvideAIContextAsync_ReturnsToolsAsync()
    {
        // Arrange
        var (tools, _) = await CreateToolsAsync();

        // Assert - 5 tools: SaveFile, ReadFile, DeleteFile, ListFiles, SearchFiles
        Assert.Equal(5, tools.Count());
    }

    [Fact]
    public async Task ProvideAIContextAsync_ReturnsInstructionsAsync()
    {
        // Arrange
        var provider = new FileMemoryProvider(new InMemoryAgentFileStore());
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        // Act
        AIContext result = await provider.InvokingAsync(context);

        // Assert
        Assert.NotNull(result.Instructions);
        Assert.Contains("file-based memory", result.Instructions);
        Assert.Contains("compacted", result.Instructions);
    }

    #endregion

    #region SaveFile Tests

    [Fact]
    public async Task SaveFile_CreatesFileAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        var (tools, _) = await CreateToolsAsync(store);
        var saveFile = GetTool(tools, "FileMemory_SaveFile");

        // Act
        await InvokeWithRunContextAsync(saveFile, new AIFunctionArguments
        {
            ["fileName"] = "notes.md",
            ["content"] = "Test content",
            ["description"] = "",
        });

        // Assert
        var content = await store.ReadFileAsync("notes.md");
        Assert.Equal("Test content", content);
    }

    [Fact]
    public async Task SaveFile_WithDescription_CreatesBothFilesAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        var (tools, _) = await CreateToolsAsync(store);
        var saveFile = GetTool(tools, "FileMemory_SaveFile");

        // Act
        await InvokeWithRunContextAsync(saveFile, new AIFunctionArguments
        {
            ["fileName"] = "research.md",
            ["content"] = "Long research content...",
            ["description"] = "Summary of research findings",
        });

        // Assert
        var content = await store.ReadFileAsync("research.md");
        Assert.Equal("Long research content...", content);
        var desc = await store.ReadFileAsync("research_description.md");
        Assert.Equal("Summary of research findings", desc);
    }

    [Fact]
    public async Task SaveFile_WithCustomState_CreatesInSubfolderAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        var (tools, state) = await CreateToolsAsync(store, _ => new FileMemoryState { WorkingFolder = "session123" });
        var saveFile = GetTool(tools, "FileMemory_SaveFile");

        // Act
        await InvokeWithRunContextAsync(saveFile, new AIFunctionArguments
        {
            ["fileName"] = "notes.md",
            ["content"] = "Session content",
            ["description"] = "",
        });

        // Assert
        Assert.Equal("session123", state.WorkingFolder);
        var content = await store.ReadFileAsync("session123/notes.md");
        Assert.Equal("Session content", content);
    }

    #endregion

    #region ReadFile Tests

    [Fact]
    public async Task ReadFile_ExistingFile_ReturnsContentAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        await store.WriteFileAsync("notes.md", "Stored content");
        var (tools, _) = await CreateToolsAsync(store);
        var readFile = GetTool(tools, "FileMemory_ReadFile");

        // Act
        var result = await InvokeWithRunContextAsync(readFile, new AIFunctionArguments
        {
            ["fileName"] = "notes.md",
        });

        // Assert
        var text = Assert.IsType<JsonElement>(result).GetString();
        Assert.Equal("Stored content", text);
    }

    [Fact]
    public async Task ReadFile_NonExistent_ReturnsNotFoundMessageAsync()
    {
        // Arrange
        var (tools, _) = await CreateToolsAsync();
        var readFile = GetTool(tools, "FileMemory_ReadFile");

        // Act
        var result = await InvokeWithRunContextAsync(readFile, new AIFunctionArguments
        {
            ["fileName"] = "nonexistent.md",
        });

        // Assert
        var text = Assert.IsType<JsonElement>(result).GetString();
        Assert.Contains("not found", text);
    }

    #endregion

    #region DeleteFile Tests

    [Fact]
    public async Task DeleteFile_ExistingFile_DeletesAndReturnsConfirmationAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        await store.WriteFileAsync("notes.md", "Content");
        var (tools, _) = await CreateToolsAsync(store);
        var deleteFile = GetTool(tools, "FileMemory_DeleteFile");

        // Act
        var result = await InvokeWithRunContextAsync(deleteFile, new AIFunctionArguments
        {
            ["fileName"] = "notes.md",
        });

        // Assert
        var text = Assert.IsType<JsonElement>(result).GetString();
        Assert.Contains("deleted", text);
        Assert.False(await store.FileExistsAsync("notes.md"));
    }

    [Fact]
    public async Task DeleteFile_AlsoDeletesDescriptionFileAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        await store.WriteFileAsync("notes.md", "Content");
        await store.WriteFileAsync("notes_description.md", "Description");
        var (tools, _) = await CreateToolsAsync(store);
        var deleteFile = GetTool(tools, "FileMemory_DeleteFile");

        // Act
        await InvokeWithRunContextAsync(deleteFile, new AIFunctionArguments
        {
            ["fileName"] = "notes.md",
        });

        // Assert
        Assert.False(await store.FileExistsAsync("notes.md"));
        Assert.False(await store.FileExistsAsync("notes_description.md"));
    }

    #endregion

    #region ListFiles Tests

    [Fact]
    public async Task ListFiles_ReturnsFilesWithDescriptionsAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        await store.WriteFileAsync("notes.md", "Content");
        await store.WriteFileAsync("notes_description.md", "A description");
        await store.WriteFileAsync("other.md", "Other content");
        var (tools, _) = await CreateToolsAsync(store);
        var listFiles = GetTool(tools, "FileMemory_ListFiles");

        // Act
        var result = await InvokeWithRunContextAsync(listFiles, new AIFunctionArguments());

        // Assert
        var entries = Assert.IsType<JsonElement>(result).EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);

        var notesEntry = entries.First(e => e.GetProperty("fileName").GetString() == "notes.md");
        Assert.Equal("A description", notesEntry.GetProperty("description").GetString());

        var otherEntry = entries.First(e => e.GetProperty("fileName").GetString() == "other.md");
        Assert.False(otherEntry.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task ListFiles_HidesDescriptionFilesAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        await store.WriteFileAsync("notes.md", "Content");
        await store.WriteFileAsync("notes_description.md", "Desc");
        var (tools, _) = await CreateToolsAsync(store);
        var listFiles = GetTool(tools, "FileMemory_ListFiles");

        // Act
        var result = await InvokeWithRunContextAsync(listFiles, new AIFunctionArguments());

        // Assert
        var entries = Assert.IsType<JsonElement>(result).EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("notes.md", entries[0].GetProperty("fileName").GetString());
    }

    #endregion

    #region SearchFiles Tests

    [Fact]
    public async Task SearchFiles_FindsMatchingContentAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        await store.WriteFileAsync("notes.md", "Important research findings about AI");
        var (tools, _) = await CreateToolsAsync(store);
        var searchFiles = GetTool(tools, "FileMemory_SearchFiles");

        // Act
        var result = await InvokeWithRunContextAsync(searchFiles, new AIFunctionArguments
        {
            ["regexPattern"] = "research findings",
            ["filePattern"] = "",
        });

        // Assert
        var entries = Assert.IsType<JsonElement>(result).EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("notes.md", entries[0].GetProperty("fileName").GetString());
        Assert.True(entries[0].TryGetProperty("matchingLines", out var matchingLines));
        Assert.True(matchingLines.GetArrayLength() > 0);
    }

    [Fact]
    public async Task SearchFiles_WithFilePattern_FiltersResultsAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        await store.WriteFileAsync("notes.md", "Important data");
        await store.WriteFileAsync("data.txt", "Important data");
        var (tools, _) = await CreateToolsAsync(store);
        var searchFiles = GetTool(tools, "FileMemory_SearchFiles");

        // Act
        var result = await InvokeWithRunContextAsync(searchFiles, new AIFunctionArguments
        {
            ["regexPattern"] = "Important",
            ["filePattern"] = "*.md",
        });

        // Assert
        var entries = Assert.IsType<JsonElement>(result).EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("notes.md", entries[0].GetProperty("fileName").GetString());
    }

    #endregion

    #region State Initializer Tests

    [Fact]
    public async Task CustomStateInitializer_SetsWorkingFolderAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        var (_, state) = await CreateToolsAsync(store, _ => new FileMemoryState { WorkingFolder = "user42" });

        // Assert
        Assert.Equal("user42", state.WorkingFolder);
    }

    [Fact]
    public async Task DefaultStateInitializer_UsesEmptyWorkingFolderAsync()
    {
        // Arrange
        var (_, state) = await CreateToolsAsync();

        // Assert
        Assert.Equal(string.Empty, state.WorkingFolder);
    }

    [Fact]
    public async Task State_PersistsAcrossInvocationsAsync()
    {
        // Arrange
        var store = new InMemoryAgentFileStore();
        var provider = new FileMemoryProvider(store, _ => new FileMemoryState { WorkingFolder = "persistent" });
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        // Act - first invocation initializes state
        await provider.InvokingAsync(context);
        session.StateBag.TryGetValue<FileMemoryState>("FileMemoryProvider", out var state1, AgentJsonUtilities.DefaultOptions);

        // Second invocation should reuse the same folder
        await provider.InvokingAsync(context);
        session.StateBag.TryGetValue<FileMemoryState>("FileMemoryProvider", out var state2, AgentJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(state1);
        Assert.NotNull(state2);
        Assert.Equal(state1!.WorkingFolder, state2!.WorkingFolder);
    }

    #endregion

    #region Path Traversal Protection

    [Fact]
    public async Task SaveFile_PathTraversal_ThrowsAsync()
    {
        // Arrange
        var (tools, _) = await CreateToolsAsync();
        var saveFile = GetTool(tools, "FileMemory_SaveFile");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await InvokeWithRunContextAsync(saveFile, new AIFunctionArguments
            {
                ["fileName"] = "../escape.md",
                ["content"] = "Content",
                ["description"] = "",
            }));
    }

    [Fact]
    public async Task SaveFile_AbsolutePath_ThrowsAsync()
    {
        // Arrange
        var (tools, _) = await CreateToolsAsync();
        var saveFile = GetTool(tools, "FileMemory_SaveFile");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await InvokeWithRunContextAsync(saveFile, new AIFunctionArguments
            {
                ["fileName"] = "/etc/passwd",
                ["content"] = "Content",
                ["description"] = "",
            }));
    }

    #endregion

    #region Helper Methods

    private static FileMemoryProvider CreateProvider(InMemoryAgentFileStore? store = null, Func<AgentSession?, FileMemoryState>? stateInitializer = null)
    {
        return new FileMemoryProvider(store ?? new InMemoryAgentFileStore(), stateInitializer);
    }

    private static async Task<(IEnumerable<AITool> Tools, FileMemoryState State)> CreateToolsAsync(InMemoryAgentFileStore? store = null, Func<AgentSession?, FileMemoryState>? stateInitializer = null)
    {
        var provider = CreateProvider(store, stateInitializer);
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
#pragma warning disable MAAI001
        var context = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001

        AIContext result = await provider.InvokingAsync(context);

        session.StateBag.TryGetValue<FileMemoryState>("FileMemoryProvider", out var state, AgentJsonUtilities.DefaultOptions);

        return (result.Tools!, state!);
    }

    private static AIFunction GetTool(IEnumerable<AITool> tools, string name)
    {
        return (AIFunction)tools.First(t => t is AIFunction f && f.Name == name);
    }

    /// <summary>
    /// Invokes a tool within a mock <see cref="AIAgent.CurrentRunContext"/> so that
    /// the tool methods can access the session via <c>AIAgent.CurrentRunContext?.Session</c>.
    /// </summary>
    private static async Task<object?> InvokeWithRunContextAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        var agent = new Mock<AIAgent>().Object;
        var session = new ChatClientAgentSession();
        var messages = new List<ChatMessage>();

        // Set up the ambient run context so tool methods can access the session.
        var runContext = new AgentRunContext(agent, session, messages, null);

        // Use reflection to set the protected static CurrentRunContext property.
        var property = typeof(AIAgent).GetProperty("CurrentRunContext", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var setter = property!.GetSetMethod(true)!;
        var previousContext = AIAgent.CurrentRunContext;
        try
        {
            setter.Invoke(null, [runContext]);
            return await tool.InvokeAsync(arguments);
        }
        finally
        {
            setter.Invoke(null, [previousContext]);
        }
    }

    #endregion
}
