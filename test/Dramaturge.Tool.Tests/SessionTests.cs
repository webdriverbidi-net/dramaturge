// <copyright file="SessionTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Dramaturge.Browsers;
using Dramaturge.TestUtilities;
using Dramaturge.Tool.TestUtilities;
using Microsoft.Extensions.Time.Testing;

public sealed class SessionTests : IAsyncLifetime
{
    private InProcessSessions sessions = null!;

    public async ValueTask InitializeAsync()
    {
        this.sessions = await InProcessSessions.StartAsync();
    }

    public ValueTask DisposeAsync() => this.sessions.DisposeAsync();

    [Fact]
    public async Task SessionStartsAndIsListedUntilItStops()
    {
        ToolResult start = await this.sessions.RunAsync("session", "start", "--mask-passwords");
        ToolResult list = await this.sessions.RunAsync("session", "list");
        ToolResult stop = await this.sessions.RunAsync("session", "stop");
        int daemonExitCode = await this.sessions.Daemons().Single().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        ToolResult after = await this.sessions.RunAsync("session", "list");

        Assert.Equal(0, start.ExitCode);
        Assert.Equal(["Started session 'default' (chrome)."], start.OutputLines);
        Assert.Equal([$"default\tchrome\tstarted {this.sessions.Time.GetUtcNow():O}"], list.OutputLines);
        Assert.Equal(["Stopped session 'default'."], stop.OutputLines);
        Assert.Equal(0, daemonExitCode);
        Assert.Equal(["No sessions are running."], after.OutputLines);
        Assert.Empty(Directory.GetFiles(this.sessions.Directory));

        // The session records its page from the start, with no toolbar to cover the page.
        JsonObject recorderStart = this.sessions.Server.SessionFor("default").RemoteEnd.CommandsFor("script.callFunction").Select(command => command["params"]!.AsObject()).Single(parameters => ((string)parameters["functionDeclaration"]!).Contains("recorder.start"));
        Assert.False((bool)recorderStart["arguments"]![3]!["value"]!);
    }

    [Fact]
    public async Task EachNameRunsOneSession()
    {
        await this.sessions.RunAsync("session", "start");
        ToolResult second = await this.sessions.RunAsync("session", "start", "--session", "second", "--browser", "firefox", "--channel", "beta", "--headed", "--user-data-dir", this.sessions.Directory, "--path", this.sessions.Directory);
        ToolResult again = await this.sessions.RunAsync("session", "start");
        ToolResult list = await this.sessions.RunAsync("session", "list");

        Assert.Equal(["Started session 'second' (firefox)."], second.OutputLines);
        Assert.Equal(1, again.ExitCode);
        Assert.Equal("error[session-exists]: A session named 'default' is running. — Stop it with 'dramaturge session stop', or name another with --session.", again.Error.Trim());
        Assert.Equal(["default", "second"], list.OutputLines.Select(line => line.Split('\t')[0]));
        Assert.Equal(2, this.sessions.Daemons().Count);
    }

