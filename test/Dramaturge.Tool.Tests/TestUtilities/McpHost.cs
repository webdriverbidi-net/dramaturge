// <copyright file="McpHost.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.TestUtilities;

using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// An MCP host connected to the tool's <c>mcp</c> command, which serves in the test's own process on a pair of
/// pipes in place of the console, with the sessions' host.
/// </summary>
public sealed class McpHost : IAsyncDisposable
{
    private readonly Pipe requests = new();
    private readonly Pipe answers = new();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();
    private readonly CancellationTokenSource cancellation = new();

    private McpHost()
    {
    }

    /// <summary>
    /// Gets the client connected to the server.
    /// </summary>
    public McpClient Client { get; private set; } = null!;

    /// <summary>
    /// Gets the server's run, whose result is its exit code.
    /// </summary>
    public Task<int> Serving { get; private set; } = null!;

    /// <summary>
    /// Gets what the server wrote to its output writer, which should be nothing.
    /// </summary>
    public string Output => this.output.ToString();

    /// <summary>
    /// Gets what the server wrote to its error writer.
    /// </summary>
    public string Error => this.error.ToString();

    /// <summary>
    /// Starts the server and connects to it.
    /// </summary>
    /// <param name="sessions">The sessions.</param>
    /// <param name="arguments">The arguments of <c>mcp</c>.</param>
    /// <returns>The host.</returns>
    public static async Task<McpHost> StartAsync(InProcessSessions sessions, params string[] arguments)
    {
        McpHost host = new();
        SessionHost sessionHost = sessions.Host(new StandardStreams(host.requests.Reader.AsStream(), host.answers.Writer.AsStream()));
        host.Serving = DramaturgeTool.RunAsync(["mcp", .. arguments], host.output, host.error, sessionHost: sessionHost, cancellationToken: host.cancellation.Token);
        host.Client = await McpClient.CreateAsync(new StreamClientTransport(host.requests.Writer.AsStream(), host.answers.Reader.AsStream()), cancellationToken: TestContext.Current.CancellationToken);
        return host;
    }

    /// <summary>
    /// Calls a tool.
    /// </summary>
    /// <param name="tool">The tool's name.</param>
    /// <param name="arguments">The arguments, or <see langword="null"/> for none.</param>
    /// <returns>The result.</returns>
    public async Task<CallToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        return await this.Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Calls a tool and reads its result's text.
    /// </summary>
    /// <param name="tool">The tool's name.</param>
    /// <param name="arguments">The arguments, or <see langword="null"/> for none.</param>
    /// <returns>The lines of the result's text, and whether it is an error.</returns>
    public async Task<ToolAnswer> AskAsync(string tool, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await this.CallAsync(tool, arguments);
        return new ToolAnswer(result.IsError == true, [.. result.Content.OfType<TextContentBlock>().SelectMany(block => block.Text.Split('\n')).Select(line => line.TrimEnd('\r'))]);
    }

    /// <summary>
    /// Closes the connection, as a host does to shut the server down, and waits for the server to exit.
    /// </summary>
    /// <returns>The server's exit code.</returns>
    public async Task<int> CloseAsync()
    {
        await this.Client.DisposeAsync();
        await this.requests.Writer.CompleteAsync();
        return await this.Serving.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Cancels the server, as Ctrl+C does, and waits for it to exit.
    /// </summary>
    /// <returns>The server's exit code.</returns>
    public async Task<int> CancelAsync()
    {
        await this.cancellation.CancelAsync();
        return await this.Serving.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!this.Serving.IsCompleted)
        {
            await this.CloseAsync();
        }

        this.cancellation.Dispose();
    }
}
