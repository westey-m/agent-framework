// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.AI.UnitTests.Harness.FileMemory;

/// <summary>
/// Unit tests for the <see cref="FileEditor"/> helper that backs the <c>replace</c> and
/// <c>replace_lines</c> tools.
/// </summary>
public class FileEditorTests
{
    #region ApplyReplace

    [Fact]
    public void ApplyReplace_SingleOccurrence_ReplacesAndReturnsCount()
    {
        // Act
        (string content, int count) = FileEditor.ApplyReplace("Hello world", "world", "there", replaceAll: false);

        // Assert
        Assert.Equal("Hello there", content);
        Assert.Equal(1, count);
    }

    [Fact]
    public void ApplyReplace_ReplaceAll_ReplacesEveryOccurrence()
    {
        // Act
        (string content, int count) = FileEditor.ApplyReplace("a a a", "a", "b", replaceAll: true);

        // Assert
        Assert.Equal("b b b", content);
        Assert.Equal(3, count);
    }

    [Fact]
    public void ApplyReplace_EmptyOldString_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplace("content", string.Empty, "x", replaceAll: false));
    }

    [Fact]
    public void ApplyReplace_NotFound_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplace("content", "missing", "x", replaceAll: false));
    }

    [Fact]
    public void ApplyReplace_MultipleOccurrences_WithoutReplaceAll_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplace("a a a", "a", "b", replaceAll: false));
    }

    #endregion

    #region ApplyReplaceLines

    [Fact]
    public void ApplyReplaceLines_ReplacesSpecifiedLine()
    {
        // Act
        string result = FileEditor.ApplyReplaceLines(
            "line1\nline2\nline3",
            new List<FileLineEdit> { new() { LineNumber = 2, NewLine = "CHANGED" } });

        // Assert
        Assert.Equal("line1\nCHANGED\nline3", result);
    }

    [Fact]
    public void ApplyReplaceLines_EmptyEdits_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplaceLines("line1", new List<FileLineEdit>()));
    }

    [Fact]
    public void ApplyReplaceLines_OutOfRange_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplaceLines(
            "line1\nline2",
            new List<FileLineEdit> { new() { LineNumber = 5, NewLine = "X" } }));
    }

    [Fact]
    public void ApplyReplaceLines_DuplicateLineNumber_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplaceLines(
            "line1\nline2",
            new List<FileLineEdit>
            {
                new() { LineNumber = 1, NewLine = "A" },
                new() { LineNumber = 1, NewLine = "B" },
            }));
    }

    [Fact]
    public void ApplyReplaceLines_PreservesTrailingNewline()
    {
        // Act
        string result = FileEditor.ApplyReplaceLines(
            "line1\nline2\n",
            new List<FileLineEdit> { new() { LineNumber = 1, NewLine = "CHANGED" } });

        // Assert
        Assert.Equal("CHANGED\nline2\n", result);
    }

    [Fact]
    public void ApplyReplaceLines_PreservesCrlfLineEndings()
    {
        // Act — a CRLF file should retain CRLF endings after a surgical line edit.
        string result = FileEditor.ApplyReplaceLines(
            "line1\r\nline2\r\nline3",
            new List<FileLineEdit> { new() { LineNumber = 2, NewLine = "CHANGED" } });

        // Assert
        Assert.Equal("line1\r\nCHANGED\r\nline3", result);
    }

    [Fact]
    public void ApplyReplaceLines_PreservesCrlfWithTrailingNewline()
    {
        // Act
        string result = FileEditor.ApplyReplaceLines(
            "line1\r\nline2\r\n",
            new List<FileLineEdit> { new() { LineNumber = 1, NewLine = "CHANGED" } });

        // Assert
        Assert.Equal("CHANGED\r\nline2\r\n", result);
    }

    #endregion
}
