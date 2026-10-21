// <copyright file="McpSessionServer.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Mcp;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Dramaturge.Browsers;
using Dramaturge.Tool.Sessions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// Serves the session commands to an MCP host as tools, each sent to a session's daemon as the command line would
/// send it. The sessions the host starts get the settings the server was started with, and are stopped when the
/// host closes the connection.
/// </summary>
internal sealed class McpSessionServer
{
    private const string ServerName = "dramaturge";

    private readonly SessionSettings template;
    private readonly string? cachePath;
    private readonly SessionHost host;
    private readonly TextWriter progress;
    private readonly HashSet<string> started = [];
    private readonly ListToolsResult tools = new() { Tools = McpTools.Describe() };

    private McpSessionServer(SessionSettings template, string? cachePath, SessionHost host, TextWriter progress)
    {
        this.template = template;
        this.cachePath = cachePath;
        this.host = host;
        this.progress = progress;
    }

    /// <summary>
    /// Serves a host until it closes the connection, or the server is cancelled, then stops the sessions the host
    /// started.
    /// </summary>
    /// <param name="template">The settings every session the host starts gets, but for its name, browser, and channel.</param>
    /// <param name="cachePath">The browser cache directory, or <see langword="null"/> for the default.</param>
    /// <param name="host">Where sessions are kept, how their daemons start, and the streams to serve on.</param>
    /// <param name="output">The writer for a usage error's JSON, which is not used otherwise.</param>
    /// <param name="error">The writer for the daemons' progress, and for a usage error.</param>
    /// <param name="cancellationToken">A token that cancels the server.</param>
    /// <returns>The exit code: 0 when the host went away, or 2 when the settings cannot be used.</returns>
    public static async Task<int> RunAsync(SessionSettings template, string? cachePath, SessionHost host, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (DramaturgeTool.CheckSettings(template) is string problem)
        {
            return CommandOutput.Write(CommandOutcome.Failure(new SessionError(CommandOutput.Usage, problem)), false, output, error);
        }

        McpSessionServer server = new(template, cachePath, host, error);
        McpServerOptions options = new()
        {
            ServerInfo = new() { Name = ServerName, Version = typeof(McpSessionServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0] },
            Capabilities = new() { Tools = new() },
            ServerInstructions = Instructions(),
        };
        options.Handlers.ListToolsHandler = (_, _) => ValueTask.FromResult(server.tools);
        options.Handlers.CallToolHandler = (request, token) => new ValueTask<CallToolResult>(server.CallAsync(request.Params, token));
        McpServer mcpServer = McpServer.Create(CreateTransport(host), options);
        try
        {
            // Returns when the host closes its end, and when the token is cancelled.
            await mcpServer.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await mcpServer.DisposeAsync().ConfigureAwait(false);
            await server.StopStartedAsync().ConfigureAwait(false);
        }

        return 0;
    }

    // Tests serve on streams of their own; a real run serves on the console.
    [ExcludeFromCodeCoverage]
    private static ITransport CreateTransport(SessionHost host)
    {
        return host.Streams is StandardStreams streams ? new StreamServerTransport(streams.Input, streams.Output, ServerName) : new StdioServerTransport(ServerName);
    }

    private static string Instructions()
    {
        using Stream stream = typeof(McpSessionServer).Assembly.GetManifestResourceStream("Dramaturge.Tool.Mcp.instructions.md")!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    // A call that cannot be made, as one of a tool that does not exist or with an argument of the wrong kind, is
    // answered as a usage error, as the command line answers arguments it cannot use.
    private async Task<CallToolResult> CallAsync(CallToolRequestParams call, CancellationToken cancellationToken)
    {
        try
        {
            IDictionary<string, JsonElement> arguments = call.Arguments ?? new Dictionary<string, JsonElement>();
            string name = McpTools.Text(call.Name, arguments, "session") ?? SessionPaths.DefaultName;
            switch (call.Name)
            {
                case "session_start":
                    return McpResults.Of(await this.StartAsync(name, arguments, cancellationToken).ConfigureAwait(false));
                case "session_list":
                    return McpResults.Of(await DramaturgeTool.ListSessionsAsync(this.host, cancellationToken).ConfigureAwait(false));
                case "session_stop":
                    CommandOutcome stopped = await DramaturgeTool.StopSessionAsync(name, this.host, cancellationToken).ConfigureAwait(false);
                    if (stopped.Error is null)
                    {
                        lock (this.started)
                        {
                            this.started.Remove(name);
                        }
                    }

                    return McpResults.Of(stopped);
                default:
                    SessionCommand command = McpTools.Find(call.Name) ?? throw new McpException($"There is no tool named '{call.Name}'.");
                    if (SessionPaths.CheckName(name) is string problem)
                    {
                        throw new McpException(problem);
                    }

                    SessionReply reply = await SessionClient.SendAsync(SessionPaths.SocketPath(SessionPaths.Prepare(this.host.Directory), name), name, command.Name, McpTools.Arguments(command, arguments), cancellationToken).ConfigureAwait(false);
                    return McpResults.Of(command.Name, reply);
            }
        }
        catch (McpException ex)
        {
            return McpResults.Of(CommandOutcome.Failure(new SessionError(CommandOutput.Usage, ex.Message)));
        }
    }

    private async Task<CommandOutcome> StartAsync(string name, IDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        string browserName = McpTools.Text("session_start", arguments, "browser") ?? "chrome";
        if (!Enum.TryParse(browserName, true, out BrowserKind browser))
        {
            return CommandOutcome.Failure(new SessionError(CommandOutput.Usage, $"'{browserName}' is not a browser: chrome, firefox, or edge."));
        }

        SessionSettings settings = this.template with { Name = name, Browser = browser, Channel = McpTools.Text("session_start", arguments, "channel") };
        CommandOutcome outcome = await DramaturgeTool.StartSessionAsync(settings, this.cachePath, this.host, this.progress, cancellationToken).ConfigureAwait(false);
        if (outcome.Error is null)
        {
            lock (this.started)
            {
                this.started.Add(name);
            }
        }

        return outcome;
    }

    // The sessions the host started and did not stop; one that stopped on its own is reported, and its socket removed.
    private async Task StopStartedAsync()
    {
        string[] names;
        lock (this.started)
        {
            names = [.. this.started];
            this.started.Clear();
        }

        foreach (string name in names)
        {
            await DramaturgeTool.StopSessionAsync(name, this.host, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
