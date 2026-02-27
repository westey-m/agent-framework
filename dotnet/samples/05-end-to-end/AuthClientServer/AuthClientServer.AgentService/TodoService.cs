// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;

namespace AuthClientServer.AgentService;

/// <summary>
/// Manages per-user TODO lists. Uses <see cref="IUserContext"/> to identify
/// the current caller without coupling to HTTP or claim-parsing details.
/// </summary>
public sealed class TodoService
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> s_todos = new();

    private readonly IUserContext _userContext;

    public TodoService(IUserContext userContext)
    {
        this._userContext = userContext;
    }

    /// <summary>
    /// Lists all TODO items for the currently authenticated user.
    /// </summary>
    [Description("Lists all TODO items for the current user.")]
    public string ListTodos()
    {
        var items = s_todos.GetOrAdd(this._userContext.UserId, _ => []);

        return items.IsEmpty
            ? "You have no TODO items."
            : string.Join("\n", items.Select((item, i) => $"{i + 1}. {item}"));
    }

    /// <summary>
    /// Adds a new TODO item for the currently authenticated user.
    /// </summary>
    [Description("Adds a new TODO item for the current user.")]
    public string AddTodo([Description("The TODO item to add")] string item)
    {
        var items = s_todos.GetOrAdd(this._userContext.UserId, _ => []);
        items.Add(item);

        return $"Added \"{item}\" to your TODO list.";
    }
}
