// <copyright file="McpTools.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Mcp;

using System.Text.Json;
using Dramaturge.Browsers;
using Dramaturge.Tool.Sessions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

/// <summary>
/// The tools an MCP host is offered: the session commands of the catalogue, under their own names, and the tools
/// that start, list, and stop sessions. Each tool's schema is its command's parameters, and a call's arguments are
/// turned back into the command's, as the command line would send them.
/// </summary>
internal static class McpTools
{
    private const string SessionName = "session";

    private static readonly SessionParameter Session = new(SessionName, ParameterKind.Text, "The session's name; 'default' when not given.");
    private static readonly SessionParameter Browser = new("browser", ParameterKind.Text, "The browser to drive: chrome (the default), firefox, or edge.", Choices: [.. Enum.GetNames<BrowserKind>().Select(name => name.ToLowerInvariant())], Default: "chrome");
    private static readonly SessionParameter Channel = new("channel", ParameterKind.Text, "The browser's release channel, as browsers install names it: stable (the default), beta, dev, and canary for Chrome and Edge, or nightly and esr for Firefox.");
    private static readonly HashSet<string> ReadOnly = ["session_list", "url", "snapshot", "tabs", "console"];
    private static readonly Dictionary<string, object?> NoArguments = [];

    /// <summary>
    /// Gets the tools, in the order they are offered: the session tools, then the catalogue's commands.
    /// </summary>
    /// <returns>The tools.</returns>
    public static IList<Tool> Describe() =>
    [
        Tool("session_start", "Start a browser session that the other tools drive. It stops with session_stop, when its browser closes, when it is idle, or when this server exits.", [Session, Browser, Channel]),
        Tool("session_list", "List the sessions running.", []),
        Tool("session_stop", "Stop a session, closing its browser.", [Session]),
        .. SessionCommands.Page.Select(command => Tool(command.Name, command.Description, [.. command.Parameters, Session])),
    ];

    /// <summary>
    /// Finds the catalogue's command a tool is named for.
    /// </summary>
    /// <param name="tool">The tool's name.</param>
    /// <returns>The command, or <see langword="null"/> when no command has the name.</returns>
    public static SessionCommand? Find(string tool) => SessionCommands.Page.FirstOrDefault(command => command.Name == tool);

    /// <summary>
    /// Reads a text argument of a call.
    /// </summary>
    /// <param name="tool">The tool called.</param>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="name">The argument's name.</param>
    /// <returns>The text, or <see langword="null"/> when the argument is not given, or is null.</returns>
    /// <exception cref="McpException">The argument is not a string.</exception>
    public static string? Text(string tool, IDictionary<string, JsonElement> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw NotOfKind(tool, name, ParameterKind.Text);
    }

    /// <summary>
    /// Turns a call's arguments into a command's, as the command line would send them: the command's fixed arguments,
    /// each parameter given, paths made absolute, and the defaults of those not given.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The command's arguments.</returns>
    /// <exception cref="McpException">An argument is not of its parameter's kind.</exception>
    public static Dictionary<string, object?> Arguments(SessionCommand command, IDictionary<string, JsonElement> arguments)
    {
        Dictionary<string, object?> sent = new(command.Fixed ?? NoArguments);
        foreach (SessionParameter parameter in command.Parameters)
        {
            if (arguments.TryGetValue(parameter.Name, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
            {
                sent[parameter.Name] = Read(command.Name, parameter, value);
            }
            else if (parameter.Form == ParameterForm.InvertedOption)
            {
                sent[parameter.Name] = true;
            }
            else if (parameter.Default is not null)
            {
                sent[parameter.Name] = parameter.Default;
            }
        }

        return sent;
    }

    private static object Read(string tool, SessionParameter parameter, JsonElement value)
    {
        bool ofKind = parameter.Kind switch
        {
            ParameterKind.Number => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            ParameterKind.Flag => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ParameterKind.Texts or ParameterKind.Paths => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
            _ => value.ValueKind == JsonValueKind.String,
        };
        if (!ofKind)
        {
            throw NotOfKind(tool, parameter.Name, parameter.Kind);
        }

        return parameter.Kind switch
        {
            ParameterKind.Path => Path.GetFullPath(value.GetString()!),
            ParameterKind.Paths => value.EnumerateArray().Select(item => Path.GetFullPath(item.GetString()!)).ToArray(),
            _ => value,
        };
    }

    private static McpException NotOfKind(string tool, string name, ParameterKind kind)
    {
        string expected = kind switch
        {
            ParameterKind.Number => "a whole number",
            ParameterKind.Flag => "true or false",
            ParameterKind.Texts or ParameterKind.Paths => "a list of strings",
            _ => "a string",
        };
        return new McpException($"The argument '{name}' of '{tool}' must be {expected}.");
    }

    private static Tool Tool(string name, string description, IReadOnlyList<SessionParameter> parameters) => new()
    {
        Name = name,
        Description = description,
        InputSchema = Schema(parameters),
        Annotations = new() { ReadOnlyHint = ReadOnly.Contains(name), DestructiveHint = false },
    };

    // A JSON schema of an object with the parameters as its properties, the positional ones required.
    private static JsonElement Schema(IReadOnlyList<SessionParameter> parameters)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            foreach (SessionParameter parameter in parameters)
            {
                writer.WriteStartObject(parameter.Name);
                writer.WriteString("type", parameter.Kind switch { ParameterKind.Number => "integer", ParameterKind.Flag => "boolean", ParameterKind.Texts or ParameterKind.Paths => "array", _ => "string" });
                if (parameter.Kind is ParameterKind.Texts or ParameterKind.Paths)
                {
                    writer.WriteStartObject("items");
                    writer.WriteString("type", "string");
                    writer.WriteEndObject();
                }

                writer.WriteString("description", parameter.SchemaDescription ?? parameter.Description);
                if (parameter.Choices is not null)
                {
                    writer.WriteStartArray("enum");
                    foreach (string choice in parameter.Choices)
                    {
                        writer.WriteStringValue(choice);
                    }

                    writer.WriteEndArray();
                }

                if (parameter.Default is not null)
                {
                    writer.WriteString("default", parameter.Default);
                }

                if (parameter.Form == ParameterForm.InvertedOption)
                {
                    writer.WriteBoolean("default", true);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            string[] required = [.. parameters.Where(parameter => parameter.Form == ParameterForm.Argument).Select(parameter => parameter.Name)];
            if (required.Length > 0)
            {
                writer.WriteStartArray("required");
                foreach (string name in required)
                {
                    writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
            }

            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}
