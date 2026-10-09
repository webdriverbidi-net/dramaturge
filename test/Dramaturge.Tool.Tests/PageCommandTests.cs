// <copyright file="PageCommandTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

using System.Text.Json.Nodes;
using Dramaturge.TestUtilities;
using Dramaturge.Tool.TestUtilities;

// Each command drives a session whose browser is fake: its pages have the title "Fake title" and the snapshot
// below. A command that follows events the browser raised runs after "url", whose answer comes after them.
public sealed class PageCommandTests : IAsyncLifetime
{
    private const string Snapshot = "- heading \"Welcome\" [level=1] [ref=e1]";
    private const string Start = "--- page content (untrusted) ---";
    private const string End = "--- end of page content ---";

    private InProcessSessions sessions = null!;

    public async ValueTask InitializeAsync()
    {
        this.sessions = await InProcessSessions.StartAsync();
        this.sessions.OnConnect = session => session.RemoteEnd.AnswerWith("script.callFunction", AnswerScript);
    }

    public ValueTask DisposeAsync() => this.sessions.DisposeAsync();

    [Fact]
    public async Task OpenNavigatesTheCurrentTabAndShowsItsSnapshot()
    {
        await this.sessions.RunAsync("session", "start");

        ToolResult open = await this.sessions.RunAsync("open", "https://example.com/");
        ToolResult bare = await this.sessions.RunAsync("open", "https://example.com/bare", "--no-snapshot");
        ToolResult json = await this.sessions.RunAsync("open", "https://example.com/json", "--json");

        Assert.Equal(0, open.ExitCode);
        Assert.Equal(["Opened https://example.com/", Start, "Title: Fake title", Snapshot, End], open.OutputLines);
        Assert.Equal(["Opened https://example.com/bare", Start, "Title: Fake title", End], bare.OutputLines);
        Assert.Equal("{\"ok\":true,\"result\":{\"url\":\"https://example.com/json\",\"title\":\"Fake title\",\"snapshot\":\"- heading \\\"Welcome\\\" [level=1] [ref=e1]\"}}", json.Output.Trim());
        Assert.Equal(["https://example.com/", "https://example.com/bare", "https://example.com/json"], this.Commands("browsingContext.navigate").Select(command => (string?)command["params"]!["url"]));
    }

    [Fact]
    public async Task HistoryCommandsActOnTheCurrentTab()
    {
        int[] traversals = [0];
        this.sessions.OnConnect = session =>
        {
            session.RemoteEnd.AnswerWith("script.callFunction", AnswerScript);
            session.RemoteEnd.AnswerWith("browsingContext.traverseHistory", parameters => new FakeResponse(new JsonObject(), [new FakeEvent("browsingContext.historyUpdated", new JsonObject() { ["context"] = (string)parameters["context"]!, ["timestamp"] = 1790000000000, ["url"] = $"https://example.com/{Interlocked.Increment(ref traversals[0])}", ["userContext"] = "default" })]));
        };
        await this.sessions.RunAsync("session", "start");

        ToolResult back = await this.sessions.RunAsync("back", "--no-snapshot");
        ToolResult forward = await this.sessions.RunAsync("forward");
        ToolResult reload = await this.sessions.RunAsync("reload", "--no-snapshot");
        this.Session().RemoteEnd.FailWith("browsingContext.traverseHistory", "no such history entry", "There is no entry to go back to.");
        ToolResult failed = await this.sessions.RunAsync("back");

        Assert.Equal(["Went back to https://example.com/1", Start, "Title: Fake title", End], back.OutputLines);
        Assert.Equal(["Went forward to https://example.com/2", Start, "Title: Fake title", Snapshot, End], forward.OutputLines);
        Assert.Equal(["Reloaded about:blank", Start, "Title: Fake title", End], reload.OutputLines);
        Assert.Equal(1, failed.ExitCode);
        Assert.StartsWith("error[browser-error]: ", failed.Error);
        Assert.Contains("There is no entry to go back to.", failed.Error);
    }

    [Fact]
    public async Task UrlAndSnapshotDescribeTheCurrentTab()
    {
        await this.sessions.RunAsync("session", "start");
        await this.sessions.RunAsync("open", "https://example.com/here", "--no-snapshot");

        ToolResult url = await this.sessions.RunAsync("url");
        ToolResult snapshot = await this.sessions.RunAsync("snapshot");

        Assert.Equal(["https://example.com/here", Start, "Title: Fake title", End], url.OutputLines);
        Assert.Equal(["https://example.com/here", Start, "Title: Fake title", Snapshot, End], snapshot.OutputLines);
    }

