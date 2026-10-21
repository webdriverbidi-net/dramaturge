// <copyright file="InProcessSessions.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.TestUtilities;

using System.Net.Sockets;
using System.Text;
using Dramaturge.Browsers;
using Dramaturge.TestUtilities;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// Sessions whose daemons serve in the test's own process, each connected to a fake browser of the session's name
/// in place of the one it would launch, on a fake clock. The sessions' directory is short, since a socket's path
/// may be at most 104 bytes on macOS.
/// </summary>
public sealed class InProcessSessions : IAsyncDisposable
{
    /// <summary>
    /// The name of a session whose browser cannot be reached.
    /// </summary>
    public const string Broken = "broken";

    private readonly List<Task<int>> daemons = [];

    private InProcessSessions(FakeBrowserServer server)
    {
        this.Server = server;
    }

    /// <summary>
    /// Gets the fake browsers' server.
    /// </summary>
    public FakeBrowserServer Server { get; }

    /// <summary>
    /// Gets the sessions' directory.
    /// </summary>
    public string Directory { get; } = System.IO.Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dts-{Guid.NewGuid():N}"[..12])).FullName;

    /// <summary>
    /// Gets the clock of the sessions' idle timeouts.
    /// </summary>
    public FakeTimeProvider Time { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether a browser has a tab when its session connects.
    /// </summary>
    public bool BrowsersHaveAPage { get; set; } = true;

    /// <summary>
    /// Gets or sets what else is done to a browser as its session connects, after it is given its tab.
    /// </summary>
    public Action<FakeSession>? OnConnect { get; set; }

    /// <summary>
    /// Starts the fake browsers' server.
    /// </summary>
    /// <returns>The sessions.</returns>
    public static async Task<InProcessSessions> StartAsync()
    {
        InProcessSessions sessions = new(await FakeBrowserServer.StartAsync());
        sessions.Server.OnConnect = session =>
        {
            if (sessions.BrowsersHaveAPage)
            {
                session.AddContext();
            }

            sessions.OnConnect?.Invoke(session);
        };
        return sessions;
    }

    /// <summary>
    /// Sends a line to a socket and reads the line that answers it.
    /// </summary>
    /// <param name="socketPath">The socket.</param>
    /// <param name="request">The line, or <see langword="null"/> to connect and send nothing.</param>
    /// <returns>The answer, or an empty string when nothing was sent.</returns>
    public static async Task<string> ExchangeAsync(string socketPath, string? request)
    {
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), TestContext.Current.CancellationToken);
        if (request is null)
        {
            return string.Empty;
        }

        await socket.SendAsync(Encoding.UTF8.GetBytes(request), TestContext.Current.CancellationToken);
        using StreamReader reader = new(new NetworkStream(socket, ownsSocket: false));
        return await reader.ReadLineAsync(TestContext.Current.CancellationToken) ?? string.Empty;
    }

    /// <summary>
    /// Gets the daemons started so far, as tasks that end when each daemon does.
    /// </summary>
    /// <returns>The daemons.</returns>
    public List<Task<int>> Daemons()
    {
        lock (this.daemons)
        {
            return [.. this.daemons];
        }
    }

    /// <summary>
    /// Runs the tool, with the sessions' host.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>What the tool wrote, and its exit code.</returns>
    public async Task<ToolResult> RunAsync(params string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await DramaturgeTool.RunAsync(args, output, error, sessionHost: this.Host(), cancellationToken: TestContext.Current.CancellationToken);
        return new ToolResult(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Stops the sessions still running, and the server.
    /// </summary>
    /// <returns>A task that completes when everything has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (string socketPath in System.IO.Directory.GetFiles(this.Directory, "*.sock"))
        {
            await this.RunAsync("session", "stop", "--session", Path.GetFileNameWithoutExtension(socketPath));
        }

        await Task.WhenAll(this.Daemons()).WaitAsync(TimeSpan.FromSeconds(10));
        await this.Server.DisposeAsync();
        System.IO.Directory.Delete(this.Directory, true);
    }

    /// <summary>
    /// Gets the sessions' host, for a tool run that is not through <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="streams">The streams an MCP host is served on, or <see langword="null"/> for a run that serves none.</param>
    /// <returns>The host.</returns>
    public SessionHost Host(StandardStreams? streams = null) => new()
    {
        Directory = this.Directory,
        StartDaemon = this.StartDaemonAsync,
        TimeProvider = this.Time,
        Streams = streams,
    };

    // Serves the session in this process until it ends, answering as soon as it is ready or has failed.
    private async Task<string?> StartDaemonAsync(IReadOnlyList<string> serveArguments, TextWriter error, CancellationToken cancellationToken)
    {
        string name = serveArguments.SkipWhile(argument => argument != "--session").Skip(1).FirstOrDefault() ?? "default";
        Uri browser = name == Broken ? new Uri("ws://127.0.0.1:1/unreachable") : this.Server.UrlFor(name);
        ReadyWriter ready = new();
        Task<int> serving = DramaturgeTool.RunAsync([.. serveArguments], ready, error, configureLauncher: _ => BrowserLauncher.Configure(BrowserKind.Chrome).ConnectToExisting(browser), sessionHost: this.Host(), cancellationToken: CancellationToken.None);
        lock (this.daemons)
        {
            this.daemons.Add(serving);
        }

        return await ready.Said;
    }

    // Completes when the daemon says it is ready, with null, or that it failed, with why.
    private sealed class ReadyWriter : StringWriter
    {
        private readonly TaskCompletionSource<string?> said = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> Said => this.said.Task;

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value == "ready")
            {
                this.said.TrySetResult(null);
            }
            else if (value?.StartsWith("failed ", StringComparison.Ordinal) == true)
            {
                this.said.TrySetResult(value.Substring("failed ".Length));
            }
        }
    }
}
