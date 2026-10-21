// <copyright file="McpTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dramaturge.TestUtilities;
using Dramaturge.Tool.TestUtilities;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// The mcp command serves the session commands as tools on a pair of streams; a call reaches a session's daemon as
// the command line's request would, and its result reads as the command line's answer.
public sealed class McpTests : IAsyncLifetime
{
    private const string Start = "--- page content (untrusted) ---";
    private const string End = "--- end of page content ---";

    private static readonly string[] ToolNames = ["session_start", "session_list", "session_stop", "open", "back", "forward", "reload", "url", "snapshot", "tabs", "tab", "screenshot", "click", "fill", "press", "select", "check", "uncheck", "hover", "upload", "expect", "code", "console"];

    private readonly FakePage page = new();
    private InProcessSessions sessions = null!;

    public async ValueTask InitializeAsync()
    {
        this.sessions = await InProcessSessions.StartAsync();
        this.sessions.OnConnect = this.page.Attach;
    }

    public ValueTask DisposeAsync() => this.sessions.DisposeAsync();

    [Fact]
    public async Task ToolsAreTheSessionCommands()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);

        IList<McpClientTool> tools = await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        McpClientTool Tool(string name) => tools.Single(tool => tool.Name == name);

        Assert.Equal(ToolNames, tools.Select(tool => tool.Name));
        Assert.All(tools.Where(tool => tool.Name != "session_list"), tool => Assert.Equal("string", tool.JsonSchema.GetProperty("properties").GetProperty("session").GetProperty("type").GetString()));
        Assert.Equal(
            Canonical("{\"type\":\"object\",\"properties\":{\"ref\":{\"type\":\"string\",\"description\":\"The element's ref, such as e7, from the current tab's latest snapshot.\"},\"double\":{\"type\":\"boolean\",\"description\":\"Click twice, as a double click.\"},\"button\":{\"type\":\"string\",\"description\":\"The mouse button: left (the default), right, or middle.\",\"enum\":[\"left\",\"right\",\"middle\"],\"default\":\"left\"},\"snapshot\":{\"type\":\"boolean\",\"description\":\"Show the page's snapshot, or its changes, after the command; false leaves it out.\",\"default\":true},\"session\":{\"type\":\"string\",\"description\":\"The session's name; 'default' when not given.\"}},\"required\":[\"ref\"],\"additionalProperties\":false}"),
            Canonical(Tool("click").JsonSchema.GetRawText()));
        Assert.Equal(Canonical("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"), Canonical(Tool("session_list").JsonSchema.GetRawText()));
        JsonElement expect = Tool("expect").JsonSchema;
        Assert.Equal("array", expect.GetProperty("properties").GetProperty("condition").GetProperty("type").GetString());
        Assert.Equal("string", expect.GetProperty("properties").GetProperty("condition").GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("integer", expect.GetProperty("properties").GetProperty("timeout").GetProperty("type").GetString());
        Assert.Equal(["condition"], expect.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.False(Tool("screenshot").JsonSchema.TryGetProperty("required", out _));
        Assert.Equal(["program", "xunit", "nunit", "mstest", "tunit"], Tool("code").JsonSchema.GetProperty("properties").GetProperty("target").GetProperty("enum").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal(["chrome", "firefox", "edge", "safari"], Tool("session_start").JsonSchema.GetProperty("properties").GetProperty("browser").GetProperty("enum").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal("array", Tool("upload").JsonSchema.GetProperty("properties").GetProperty("files").GetProperty("type").GetString());
        Assert.True(Tool("url").ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(Tool("click").ProtocolTool.Annotations!.ReadOnlyHint);
    }

    [Fact]
    public async Task ServerDescribesItselfAndItsUse()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);

        string instructions = host.Client.ServerInstructions!;

        Assert.Equal("dramaturge", host.Client.ServerInfo!.Name);
        Assert.Equal(typeof(DramaturgeTool).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0], host.Client.ServerInfo.Version);
        Assert.NotNull(host.Client.ServerCapabilities!.Tools);
        Assert.InRange(instructions.Length, 1, 4096);
        Assert.All((string[])["session_start", "snapshot", "expect", "code", "session_stop", Start, End], word => Assert.Contains(word, instructions));
        Assert.Empty(host.Output);
    }

    [Fact]
    public async Task SessionToolsStartListAndStop()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);

        ToolAnswer started = await host.AskAsync("session_start");
        ToolAnswer again = await host.AskAsync("session_start", new Dictionary<string, object?>() { ["session"] = "default" });
        ToolAnswer firefox = await host.AskAsync("session_start", new Dictionary<string, object?>() { ["session"] = "fx", ["browser"] = "firefox", ["channel"] = "nightly" });
        ToolAnswer listed = await host.AskAsync("session_list");
        ToolAnswer stopped = await host.AskAsync("session_stop");
        ToolAnswer stoppedFirefox = await host.AskAsync("session_stop", new Dictionary<string, object?>() { ["session"] = "fx" });
        ToolAnswer none = await host.AskAsync("session_list");

        Assert.False(started.IsError);
        Assert.Equal(["Started session 'default' (chrome)."], started.Lines);
        Assert.True(again.IsError);
        Assert.Equal(["error[session-exists]: A session named 'default' is running. — Stop it with 'dramaturge session stop', or name another with --session."], again.Lines);
        Assert.Equal(["Started session 'fx' (firefox)."], firefox.Lines);
        Assert.Equal(2, listed.Lines.Count);
        Assert.StartsWith("default\tchrome\tstarted ", listed.Lines[0]);
        Assert.StartsWith("fx\tfirefox\tstarted ", listed.Lines[1]);
        Assert.Equal(["Stopped session 'default'."], stopped.Lines);
        Assert.Equal(["Stopped session 'fx'."], stoppedFirefox.Lines);
        Assert.Equal(["No sessions are running."], none.Lines);
    }

    [Fact]
    public async Task SessionStartRejectsABrowserOrNameItCannotUse()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);

        ToolAnswer browser = await host.AskAsync("session_start", new Dictionary<string, object?>() { ["browser"] = "netscape" });
        ToolAnswer name = await host.AskAsync("session_start", new Dictionary<string, object?>() { ["session"] = "no spaces" });
        ToolAnswer command = await host.AskAsync("url", new Dictionary<string, object?>() { ["session"] = "no spaces" });

        Assert.True(browser.IsError);
        Assert.Equal(["error[usage]: 'netscape' is not a browser: chrome, firefox, or edge."], browser.Lines);
        Assert.True(name.IsError);
        Assert.StartsWith("error[usage]: ", name.Lines[0]);
        Assert.Equal(name.Lines, command.Lines);
    }

    [Fact]
    public async Task PageToolsAnswerAsTheCommandLineDoes()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);
        await host.AskAsync("session_start");

        ToolAnswer open = await host.AskAsync("open", new Dictionary<string, object?>() { ["url"] = "https://example.com/" });
        this.page.Snapshot += "\n- listitem [ref=e3]: Added";
        ToolAnswer click = await host.AskAsync("click", new Dictionary<string, object?>() { ["ref"] = "e2" });
        ToolAnswer missing = await host.AskAsync("click", new Dictionary<string, object?>() { ["ref"] = "e9", ["snapshot"] = false });
        ToolAnswer noSession = await host.AskAsync("url", new Dictionary<string, object?>() { ["session"] = "other" });
        ToolAnswer url = await host.AskAsync("url");
        ToolAnswer code = await host.AskAsync("code", new Dictionary<string, object?>() { ["target"] = "program" });

        Assert.False(open.IsError);
        Assert.Equal(["Opened https://example.com/", Start, "Title: Fake title", "- heading \"Welcome\" [level=1] [ref=e1]", "- button \"Add\" [ref=e2]", End], open.Lines);
        Assert.Equal("Clicked e2", click.Lines[0]);
        Assert.Contains("+ - listitem [ref=e3]: Added", click.Lines);
        Assert.True(missing.IsError);
        Assert.StartsWith("error[unknown-ref]: ", missing.Lines[0]);
        Assert.True(noSession.IsError);
        Assert.StartsWith("error[no-session]: ", noSession.Lines[0]);
        Assert.Equal(["https://example.com/", Start, "Title: Fake title", End], url.Lines);
        Assert.Contains("await page.NavigateAsync(\"https://example.com/\");", code.Lines);
    }

    // What a failed expectation found is page content, between the content lines, as on the command line.
    [Fact]
    public async Task ExpectationNotMetShowsWhatThePageHad()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);
        await host.AskAsync("session_start");
        await host.AskAsync("snapshot");

        ToolAnswer failed = await host.AskAsync("expect", new Dictionary<string, object?>() { ["condition"] = new[] { "e1", "text", "Nope" }, ["timeout"] = 1 });

        Assert.True(failed.IsError);
        Assert.Equal(["error[expectation-failed]: Not met within 1 second: e1 contains \"Nope\".", Start, "Expected ref e1 to contain text \"Nope\"; received \"Welcome\" after 1 seconds.", End], failed.Lines);
    }

    [Fact]
    public async Task ScreenshotIsTheImageOrAFile()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);
        await host.AskAsync("session_start");
        string path = Path.Combine(this.sessions.Directory, "shot.png");

        CallToolResult inline = await host.CallAsync("screenshot");
        ToolAnswer saved = await host.AskAsync("screenshot", new Dictionary<string, object?>() { ["path"] = path });

        Assert.NotEqual(true, inline.IsError);
        Assert.Equal([$"A screenshot of {FakeBrowserServer.Screenshot.Length} bytes, as PNG."], inline.Content.OfType<TextContentBlock>().Select(block => block.Text));
        ImageContentBlock image = Assert.Single(inline.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(FakeBrowserServer.Screenshot, image.DecodedData.ToArray());
        Assert.Equal([$"Saved a screenshot of {FakeBrowserServer.Screenshot.Length} bytes to {path}"], saved.Lines);
        Assert.Equal(FakeBrowserServer.Screenshot, File.ReadAllBytes(path));
    }

    // The daemon reads and writes files as named, so a relative path is made absolute against the server's directory.
    [Fact]
    public async Task PathsAreMadeAbsolute()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);
        await host.AskAsync("session_start");
        await host.AskAsync("snapshot");
        string codePath = Path.Combine(this.sessions.Directory, "Test.cs");
        string filePath = Path.Combine(this.sessions.Directory, "none.txt");

        ToolAnswer code = await host.AskAsync("code", new Dictionary<string, object?>() { ["path"] = Path.GetRelativePath(Environment.CurrentDirectory, codePath) });
        ToolAnswer upload = await host.AskAsync("upload", new Dictionary<string, object?>() { ["ref"] = "e2", ["files"] = new[] { Path.GetRelativePath(Environment.CurrentDirectory, filePath) } });

        Assert.Equal([$"Wrote the code for program to {codePath}."], code.Lines);
        Assert.True(File.Exists(codePath));
        Assert.True(upload.IsError);
        Assert.Equal([$"error[no-file]: There is no file {filePath}."], upload.Lines);
    }

    [Theory]
    [InlineData("tab", "{\"index\":\"two\"}", "The argument 'index' of 'tab' must be a whole number.")]
    [InlineData("tab", "{\"index\":2.5}", "The argument 'index' of 'tab' must be a whole number.")]
    [InlineData("click", "{\"ref\":\"e2\",\"double\":\"yes\"}", "The argument 'double' of 'click' must be true or false.")]
    [InlineData("expect", "{\"condition\":\"e1 visible\"}", "The argument 'condition' of 'expect' must be a list of strings.")]
    [InlineData("expect", "{\"condition\":[\"e1\",1]}", "The argument 'condition' of 'expect' must be a list of strings.")]
    [InlineData("open", "{\"url\":5}", "The argument 'url' of 'open' must be a string.")]
    [InlineData("url", "{\"session\":5}", "The argument 'session' of 'url' must be a string.")]
    public async Task ArgumentOfTheWrongKindIsAUsageError(string tool, string arguments, string message)
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);
        await host.AskAsync("session_start");

        ToolAnswer rejected = await host.AskAsync(tool, JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments));

        Assert.True(rejected.IsError);
        Assert.Equal([$"error[usage]: {message}"], rejected.Lines);
    }

    [Fact]
    public async Task ToolThatDoesNotExistIsAUsageError()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);

        ToolAnswer rejected = await host.AskAsync("dance");

        Assert.True(rejected.IsError);
        Assert.Equal(["error[usage]: There is no tool named 'dance'."], rejected.Lines);
    }

    // The server's options fix what every session it starts allows; a session started elsewhere keeps its own.
    [Fact]
    public async Task ServerOptionsApplyToEverySessionItStarts()
    {
        string profile = Directory.CreateDirectory(Path.Combine(this.sessions.Directory, "profile")).FullName;
        string files = Directory.CreateDirectory(Path.Combine(this.sessions.Directory, "files")).FullName;
        await using McpHost strict = await McpHost.StartAsync(this.sessions, "--allow-origin", "https://allowed.test");
        await using McpHost lenient = await McpHost.StartAsync(this.sessions, "--allow-file-urls", "--idle-timeout", "5", "--headed", "--user-data-dir", profile, "--mask-passwords", "--dialogs", "accept", "--downloads", files, "--allow-upload-from", files, "--path", files);
        await strict.AskAsync("session_start", new Dictionary<string, object?>() { ["session"] = "strict" });
        await lenient.AskAsync("session_start", new Dictionary<string, object?>() { ["session"] = "lenient" });

        ToolAnswer refused = await strict.AskAsync("open", new Dictionary<string, object?>() { ["url"] = "file:///etc/hosts", ["session"] = "strict", ["snapshot"] = false });
        ToolAnswer otherOrigin = await strict.AskAsync("open", new Dictionary<string, object?>() { ["url"] = "https://other.test/", ["session"] = "strict", ["snapshot"] = false });
        ToolAnswer allowedFile = await lenient.AskAsync("open", new Dictionary<string, object?>() { ["url"] = "file:///etc/hosts", ["session"] = "lenient", ["snapshot"] = false });
        this.sessions.Time.Advance(TimeSpan.FromMinutes(5));
        int idle = await this.sessions.Daemons()[1].WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        ToolAnswer listed = await strict.AskAsync("session_list");

        Assert.True(refused.IsError);
        Assert.StartsWith("error[file-url-not-allowed]: ", refused.Lines[0]);
        Assert.False(allowedFile.IsError);
        Assert.True(otherOrigin.IsError);
        Assert.StartsWith("error[origin-not-allowed]: ", otherOrigin.Lines[0]);
        Assert.Equal(0, idle);
        Assert.StartsWith("strict\t", Assert.Single(listed.Lines));
    }

    [Fact]
    public async Task ServerOptionsThatCannotBeUsedStopItBeforeItServes()
    {
        ToolResult result = await this.sessions.RunAsync("mcp", "--allow-origin", "nope");
        ToolResult help = await this.sessions.RunAsync("mcp", "--help");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("error[usage]: 'nope' is not an origin, such as https://example.com.", result.Error.Trim());
        Assert.Empty(result.Output);
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Serve the session commands to an MCP host", help.Output);
        Assert.Contains("--allow-origin", help.Output);
    }

    // A host shuts the server down by closing its input; the sessions the host started go with it, and no others.
    [Fact]
    public async Task ClosingTheConnectionStopsTheSessionsTheHostStarted()
    {
        await this.sessions.RunAsync("session", "start", "--session", "cli");
        McpHost host = await McpHost.StartAsync(this.sessions);
        await host.AskAsync("session_start");
        await host.AskAsync("session_start", new Dictionary<string, object?>() { ["session"] = "other" });
        await host.AskAsync("session_stop", new Dictionary<string, object?>() { ["session"] = "other" });
        await host.AskAsync("session_start", new Dictionary<string, object?>() { ["session"] = "other" });

        int exitCode = await host.CloseAsync();
        ToolResult list = await this.sessions.RunAsync("session", "list");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("cli\t", Assert.Single(list.OutputLines));
        Assert.Empty(host.Output);
        await host.DisposeAsync();
    }

    [Fact]
    public async Task CancellingTheServerStopsTheSessionsItStartedToo()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);
        await host.AskAsync("session_start");

        int exitCode = await host.CancelAsync();
        ToolResult list = await this.sessions.RunAsync("session", "list");

        Assert.Equal(0, exitCode);
        Assert.Equal(["No sessions are running."], list.OutputLines);
    }

    // The skill and the article are written by hand; each command the catalogue offers must be in both.
    [Fact]
    public async Task SkillAndArticleNameEveryCommand()
    {
        await using McpHost host = await McpHost.StartAsync(this.sessions);
        string skill = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "prose", "SKILL.md"), TestContext.Current.CancellationToken);
        string article = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "prose", "command-line-tool.md"), TestContext.Current.CancellationToken);

        IList<McpClientTool> tools = await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (string name in tools.Select(tool => tool.Name).Where(name => !name.StartsWith("session_", StringComparison.Ordinal)))
        {
            Assert.Matches($"`{Regex.Escape(name)}[ `]", skill);
            Assert.Matches($"`{Regex.Escape(name)}[ `]", article);
        }
    }

    // The same JSON written one way, whatever a writer escaped.
    private static string Canonical(string json) => JsonSerializer.Serialize(JsonNode.Parse(json));
}