    // A tab that closes is no longer acted on: the last one left is, and with none left, open opens one.
    [Fact]
    public async Task TabsAreListedAndSwitchedBetween()
    {
        this.sessions.OnConnect = session =>
        {
            session.AddContext(url: "https://example.com/second");
            session.RemoteEnd.AnswerWith("script.callFunction", AnswerScript);
        };
        await this.sessions.RunAsync("session", "start");
        string[] tabIds = [.. this.Session().Contexts.Select(context => context.Id)];

        ToolResult tabs = await this.sessions.RunAsync("tabs");
        ToolResult tab = await this.sessions.RunAsync("tab", "2");
        ToolResult switched = await this.sessions.RunAsync("tabs", "--json");
        ToolResult missing = await this.sessions.RunAsync("tab", "3");
        await this.Session().RemoteEnd.RaiseEventAsync("browsingContext.contextDestroyed", Context(tabIds[1]));
        await this.sessions.RunAsync("tabs");
        ToolResult afterClose = await this.sessions.RunAsync("url");
        await this.Session().RemoteEnd.RaiseEventAsync("browsingContext.contextDestroyed", Context(tabIds[0]));
        await this.sessions.RunAsync("tabs");
        ToolResult none = await this.sessions.RunAsync("url");
        ToolResult noScreenshot = await this.sessions.RunAsync("screenshot", "-o", Path.Combine(this.sessions.Directory, "none.png"));
        ToolResult open = await this.sessions.RunAsync("open", "https://example.com/new", "--no-snapshot");

        Assert.Equal([Start, "* [1] about:blank — Fake title", "  [2] https://example.com/second — Fake title", End], tabs.OutputLines);
        Assert.Equal(["Switched to tab [2] https://example.com/second", Start, "Title: Fake title", End], tab.OutputLines);
        Assert.Contains("{\"index\":2,\"url\":\"https://example.com/second\",\"title\":\"Fake title\",\"current\":true}", switched.Output);
        Assert.Single(this.Commands("browsingContext.activate"));
        Assert.Equal(1, missing.ExitCode);
        Assert.Equal("error[no-tab]: There is no tab [3]. — 'dramaturge tabs' lists the tabs.", missing.Error.Trim());
        Assert.Equal("about:blank", afterClose.OutputLines[0]);
        Assert.Equal("error[no-tab]: The session has no tab open. — Open one with 'dramaturge open <url>'.", none.Error.Trim());
        Assert.Equal(1, noScreenshot.ExitCode);
        Assert.Equal(0, open.ExitCode);
        Assert.Single(this.Commands("browsingContext.create"));
    }

    [Fact]
    public async Task ScreenshotIsSavedToItsFile()
    {
        await this.sessions.RunAsync("session", "start");
        string path = Path.Combine(this.sessions.Directory, "shot.png");

        ToolResult screenshot = await this.sessions.RunAsync("screenshot", "-o", path);
        ToolResult fullPage = await this.sessions.RunAsync("screenshot", "-o", path, "--full-page");
        ToolResult unwritable = await this.sessions.RunAsync("screenshot", "-o", Path.Combine(this.sessions.Directory, "missing", "shot.png"));

        Assert.Equal([$"Saved a screenshot of {FakeBrowserServer.Screenshot.Length} bytes to {path}"], screenshot.OutputLines);
        Assert.Equal(FakeBrowserServer.Screenshot, File.ReadAllBytes(path));
        Assert.Equal(0, fullPage.ExitCode);
        Assert.Equal(["viewport", "document", "viewport"], this.Commands("browsingContext.captureScreenshot").Select(command => (string?)command["params"]!["origin"] ?? "viewport"));
        Assert.Equal(1, unwritable.ExitCode);
        Assert.StartsWith("error[write-failed]: The screenshot could not be written to ", unwritable.Error);
    }