    // Each result, or error, is one line of JSON on the output, and the exit code is as for text.
    [Fact]
    public async Task JsonWritesTheResultOrTheErrorAsOneLine()
    {
        ToolResult start = await this.sessions.RunAsync("session", "start", "--json");
        ToolResult list = await this.sessions.RunAsync("session", "list", "--json");
        ToolResult again = await this.sessions.RunAsync("session", "start", "--json");
        ToolResult stop = await this.sessions.RunAsync("session", "stop", "--json");
        ToolResult none = await this.sessions.RunAsync("session", "stop", "--json");
        ToolResult usage = await this.sessions.RunAsync("session", "start", "--idle-timeout", "soon", "--json");

        Assert.Equal("{\"ok\":true,\"result\":{\"name\":\"default\",\"browser\":\"chrome\"}}", start.Output.Trim());
        Assert.Equal($"{{\"ok\":true,\"result\":{{\"sessions\":[{{\"name\":\"default\",\"browser\":\"chrome\",\"started\":\"{this.sessions.Time.GetUtcNow():O}\"}}]}}}}", list.Output.Trim());
        Assert.Equal(1, again.ExitCode);
        Assert.Equal("{\"ok\":false,\"error\":{\"code\":\"session-exists\",\"message\":\"A session named 'default' is running.\",\"hint\":\"Stop it with 'dramaturge session stop', or name another with --session.\"}}", again.Output.Trim());
        Assert.Equal("{\"ok\":true,\"result\":{\"name\":\"default\"}}", stop.Output.Trim());
        Assert.Equal(3, none.ExitCode);
        Assert.Contains("\"code\":\"no-session\"", none.Output);
        Assert.Equal(2, usage.ExitCode);
        Assert.Equal("{\"ok\":false,\"error\":{\"code\":\"usage\",\"message\":\"Cannot parse argument 'soon' for option '--idle-timeout' as expected type 'System.Int32'.\",\"hint\":\"Run 'dramaturge session start --help' for usage.\"}}", usage.Output.Trim());
        Assert.All([start, list, again, stop, none, usage], result => Assert.Empty(result.Error));
    }

    [Fact]
    public async Task StoppingASessionThatIsNotRunningIsExitCodeThree()
    {
        ToolResult stop = await this.sessions.RunAsync("session", "stop");

        Assert.Equal(3, stop.ExitCode);
        Assert.Equal("error[no-session]: No session named 'default' is running. — Start one with 'dramaturge session start'.", stop.Error.Trim());
    }

    // A session that did not stop cleanly, as one killed, leaves a socket that nothing answers.
    [Fact]
    public async Task SocketThatNothingAnswersIsRemoved()
    {
        string ghost = Path.Combine(this.sessions.Directory, "ghost.sock");
        File.WriteAllText(ghost, string.Empty);
        ToolResult list = await this.sessions.RunAsync("session", "list");
        bool listRemovedIt = !File.Exists(ghost);
        File.WriteAllText(ghost, string.Empty);
        ToolResult start = await this.sessions.RunAsync("session", "start", "--session", "ghost");

        Assert.Equal(["No sessions are running."], list.OutputLines);
        Assert.True(listRemovedIt);
        Assert.Equal(0, start.ExitCode);
    }

    [Theory]
    [InlineData("start", "--session", "not a name", "'not a name' is not a session name")]
    [InlineData("start", "--session", "a-name-that-is-longer-than-32-chars", "up to 32 characters")]
    [InlineData("start", "--idle-timeout", "0", "--idle-timeout must be at least 1 minute.")]
    [InlineData("start", "--browser", "firefox", "'canary' is not a channel; firefox channels are")]
    [InlineData("stop", "--session", "-dash", "'-dash' is not a session name")]
    public async Task SettingThatCannotBeUsedIsExitCodeTwo(string command, string option, string value, string expectedError)
    {
        string[] args = command == "start" && option == "--browser" ? ["session", "start", "--browser", value, "--channel", "canary"] : ["session", command, option, value];

        ToolResult result = await this.sessions.RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.StartsWith("error[usage]: ", result.Error);
        Assert.Contains(expectedError, result.Error);
        Assert.Empty(this.sessions.Daemons());
    }

    [Fact]
    public async Task HelpListsTheSessionCommandsButNotServe()
    {
        ToolResult help = await this.sessions.RunAsync("session", "--help");

        Assert.Equal(0, help.ExitCode);
        Assert.All(["start", "list", "stop"], command => Assert.Contains(command, help.Output));
        Assert.DoesNotContain("serve", help.Output);
    }

    // Each command starts the idle timeout again, from the moment its answer is written.
    [Fact]
    public async Task SessionIdleForItsTimeoutStops()
    {
        await this.sessions.RunAsync("session", "start", "--idle-timeout", "1");

        this.sessions.Time.Advance(TimeSpan.FromSeconds(59));
        ToolResult first = await this.sessions.RunAsync("session", "list");
        this.sessions.Time.Advance(TimeSpan.FromSeconds(59));
        ToolResult second = await this.sessions.RunAsync("session", "list");
        this.sessions.Time.Advance(TimeSpan.FromSeconds(60));
        int exitCode = await this.sessions.Daemons().Single().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Single(first.OutputLines);
        Assert.Single(second.OutputLines);
        Assert.Equal(0, exitCode);
        Assert.Empty(Directory.GetFiles(this.sessions.Directory));
    }

