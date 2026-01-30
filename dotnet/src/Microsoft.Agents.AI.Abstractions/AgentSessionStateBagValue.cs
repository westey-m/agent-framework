// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI;

/// <summary>
/// Used to store a value in session state.
/// </summary>
internal class AgentSessionStateBagValue
{
    /// <summary>
    /// Initializes a new instance of the SessionStateValue class with the specified value.
    /// </summary>
    /// <param name="jsonValue">The serialized value to associate with the session state.</param>
    [JsonConstructor]
    public AgentSessionStateBagValue(JsonElement jsonValue)
    {
        this.JsonValue = jsonValue;
    }

    /// <summary>
    /// Initializes a new instance of the SessionStateValue class with the specified value.
    /// </summary>
    /// <param name="deserializedValue">The value to associate with the session state. Can be any object, including null.</param>
    /// <param name="valueType">The type of the value.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options to use for serializing the value.</param>
    public AgentSessionStateBagValue(object deserializedValue, Type valueType, JsonSerializerOptions jsonSerializerOptions)
    {
        this.DeserializedValue = deserializedValue;
        this.ValueType = valueType;
        this.JsonSerializerOptions = jsonSerializerOptions;
    }

    /// <summary>
    /// Gets or sets the value associated with this instance.
    /// </summary>
    public JsonElement JsonValue
    {
        get
        {
            if (this.DeserializedValue != null)
            {
                if (this.ValueType is null || this.JsonSerializerOptions is null)
                {
                    throw new InvalidOperationException($"{nameof(AgentSessionStateBagValue)} is not properly initialized.");
                }

                return JsonSerializer.SerializeToElement(this.DeserializedValue, this.JsonSerializerOptions.GetTypeInfo(this.ValueType));
            }

            return field;
        }
        set;
    }

    [JsonIgnore]
    public object? DeserializedValue { get; set; }

    [JsonIgnore]
    public Type? ValueType { get; set; }

    [JsonIgnore]
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