    // The newest 500 messages are kept; those of a tab that has closed say so.
    [Fact]
    public async Task ConsoleShowsTheMessagesSinceItLastRan()
    {
        await this.sessions.RunAsync("session", "start");
        FakeSession session = this.Session();
        string page = session.Contexts[0].Id;

        await session.RemoteEnd.RaiseEventAsync("log.entryAdded", ConsoleEntry(page, "debug", "tracing"));
        await session.RemoteEnd.RaiseEventAsync("log.entryAdded", ConsoleEntry(page, "info", "hello"));
        await session.RemoteEnd.RaiseEventAsync("log.entryAdded", ConsoleEntry(page, "warn", "careful"));
        await session.RemoteEnd.RaiseEventAsync("log.entryAdded", ConsoleEntry(page, "error", "failed"));
        await session.RemoteEnd.RaiseEventAsync("log.entryAdded", Entry("javascript", "error", page, "Error: boom"));
        await this.sessions.RunAsync("url");
        ToolResult first = await this.sessions.RunAsync("console");
        ToolResult second = await this.sessions.RunAsync("console", "--json");
        for (int i = 0; i < 500; i++)
        {
            await session.RemoteEnd.RaiseEventAsync("log.entryAdded", ConsoleEntry(page, "info", $"message {i}"));
        }

        await session.RemoteEnd.RaiseEventAsync("browsingContext.contextDestroyed", Context(page));
        await this.sessions.RunAsync("tabs");
        ToolResult all = await this.sessions.RunAsync("console", "--all");

        Assert.Equal([Start, "[1] debug: tracing", "[1] info: hello", "[1] warning: careful", "[1] error: failed", "[1] error: Uncaught Error: boom", End], first.OutputLines);
        Assert.Equal("{\"ok\":true,\"result\":{\"messages\":[]}}", second.Output.Trim());
        Assert.Equal(502, all.OutputLines.Count);
        Assert.Equal("[closed] info: message 0", all.OutputLines[1]);
    }

    [Fact]
    public async Task OpenKeepsToTheOriginsAndSchemesTheSessionAllows()
    {
        await this.sessions.RunAsync("session", "start", "--allow-origin", "https://example.com", "--allow-origin", "HTTPS://Allowed.test:8443/");
        await this.sessions.RunAsync("session", "start", "--session", "files", "--allow-file-urls");

        ToolResult other = await this.sessions.RunAsync("open", "https://other.test/");
        ToolResult allowed = await this.sessions.RunAsync("open", "https://allowed.test:8443/page", "--no-snapshot");
        ToolResult blank = await this.sessions.RunAsync("open", "about:blank", "--no-snapshot");
        ToolResult file = await this.sessions.RunAsync("open", "file:///etc/hosts");
        ToolResult relative = await this.sessions.RunAsync("open", "/relative");
        ToolResult noScheme = await this.sessions.RunAsync("open", "example.com");
        ToolResult allowedFile = await this.sessions.RunAsync("open", "file:///etc/hosts", "--session", "files", "--no-snapshot");

        Assert.Equal(1, other.ExitCode);
        Assert.Equal("error[origin-not-allowed]: https://other.test is not an origin the session allows. — Start the session with --allow-origin for each origin it may open.", other.Error.Trim());
        Assert.Equal(0, allowed.ExitCode);
        Assert.Equal(0, blank.ExitCode);
        Assert.Equal("error[file-url-not-allowed]: The session does not open file: URLs. — Start the session with --allow-file-urls to open them.", file.Error.Trim());
        Assert.Equal(2, relative.ExitCode);
        Assert.Equal("error[usage]: '/relative' is not an absolute URL, such as https://example.com/.", relative.Error.Trim());
        Assert.Equal(2, noScheme.ExitCode);
        Assert.Equal(0, allowedFile.ExitCode);
        Assert.Equal("file:///etc/hosts", (string?)this.sessions.Server.SessionFor("files").RemoteEnd.CommandsFor("browsingContext.navigate").Single()["params"]!["url"]);
        Assert.Empty(this.sessions.Server.SessionFor("files").RemoteEnd.CommandsFor("network.addIntercept"));
    }