    [Fact]
    public async Task SessionStopsWhenItsBrowserGoesAway()
    {
        await this.sessions.RunAsync("session", "start");

        await this.sessions.Server.DisconnectAsync("default");
        int exitCode = await this.sessions.Daemons().Single().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Empty(Directory.GetFiles(this.sessions.Directory));
    }

    [Fact]
    public async Task SessionOpensAPageInABrowserWithNone()
    {
        this.sessions.BrowsersHaveAPage = false;

        ToolResult start = await this.sessions.RunAsync("session", "start");

        Assert.Equal(0, start.ExitCode);
        Assert.Single(this.sessions.Server.SessionFor("default").RemoteEnd.CommandsFor("browsingContext.create"));
    }

    [Fact]
    public async Task BrowserThatCannotStartFailsTheStart()
    {
        ToolResult start = await this.sessions.RunAsync("session", "start", "--session", InProcessSessions.Broken);

        Assert.Equal(1, start.ExitCode);
        Assert.StartsWith("error[start-failed]: The browser could not be started: ", start.Error);
        Assert.Empty(start.OutputLines);
        Assert.Empty(Directory.GetFiles(this.sessions.Directory));
    }

    // A connection that sends nothing usable is answered, or dropped, without ending the session.
    [Fact]
    public async Task RequestThatIsNotACommandIsAnsweredWithAnError()
    {
        await this.sessions.RunAsync("session", "start");
        string socketPath = Path.Combine(this.sessions.Directory, "default.sock");

        string notJson = await InProcessSessions.ExchangeAsync(socketPath, "not json\n");
        string notObject = await InProcessSessions.ExchangeAsync(socketPath, "[1]\n");
        string unknown = await InProcessSessions.ExchangeAsync(socketPath, "{\"command\":\"dance\"}\n");
        await InProcessSessions.ExchangeAsync(socketPath, null);
        ToolResult list = await this.sessions.RunAsync("session", "list");

        Assert.Contains("\"code\":\"bad-request\"", notJson);
        Assert.Contains("\"code\":\"bad-request\"", notObject);
        Assert.Contains("\"code\":\"unknown-command\"", unknown);
        Assert.Single(list.OutputLines);
    }

    [Theory]
    [InlineData(null, "error[no-answer]: Session 'other' closed the connection without answering. — It may have stopped; 'dramaturge session list' shows the sessions running.")]
    [InlineData("{\"ok\":false,\"error\":{\"code\":\"broken\",\"message\":\"It broke.\",\"hint\":null}}", "error[broken]: It broke.")]
    [InlineData("{\"ok\":false,\"error\":{\"code\":\"broken\",\"message\":\"It broke.\",\"hint\":\"Try again.\"}}", "error[broken]: It broke. — Try again.")]
    public async Task AnswerOtherThanAResultIsReported(string? answer, string expectedError)
    {
        string socketPath = Path.Combine(this.sessions.Directory, "other.sock");
        using Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen();
        Task answering = AnswerOnceAsync(listener, answer);

        ToolResult stop = await this.sessions.RunAsync("session", "stop", "--session", "other");
        await answering;

        Assert.Equal(1, stop.ExitCode);
        Assert.Equal(expectedError, stop.Error.Trim());
        Assert.False(File.Exists(socketPath));
    }

    private static async Task AnswerOnceAsync(Socket listener, string? answer)
    {
        using Socket client = await listener.AcceptAsync(TestContext.Current.CancellationToken);
        using NetworkStream stream = new(client, ownsSocket: false);
        using StreamReader reader = new(stream);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        if (answer is not null)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(answer + "\n"), TestContext.Current.CancellationToken);
        }
    }
}