    // A page that navigates by itself, or loads a frame, from an origin the session does not allow is stopped.
    [Fact]
    public async Task PagesOwnNavigationToAnotherOriginIsStopped()
    {
        await this.sessions.RunAsync("session", "start", "--allow-origin", "https://example.com");
        FakeSession session = this.Session();
        string page = session.Contexts[0].Id;
        string intercept = (string)session.RemoteEnd.ResultsFor("network.addIntercept").Single()["intercept"]!;

        await session.RemoteEnd.RaiseEventAsync("network.beforeRequestSent", BlockedRequest(page, intercept, "away", "https://other.test/", "document"));
        await session.RemoteEnd.RaiseEventAsync("network.beforeRequestSent", BlockedRequest(page, intercept, "frame", "https://other.test/frame", "iframe"));
        await session.RemoteEnd.RaiseEventAsync("network.beforeRequestSent", BlockedRequest(page, intercept, "home", "https://example.com/next", "document"));
        await session.RemoteEnd.RaiseEventAsync("network.beforeRequestSent", BlockedRequest(page, intercept, "image", "https://other.test/logo.png", "image"));
        await session.RemoteEnd.RaiseEventAsync("network.beforeRequestSent", BlockedRequest(page, intercept, "odd", "odd", "document"));
        await this.sessions.RunAsync("url");

        Assert.Equal(["away", "frame", "odd"], session.RemoteEnd.CommandsFor("network.failRequest").Select(command => (string?)command["params"]!["request"]).Order());
        Assert.Equal(["home", "image"], session.RemoteEnd.CommandsFor("network.continueRequest").Select(command => (string?)command["params"]!["request"]).Order());
    }

    [Theory]
    [InlineData("{\"command\":\"open\",\"arguments\":{}}")]
    [InlineData("{\"command\":\"tab\",\"arguments\":{\"index\":\"two\"}}")]
    [InlineData("{\"command\":\"screenshot\",\"arguments\":[]}")]
    [InlineData("{\"command\":\"open\",\"arguments\":{\"url\":5}}")]
    [InlineData("{\"command\":\"tab\",\"arguments\":[]}")]
    [InlineData("{\"command\":\"tab\"}")]
    [InlineData("{\"command\":\"tab\",\"arguments\":{}}")]
    [InlineData("{\"command\":\"tab\",\"arguments\":{\"index\":2.5}}")]
    public async Task CommandWithArgumentsItCannotUseIsABadRequest(string request)
    {
        await this.sessions.RunAsync("session", "start");

        string answer = await InProcessSessions.ExchangeAsync(Path.Combine(this.sessions.Directory, "default.sock"), request + "\n");

        Assert.Contains("\"code\":\"bad-request\"", answer);
    }

    [Fact]
    public async Task CommandForASessionThatIsNotRunningOrNamedBadlyFails()
    {
        ToolResult none = await this.sessions.RunAsync("snapshot");
        ToolResult badName = await this.sessions.RunAsync("snapshot", "--session", "not a name");
        ToolResult badOrigin = await this.sessions.RunAsync("session", "start", "--allow-origin", "https://example.com/path");
        ToolResult noScheme = await this.sessions.RunAsync("session", "start", "--allow-origin", "example.com");
        ToolResult otherScheme = await this.sessions.RunAsync("session", "start", "--allow-origin", "ftp://example.com");
        ToolResult http = await this.sessions.RunAsync("session", "start", "--allow-origin", "http://example.com");
        ToolResult tabZero = await this.sessions.RunAsync("tab", "0");

        Assert.Equal(3, none.ExitCode);
        Assert.Equal(2, badName.ExitCode);
        Assert.Equal(2, badOrigin.ExitCode);
        Assert.Equal("error[usage]: 'https://example.com/path' is not an origin, such as https://example.com.", badOrigin.Error.Trim());
        Assert.Equal(2, noScheme.ExitCode);
        Assert.Equal(2, otherScheme.ExitCode);
        Assert.Equal(0, http.ExitCode);
        Assert.Equal("error[no-tab]: There is no tab [0]. — 'dramaturge tabs' lists the tabs.", tabZero.Error.Trim());
    }

    // A title that cannot be read, as of a document going away, is shown as none.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TitleThatCannotBeReadIsEmpty(bool throws)
    {
        JsonObject title = throws
            ? new JsonObject() { ["type"] = "exception", ["exceptionDetails"] = new JsonObject() { ["columnNumber"] = 0, ["exception"] = new JsonObject() { ["type"] = "error" }, ["lineNumber"] = 0, ["stackTrace"] = new JsonObject() { ["callFrames"] = new JsonArray() }, ["text"] = "Error: gone" }, ["realm"] = "realm-1" }
            : new JsonObject() { ["type"] = "success", ["result"] = new JsonObject() { ["type"] = "number", ["value"] = 5 }, ["realm"] = "realm-1" };
        this.sessions.OnConnect = session => session.RemoteEnd.AnswerWith("script.callFunction", parameters => ((string)parameters["functionDeclaration"]!).Contains("document.title") ? new FakeResponse(title) : AnswerScript(parameters));
        await this.sessions.RunAsync("session", "start");

        ToolResult url = await this.sessions.RunAsync("url");

        Assert.Equal(["about:blank", Start, "Title: ", End], url.OutputLines);
    }

    // The library's snapshot, the page's title and its load state, and nothing for any other script.
    private static FakeResponse AnswerScript(JsonObject parameters)
    {
        string function = (string)parameters["functionDeclaration"]!;
        JsonObject value = function.Contains("snapshots.snapshot") ? SnapshotValue()
            : function.Contains("document.title") ? new JsonObject() { ["type"] = "string", ["value"] = "Fake title" }
            : function.Contains("document.readyState") ? new JsonObject() { ["type"] = "string", ["value"] = "complete" }
            : new JsonObject() { ["type"] = "undefined" };
        return new FakeResponse(new JsonObject() { ["type"] = "success", ["result"] = value, ["realm"] = "realm-1" });
    }

    private static JsonObject SnapshotValue()
    {
        static JsonArray Property(string name, JsonNode value) => [name, value];
        static JsonObject Text(string value) => new() { ["type"] = "string", ["value"] = value };
        string tree = "{\"role\":\"fragment\",\"name\":\"\",\"children\":[{\"role\":\"heading\",\"name\":\"Welcome\",\"children\":[],\"level\":1,\"ref\":\"e1\"}]}";
        JsonObject element = new() { ["type"] = "node", ["sharedId"] = "heading-1", ["value"] = new JsonObject() { ["nodeType"] = 1, ["childNodeCount"] = 0 } };
        return new JsonObject()
        {
            ["type"] = "object",
            ["value"] = new JsonArray(
                Property("tree", Text(tree)),
                Property("text", Text(Snapshot)),
                Property("refs", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray(Text("e1")) }),
                Property("elements", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray(element) }),
                Property("frames", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray() })),
        };
    }

    private static JsonObject Context(string contextId)
    {
        return new JsonObject() { ["context"] = contextId, ["clientWindow"] = $"window-for-{contextId}", ["url"] = "about:blank", ["userContext"] = "default", ["children"] = null, ["originalOpener"] = null };
    }

    private static JsonObject Entry(string type, string level, string contextId, string text)
    {
        return new JsonObject() { ["type"] = type, ["level"] = level, ["source"] = new JsonObject() { ["realm"] = "realm-1", ["context"] = contextId }, ["text"] = text, ["timestamp"] = 1790000000000 };
    }

    private static JsonObject ConsoleEntry(string contextId, string level, string text)
    {
        JsonObject entry = Entry("console", level, contextId, text);
        entry["method"] = level == "warn" ? "warn" : "log";
        entry["args"] = new JsonArray(new JsonObject() { ["type"] = "string", ["value"] = text });
        return entry;
    }

    private static JsonObject BlockedRequest(string contextId, string intercept, string requestId, string url, string destination)
    {
        JsonObject request = new()
        {
            ["request"] = requestId,
            ["url"] = url,
            ["method"] = "get",
            ["headers"] = new JsonArray(),
            ["cookies"] = new JsonArray(),
            ["destination"] = destination,
            ["initiatorType"] = null,
            ["headersSize"] = 0,
            ["bodySize"] = 0,
            ["timings"] = new JsonObject()
            {
                ["timeOrigin"] = 0, ["requestTime"] = 0, ["redirectStart"] = 0, ["redirectEnd"] = 0, ["fetchStart"] = 0, ["dnsStart"] = 0, ["dnsEnd"] = 0,
                ["connectStart"] = 0, ["connectEnd"] = 0, ["tlsStart"] = 0, ["requestStart"] = 0, ["responseStart"] = 0, ["responseEnd"] = 0,
            },
        };
        return new JsonObject() { ["context"] = contextId, ["navigation"] = null, ["isBlocked"] = true, ["intercepts"] = new JsonArray(intercept), ["redirectCount"] = 0, ["timestamp"] = 1790000000000, ["request"] = request, ["initiator"] = new JsonObject() { ["type"] = "other" } };
    }

    private FakeSession Session() => this.sessions.Server.SessionFor("default");

    private IReadOnlyList<JsonObject> Commands(string method) => this.Session().RemoteEnd.CommandsFor(method);
}
