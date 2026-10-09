// <copyright file="ActionCommandTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dramaturge.TestUtilities;
using Dramaturge.Tool.TestUtilities;

// Each command drives a session whose browser is fake: its page's snapshot is whatever the test last set, each ref in
// it an element, and every element is ready for any action.
public sealed partial class ActionCommandTests : IAsyncLifetime
{
    private const string Start = "--- page content (untrusted) ---";
    private const string End = "--- end of page content ---";
    private const string Form = "- heading \"Form\" [level=1] [ref=e1]\n- textbox \"Name\" [ref=e2]\n- button \"Add\" [ref=e3]\n- list [ref=e4]:\n  - listitem [ref=e5]: First";

    private InProcessSessions sessions = null!;
    private string snapshot = Form;
    private string checkedState = "checked";
    private bool visible = true;

    public async ValueTask InitializeAsync()
    {
        this.sessions = await InProcessSessions.StartAsync();
        this.sessions.OnConnect = session =>
        {
            session.RemoteEnd.AnswerWith("script.callFunction", this.AnswerScript);
            session.RemoteEnd.AnswerWith("browsingContext.locateNodes", _ => new JsonObject() { ["nodes"] = new JsonArray(Element("body-1")) });
        };
    }

    public ValueTask DisposeAsync() => this.sessions.DisposeAsync();

    [Fact]
    public async Task ClickShowsTheChangesToTheSnapshot()
    {
        await this.StartWithSnapshotAsync();
        this.snapshot = Form + "\n  - listitem [ref=e6]: Added";

        ToolResult click = await this.sessions.RunAsync("click", "e3");

        Assert.Equal(0, click.ExitCode);
        Assert.Equal(["Clicked e3", Start, "Changes:", "  - list [ref=e4]:", "+   - listitem [ref=e6]: Added", End], click.OutputLines);
        Assert.Single(this.Commands("input.performActions"));
    }

    [Fact]
    public async Task ClickTakesItsButtonAndCount()
    {
        await this.StartWithSnapshotAsync();

        ToolResult right = await this.sessions.RunAsync("click", "e3", "--button", "right", "--no-snapshot");
        ToolResult middle = await this.sessions.RunAsync("click", "e3", "--button", "middle", "--no-snapshot");
        ToolResult twice = await this.sessions.RunAsync("click", "e3", "--double", "--json");

        Assert.Equal(["Clicked e3"], right.OutputLines);
        Assert.Equal(["Clicked e3"], middle.OutputLines);
        Assert.Equal("{\"ok\":true,\"result\":{\"ref\":\"e3\",\"double\":true,\"url\":\"about:blank\",\"navigated\":false,\"changes\":[],\"effects\":[]}}", twice.Output.Trim());
        Assert.Equal([2, 1, 0], this.Commands("input.performActions").Select(PressedButton));
        Assert.Equal(2, this.Commands("input.performActions")[2]["params"]!["actions"]![0]!["actions"]!.AsArray().Count(action => (string?)action!["type"] == "pointerDown"));
    }

    [Fact]
    public async Task EachActionSaysWhatItDid()
    {
        string file = Path.Combine(Environment.CurrentDirectory, $"upload-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "upload", TestContext.Current.CancellationToken);
        try
        {
            this.sessions.OnConnect = session =>
            {
                session.RemoteEnd.AnswerWith("script.callFunction", this.AnswerScript);
                session.RemoteEnd.AnswerWith("script.callFunction", parameters => ((string)parameters["functionDeclaration"]!).Contains("selectOptions") ? Success(new JsonObject() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("status", Text("selected")), new JsonArray("values", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray(Text("r"), Text("g")) })) }) : this.AnswerScript(parameters));
            };
            await this.StartWithSnapshotAsync();

            string[][] commands =
            [
                ["fill", "e2", "Ada"],
                ["press", "e2", "Enter"],
                ["press", "e2", "control+Shift+a"],
                ["press", "e2", "Control++"],
                ["press", "e2", "+"],
                ["select", "e2", "Red", "Green"],
                ["select", "e2", "r", "--by-value"],
                ["check", "e2"],
                ["uncheck", "e2"],
                ["hover", "e2"],
                ["upload", "e2", file],
            ];
            List<string> said = [];
            foreach (string[] command in commands)
            {
                // The box is already as each command would leave it.
                this.checkedState = command[0] == "uncheck" ? "unchecked" : "checked";
                ToolResult result = await this.sessions.RunAsync([.. command, "--no-snapshot"]);
                said.Add(result.OutputLines.Count > 0 ? result.OutputLines[0] : result.Error.Trim());
            }

            Assert.Equal(
                [
                    "Filled e2",
                    "Pressed Enter on e2",
                    "Pressed control+Shift+a on e2",
                    "Pressed Control++ on e2",
                    "Pressed + on e2",
                    "Selected 2 options in e2",
                    "Selected 2 options in e2",
                    "Checked e2",
                    "Unchecked e2",
                    "Hovered over e2",
                    "Uploaded 1 file to e2",
                ],
                said);
            Assert.Equal(file, (string?)this.Commands("input.setFiles").Single()["params"]!["files"]![0]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task SnapshotThatDidNotChangeOrChangedMoreThanItsLengthIsSaidSo()
    {
        await this.StartWithSnapshotAsync();

        ToolResult same = await this.sessions.RunAsync("click", "e3");
        this.snapshot = "- heading \"Done\" [level=1] [ref=e6]";
        ToolResult replaced = await this.sessions.RunAsync("click", "e3");
        this.snapshot = "- heading \"Done\" [level=1] [ref=e6]\n- list [ref=e7]:\n  - listitem [ref=e8]: One\n  - list [ref=e9]:\n    - listitem [ref=e10]: Two\n    - listitem [ref=e11]: Three";
        await this.sessions.RunAsync("click", "e6");
        this.snapshot = "- heading \"Done\" [level=1] [ref=e6]\n- list [ref=e7]:\n  - listitem [ref=e8]: One\n  - list [ref=e9]:\n    - listitem [ref=e10]: Changed\n    - listitem [ref=e11]: Three\n- button \"Last\" [ref=e12]";
        ToolResult nested = await this.sessions.RunAsync("click", "e6");

        this.snapshot = "- heading \"Done\" [level=1] [ref=e6]\n- button \"X\" [ref=e13]\n- button \"Y\" [ref=e14]\n- list [ref=e7]:\n  - listitem [ref=e8]: One\n  - list [ref=e9]:\n    - listitem [ref=e10]: Changed\n    - listitem [ref=e11]: Three\n- button \"Last\" [ref=e12]";
        ToolResult middle = await this.sessions.RunAsync("click", "e6");

        Assert.Equal(["Clicked e3", "The snapshot did not change."], same.OutputLines);
        Assert.Equal(["Clicked e6", Start, "Changes:", "+ - button \"X\" [ref=e13]", "+ - button \"Y\" [ref=e14]", End], middle.OutputLines);
        Assert.Equal(["Clicked e3", Start, "Snapshot:", "- heading \"Done\" [level=1] [ref=e6]", End], replaced.OutputLines);
        Assert.Equal(["Clicked e6", Start, "Changes:", "  - list [ref=e7]:", "    - list [ref=e9]:", "-     - listitem [ref=e10]: Two", "+     - listitem [ref=e10]: Changed", "+ - button \"Last\" [ref=e12]", End], nested.OutputLines);
    }

    [Fact]
    public async Task ChangesAreInThePagesOrder()
    {
        this.snapshot = "- heading \"List\" [level=1] [ref=e1]\n- button \"B\" [ref=e2]\n- button \"Z\" [ref=e3]\n- link \"a\" [ref=e4]\n- link \"b\" [ref=e5]\n- link \"c\" [ref=e6]";
        await this.StartWithSnapshotAsync();
        this.snapshot = "- heading \"List\" [level=1] [ref=e1]\n- button \"X\" [ref=e7]\n- button \"B\" [ref=e2]\n- button \"Y\" [ref=e8]\n- link \"a\" [ref=e4]\n- link \"b\" [ref=e5]\n- link \"c\" [ref=e6]";

        ToolResult click = await this.sessions.RunAsync("click", "e2");

        this.snapshot = "- heading \"List\" [level=1] [ref=e1]\n- button \"X\" [ref=e7]\n- button \"B\" [ref=e2]\n- link \"a\" [ref=e4]\n- link \"b\" [ref=e5]\n- link \"c\" [ref=e6]";
        ToolResult removal = await this.sessions.RunAsync("click", "e2");
        this.snapshot = "- heading \"List\" [level=1] [ref=e1]\n- button \"X\" [ref=e7]\n- button \"B\" [ref=e2]\n- link \"a\" [ref=e4]\n- link \"b\" [ref=e5]";
        ToolResult lastRemoved = await this.sessions.RunAsync("click", "e2");

        Assert.Equal(["Clicked e2", Start, "Changes:", "+ - button \"X\" [ref=e7]", "- - button \"Z\" [ref=e3]", "+ - button \"Y\" [ref=e8]", End], click.OutputLines);
        Assert.Equal(["Clicked e2", Start, "Changes:", "- - button \"Y\" [ref=e8]", End], removal.OutputLines);
        Assert.Equal(["Clicked e2", Start, "Changes:", "- - link \"c\" [ref=e6]", End], lastRemoved.OutputLines);
    }

    // A navigation shows the new page in full, and the refs of the document it replaced can no longer be used.
    [Fact]
    public async Task ActionThatNavigatesShowsTheNewPage()
    {
        string[] page = [string.Empty];
        this.sessions.OnConnect = session =>
        {
            page[0] = session.Contexts[0].Id;
            session.RemoteEnd.AnswerWith("script.callFunction", this.AnswerScript);
            session.RemoteEnd.AnswerWith("input.performActions", _ => new FakeResponse(new JsonObject(), [Navigation(page[0], "https://example.com/next")]));
        };
        await this.StartWithSnapshotAsync();
        this.snapshot = "- heading \"Next\" [level=1] [ref=e1]";

        ToolResult click = await this.sessions.RunAsync("click", "e3");
        ToolResult fromNewPage = await this.sessions.RunAsync("click", "e1", "--no-snapshot");
        ToolResult stale = await this.sessions.RunAsync("click", "e1");
        await this.sessions.RunAsync("snapshot");
        ToolResult json = await this.sessions.RunAsync("click", "e1", "--json");

        Assert.Equal(["Clicked e3, and the page navigated to https://example.com/next", Start, "Title: Fake title", "- heading \"Next\" [level=1] [ref=e1]", End], click.OutputLines);
        Assert.Equal(0, fromNewPage.ExitCode);
        Assert.Equal(1, stale.ExitCode);
        Assert.Equal("error[stale-ref]: e1 is from a document its frame has since replaced. — Run 'dramaturge snapshot' for the current tab's refs.", stale.Error.Trim());
        Assert.Contains("\"navigated\":true,\"title\":\"Fake title\",\"snapshot\":\"- heading", json.Output);
    }

    [Fact]
    public async Task ActionThatNavigatesWithoutASnapshotShowsTheTitle()
    {
        string[] page = [string.Empty];
        this.sessions.OnConnect = session =>
        {
            page[0] = session.Contexts[0].Id;
            session.RemoteEnd.AnswerWith("script.callFunction", this.AnswerScript);
            session.RemoteEnd.AnswerWith("input.performActions", _ => new FakeResponse(new JsonObject(), [Navigation(page[0], "https://example.com/next")]));
        };
        await this.StartWithSnapshotAsync();

        ToolResult click = await this.sessions.RunAsync("click", "e3", "--no-snapshot");

        Assert.Equal(["Clicked e3, and the page navigated to https://example.com/next", Start, "Title: Fake title", End], click.OutputLines);
    }

    [Fact]
    public async Task SnapshotThatFailsAfterTheActionIsSaidSo()
    {
        await this.StartWithSnapshotAsync();
        this.Session().RemoteEnd.AnswerWith("script.callFunction", parameters => ((string)parameters["functionDeclaration"]!).Contains("snapshots.snapshot") ? FakeResponse.Failure("no such frame", "The page is changing.") : this.AnswerScript(parameters));

        ToolResult click = await this.sessions.RunAsync("click", "e3");
        ToolResult json = await this.sessions.RunAsync("click", "e3", "--json");

        Assert.Equal(0, click.ExitCode);
        Assert.Equal(["Clicked e3", "No snapshot was taken, as the page was changing. Run 'dramaturge snapshot' to see it."], click.OutputLines);
        Assert.Contains("\"snapshotError\":", json.Output);
    }

    // What happened since the last action, the action's own effects among it, is shown.
    [Fact]
    public async Task EffectsOfTheActionAreShown()
    {
        string[] page = [string.Empty];
        this.sessions.OnConnect = session =>
        {
            page[0] = session.Contexts[0].Id;
            session.RemoteEnd.AnswerWith("script.callFunction", this.AnswerScript);
            session.RemoteEnd.AnswerWith("input.performActions", _ => new FakeResponse(new JsonObject(),
            [
                Prompt(page[0], "confirm", "Sure?", "dismiss"),
                Prompt(page[0], "alert", "Hi", "accept"),
                new FakeEvent("browsingContext.downloadWillBegin", new JsonObject() { ["context"] = page[0], ["download"] = "download-1", ["navigation"] = "download-1", ["timestamp"] = 1790000000000, ["url"] = "https://example.com/report.pdf", ["suggestedFilename"] = "report.pdf" }),
                new FakeEvent("browsingContext.contextCreated", new JsonObject() { ["context"] = "popup-1", ["clientWindow"] = "window-2", ["url"] = "https://example.com/popup", ["userContext"] = "default", ["children"] = null, ["parent"] = null, ["originalOpener"] = page[0] }),
                new FakeEvent("log.entryAdded", LogEntry(page[0], "javascript", "error", "Error: boom")),
                new FakeEvent("log.entryAdded", LogEntry(page[0], "console", "info", "fine")),
            ]));
        };
        await this.StartWithSnapshotAsync();
        await this.Session().RemoteEnd.RaiseEventAsync("browsingContext.userPromptOpened", Prompt(page[0], "alert", "Before", "dismiss").Parameters);
        await this.sessions.RunAsync("url");

        ToolResult click = await this.sessions.RunAsync("click", "e3", "--no-snapshot");

        Assert.Equal(
            [
                "Clicked e3",
                Start,
                "Dialog (alert): \"Before\" — dismissed",
                "Dialog (confirm): \"Sure?\" — dismissed",
                "Dialog (alert): \"Hi\" — accepted",
                "Download: report.pdf from https://example.com/report.pdf — refused; start the session with --downloads <dir> to save downloads",
                "Opened tab [2] https://example.com/popup",
                "Console error: Uncaught Error: boom",
                End,
            ],
            click.OutputLines);
    }

    // An effect that comes after its action has answered is shown by the next action, and each is shown once.
    [Fact]
    public async Task EffectThatComesLateIsShownByTheNextAction()
    {
        string downloads = Directory.CreateTempSubdirectory("dts-downloads-").FullName;
        try
        {
            await this.StartWithSnapshotAsync("--downloads", downloads);
            string page = this.Session().Contexts[0].Id;

            ToolResult first = await this.sessions.RunAsync("click", "e3", "--no-snapshot");
            await this.Session().RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", new JsonObject() { ["context"] = page, ["download"] = "download-1", ["navigation"] = "download-1", ["timestamp"] = 1790000000000, ["url"] = "https://example.com/late.pdf", ["suggestedFilename"] = "late.pdf" });
            await this.Session().RemoteEnd.RaiseEventAsync("log.entryAdded", LogEntry(page, "javascript", "error", "Error: late"));
            await this.sessions.RunAsync("url");
            ToolResult second = await this.sessions.RunAsync("click", "e3", "--no-snapshot");
            ToolResult third = await this.sessions.RunAsync("click", "e3", "--no-snapshot");

            Assert.Equal(["Clicked e3"], first.OutputLines);
            Assert.Equal(["Clicked e3", Start, "Download: late.pdf from https://example.com/late.pdf", "Console error: Uncaught Error: late", End], second.OutputLines);
            Assert.Equal(["Clicked e3"], third.OutputLines);
            JsonObject allowed = this.Commands("browser.setDownloadBehavior").Single();
            Assert.Equal("allowed", (string?)allowed["params"]!["downloadBehavior"]!["type"]);
            Assert.Equal(downloads, (string?)allowed["params"]!["downloadBehavior"]!["destinationFolder"]);
        }
        finally
        {
            Directory.Delete(downloads, true);
        }
    }

    [Fact]
    public async Task NewestHundredEffectsAreKept()
    {
        await this.StartWithSnapshotAsync();
        string page = this.Session().Contexts[0].Id;
        for (int i = 0; i < 101; i++)
        {
            await this.Session().RemoteEnd.RaiseEventAsync("browsingContext.userPromptOpened", Prompt(page, "alert", $"Alert {i}", "dismiss").Parameters);
        }

        await this.sessions.RunAsync("url");
        ToolResult click = await this.sessions.RunAsync("click", "e3", "--no-snapshot");

        Assert.Equal(103, click.OutputLines.Count);
        Assert.Equal("Dialog (alert): \"Alert 1\" — dismissed", click.OutputLines[2]);
    }

    [Fact]
    public async Task DownloadsAreRefusedUnlessTheSessionSavesThem()
    {
        await this.sessions.RunAsync("session", "start");
        ToolResult missing = await this.sessions.RunAsync("session", "start", "--session", "other", "--downloads", Path.Combine(this.sessions.Directory, "missing"));

        Assert.Equal("denied", (string?)this.Commands("browser.setDownloadBehavior").Single()["params"]!["downloadBehavior"]!["type"]);
        Assert.Equal(2, missing.ExitCode);
        Assert.Equal($"error[usage]: '{Path.Combine(this.sessions.Directory, "missing")}' is not a directory.", missing.Error.Trim());
    }

    [Fact]
    public async Task RefMustBeInTheCurrentTabsLatestSnapshot()
    {
        await this.sessions.RunAsync("session", "start");

        ToolResult noSnapshot = await this.sessions.RunAsync("click", "e1");
        await this.sessions.RunAsync("snapshot");
        ToolResult unknown = await this.sessions.RunAsync("click", "e9");
        ToolResult screenshot = await this.sessions.RunAsync("screenshot", "-o", Path.Combine(this.sessions.Directory, "e9.png"), "--ref", "e9");

        Assert.Equal("error[unknown-ref]: The current tab has no snapshot to take e1 from. — Run 'dramaturge snapshot' for the current tab's refs.", noSnapshot.Error.Trim());
        Assert.Equal("error[unknown-ref]: The current tab's latest snapshot has no ref e9. — Run 'dramaturge snapshot' for the current tab's refs.", unknown.Error.Trim());
        Assert.Equal(1, screenshot.ExitCode);
        Assert.StartsWith("error[unknown-ref]", screenshot.Error);
    }

    [Fact]
    public async Task ActionWithNoTabOpenFails()
    {
        await this.StartWithSnapshotAsync();
        await this.Session().RemoteEnd.RaiseEventAsync("browsingContext.contextDestroyed", new JsonObject() { ["context"] = this.Session().Contexts[0].Id, ["clientWindow"] = "window-1", ["url"] = "about:blank", ["userContext"] = "default", ["children"] = null, ["originalOpener"] = null });
        await this.sessions.RunAsync("tabs");

        ToolResult click = await this.sessions.RunAsync("click", "e1");

        Assert.Equal("error[no-tab]: The session has no tab open. — Open one with 'dramaturge open <url>'.", click.Error.Trim());
    }

    [Fact]
    public async Task ElementScreenshotCapturesTheElement()
    {
        await this.StartWithSnapshotAsync();
        string path = Path.Combine(this.sessions.Directory, "element.png");

        ToolResult screenshot = await this.sessions.RunAsync("screenshot", "-o", path, "--ref", "e3");

        Assert.Equal(0, screenshot.ExitCode);
        Assert.Equal(FakeBrowserServer.Screenshot, File.ReadAllBytes(path));
        Assert.Equal("node-e3", (string?)this.Commands("browsingContext.captureScreenshot").Single()["params"]!["clip"]!["element"]!["sharedId"]);
        Assert.Single(this.Commands("browsingContext.activate"));
    }

    [Fact]
    public async Task ActionThatFailsSaysWhy()
    {
        await this.StartWithSnapshotAsync();
        this.Session().RemoteEnd.FailWith("input.performActions", "unknown error", "The input failed.");

        ToolResult click = await this.sessions.RunAsync("click", "e3");
        ToolResult badKey = await this.sessions.RunAsync("press", "e2", "Hyper+a");
        ToolResult unknownKey = await this.sessions.RunAsync("press", "e2", "Hyperspace");
        ToolResult numericModifier = await this.sessions.RunAsync("press", "e2", "1+a");
        ToolResult noneModifier = await this.sessions.RunAsync("press", "e2", "None+a");

        Assert.Equal(1, click.ExitCode);
        Assert.StartsWith("error[action-failed]: ", click.Error);
        Assert.Contains("The input failed.", click.Error);
        Assert.Equal(2, badKey.ExitCode);
        Assert.Equal("error[usage]: 'Hyper+a' is not a key, such as Enter, a, or Control+a.", badKey.Error.Trim());
        Assert.Equal(2, unknownKey.ExitCode);
        Assert.Equal(2, numericModifier.ExitCode);
        Assert.Equal(2, noneModifier.ExitCode);
    }

    // Files are uploaded only from the directory the session started in, those given, and those below them, however
    // links lead.
    [Fact]
    public async Task UploadIsLimitedToTheSessionsDirectories()
    {
        string root = Directory.CreateTempSubdirectory("dts-upload-").FullName;
        try
        {
            string allowed = Directory.CreateDirectory(Path.Combine(root, "allowed")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            string inside = Path.Combine(allowed, "inside.txt");
            string secret = Path.Combine(outside, "secret.txt");
            File.WriteAllText(inside, "inside");
            File.WriteAllText(secret, "secret");
            File.CreateSymbolicLink(Path.Combine(allowed, "link.txt"), secret);
            Directory.CreateSymbolicLink(Path.Combine(allowed, "linked"), outside);
            Directory.CreateSymbolicLink(Path.Combine(root, "alias"), allowed);
            await this.StartWithSnapshotAsync("--allow-upload-from", Path.Combine(root, "alias"));

            ToolResult direct = await this.sessions.RunAsync("upload", "e2", inside, "--no-snapshot");
            ToolResult throughAlias = await this.sessions.RunAsync("upload", "e2", Path.Combine(root, "alias", "inside.txt"), "--no-snapshot");
            ToolResult fileLink = await this.sessions.RunAsync("upload", "e2", Path.Combine(allowed, "link.txt"));
            ToolResult directoryLink = await this.sessions.RunAsync("upload", "e2", Path.Combine(allowed, "linked", "secret.txt"));
            ToolResult outsideFile = await this.sessions.RunAsync("upload", "e2", secret);
            ToolResult missing = await this.sessions.RunAsync("upload", "e2", Path.Combine(allowed, "missing.txt"));
            ToolResult notDirectory = await this.sessions.RunAsync("session", "start", "--session", "other", "--allow-upload-from", Path.Combine(root, "missing"));

            Assert.Equal(0, direct.ExitCode);
            Assert.Equal(0, throughAlias.ExitCode);
            Assert.Equal($"error[upload-not-allowed]: {Path.Combine(allowed, "link.txt")} is not in a directory the session uploads from. — Start the session in a directory above it, or with --allow-upload-from.", fileLink.Error.Trim());
            Assert.StartsWith("error[upload-not-allowed]", directoryLink.Error);
            Assert.StartsWith("error[upload-not-allowed]", outsideFile.Error);
            Assert.Equal($"error[no-file]: There is no file {Path.Combine(allowed, "missing.txt")}.", missing.Error.Trim());
            Assert.Equal(2, notDirectory.ExitCode);
            Assert.Equal($"error[usage]: '{Path.Combine(root, "missing")}' is not a directory.", notDirectory.Error.Trim());
            Assert.Equal(2, this.Commands("input.setFiles").Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("dismiss", "dismiss")]
    [InlineData("accept", "accept")]
    [InlineData(null, "dismiss")]
    public async Task DialogsAreAnsweredAsTheSessionSays(string? dialogs, string expected)
    {
        await this.sessions.RunAsync(dialogs is null ? ["session", "start"] : ["session", "start", "--dialogs", dialogs]);

        JsonObject newSession = this.Session().RemoteEnd.CommandsFor("session.new").Single();

        Assert.Equal(expected, (string?)newSession["params"]!["capabilities"]!["alwaysMatch"]!["unhandledPromptBehavior"]!["default"]);
    }

    [Fact]
    public async Task ExpectSaysWhichConditionPassed()
    {
        await this.StartWithSnapshotAsync();
        this.checkedState = "unchecked";

        string[][] conditions =
        [
            ["url", "about:blank"],
            ["title", "Fake title"],
            ["text", "Welcome"],
            ["e1", "visible"],
            ["e1", "enabled"],
            ["e3", "unchecked"],
            ["e1", "text", "Form"],
            ["e2", "value", "Ada"],
            ["e1", "snapshot", "- heading \"Form\""],
        ];
        List<string> said = [];
        foreach (string[] condition in conditions)
        {
            ToolResult result = await this.sessions.RunAsync(["expect", .. condition]);
            said.Add(result.OutputLines.Count > 0 ? result.OutputLines[0] : result.Error.Trim());
        }

        this.checkedState = "checked";
        ToolResult isChecked = await this.sessions.RunAsync("expect", "e3", "checked");
        this.visible = false;
        ToolResult hidden = await this.sessions.RunAsync("expect", "e1", "hidden");

        Assert.Equal(
            [
                "Passed: the tab's URL is about:blank.",
                "Passed: the tab's title is \"Fake title\".",
                "Passed: the tab's page contains \"Welcome\".",
                "Passed: e1 is visible.",
                "Passed: e1 is enabled.",
                "Passed: e3 is not checked.",
                "Passed: e1 contains \"Form\".",
                "Passed: e2 has the value \"Ada\".",
                "Passed: e1 matches the snapshot given.",
            ],
            said);
        Assert.Equal(["Passed: e3 is checked."], isChecked.OutputLines);
        Assert.Equal(["Passed: e1 is hidden."], hidden.OutputLines);
        Assert.Equal("body", (string?)this.Commands("browsingContext.locateNodes").Single()["params"]!["locator"]!["value"]);
    }

    // Without a template, the element's snapshot now is the one expected, and is shown.
    [Fact]
    public async Task ExpectSnapshotWithoutATemplateUsesTheElementsSnapshot()
    {
        await this.StartWithSnapshotAsync();

        ToolResult snapshot = await this.sessions.RunAsync("expect", "e1", "snapshot");

        Assert.Equal(["Passed: e1 matches its snapshot.", Start, .. Form.Split('\n'), End], snapshot.OutputLines);
        Assert.Contains("false", this.Commands("script.callFunction").Last(command => ((string)command["params"]!["functionDeclaration"]!).Contains("snapshots.snapshot"))["params"]!["arguments"]!.ToJsonString());
    }

    // What the page showed is page content, kept apart from the tool's own words.
    [Fact]
    public async Task ExpectThatIsNotMetShowsWhatThePageHad()
    {
        await this.StartWithSnapshotAsync();

        ToolResult text = await this.sessions.RunAsync("expect", "e1", "text", "Nope", "--timeout", "1");
        ToolResult json = await this.sessions.RunAsync("expect", "title", "Other", "--timeout", "2", "--json");

        Assert.Equal(1, text.ExitCode);
        Assert.Equal(["error[expectation-failed]: Not met within 1 second: e1 contains \"Nope\".", Start, "Expected ref e1 to contain text \"Nope\"; received \"Form Welcome\" after 1 seconds.", End], text.Error.Trim().Split(Environment.NewLine));
        Assert.Equal(1, json.ExitCode);
        Assert.Contains("\"code\":\"expectation-failed\",\"message\":\"Not met within 2 seconds: the tab's title is \\\"Other\\\".\",\"hint\":null,\"details\":\"Expected the page to have title", json.Output);
    }

    // A snapshot of an element that cannot be found in time is not met, as any other expectation.
    [Fact]
    public async Task ExpectSnapshotOfAnElementThatIsGoneIsNotMet()
    {
        await this.StartWithSnapshotAsync();
        this.Session().RemoteEnd.AnswerWith("script.callFunction", parameters => ((string)parameters["functionDeclaration"]!).Contains("isConnected") ? Success(new JsonObject() { ["type"] = "null" }) : this.AnswerScript(parameters));

        ToolResult snapshot = await this.sessions.RunAsync("expect", "e1", "snapshot", "--timeout", "1");

        Assert.Equal(1, snapshot.ExitCode);
        Assert.StartsWith("error[expectation-failed]: Not met within 1 second: e1 matches its snapshot.", snapshot.Error);
    }

    [Fact]
    public async Task ExpectThatCannotBeUsedSaysWhy()
    {
        ToolResult wait = await this.sessions.RunAsync("wait", "e1", "visible");
        await this.sessions.RunAsync("session", "start");
        ToolResult noTab = await this.ExpectWithNoTabAsync();
        await this.sessions.RunAsync("open", "about:blank");

        ToolResult bogus = await this.sessions.RunAsync("expect", "e1", "bogus");
        ToolResult tooLong = await this.sessions.RunAsync("expect", "e1", "visible", "now");
        ToolResult zero = await this.sessions.RunAsync("expect", "e1", "visible", "--timeout", "0");
        ToolResult unknown = await this.sessions.RunAsync("expect", "e9", "visible");
        ToolResult template = await this.sessions.RunAsync("expect", "e1", "snapshot", "- heading not valid");

        Assert.Equal(2, wait.ExitCode);
        Assert.EndsWith("— To wait until a condition is met, use 'dramaturge expect'.", wait.Error.Trim());
        Assert.StartsWith("error[no-tab]", noTab.Error);
        Assert.Equal("error[usage]: 'e1 bogus' is not a condition, such as 'e5 visible', 'e5 text Saved', or 'url https://example.com/'. — Run 'dramaturge expect --help' for the conditions.", bogus.Error.Trim());
        Assert.Equal(2, tooLong.ExitCode);
        Assert.Equal("error[usage]: --timeout must be at least 1 second.", zero.Error.Trim());
        Assert.StartsWith("error[unknown-ref]", unknown.Error);
        Assert.Equal(2, template.ExitCode);
    }

    // The code is the file alone, so that it can be redirected to one.
    [Fact]
    public async Task CodeShowsTheSessionsRecording()
    {
        await this.sessions.RunAsync("session", "start", "--dialogs", "accept");
        await this.sessions.RunAsync("open", "https://example.com/", "--no-snapshot");
        string path = Path.Combine(this.sessions.Directory, "Recorded.cs");

        ToolResult program = await this.sessions.RunAsync("code");
        ToolResult test = await this.sessions.RunAsync("code", "--target", "nunit", "-o", path);
        ToolResult json = await this.sessions.RunAsync("code", "--target", "xunit", "--json");
        ToolResult unwritable = await this.sessions.RunAsync("code", "-o", Path.Combine(this.sessions.Directory, "missing", "Recorded.cs"));

        Assert.Equal(0, program.ExitCode);
        Assert.Equal("using Dramaturge;", program.OutputLines[0]);
        Assert.Equal("await page.NavigateAsync(\"https://example.com/\");", program.OutputLines[^1]);
        Assert.Equal(string.Empty, program.Error);
        Assert.Equal([$"Wrote the code for nunit to {path}."], test.OutputLines);
        Assert.Contains("[Test]", File.ReadAllText(path));
        Assert.Contains("\"target\":\"xunit\",\"code\":\"using Dramaturge;", json.Output);
        Assert.Equal(1, unwritable.ExitCode);
        Assert.StartsWith("error[write-failed]: The code could not be written to ", unwritable.Error);
    }

    // After a reset, the code is what was done since; a reset that cannot write its file does not start over.
    [Fact]
    public async Task CodeWithResetStartsTheRecordingOver()
    {
        await this.sessions.RunAsync("session", "start");
        string path = Path.Combine(this.sessions.Directory, "Exploring.cs");
        await this.sessions.RunAsync("open", "https://example.com/exploring", "--no-snapshot");

        ToolResult unwritable = await this.sessions.RunAsync("code", "--reset", "-o", Path.Combine(this.sessions.Directory, "missing", "Recorded.cs"));
        ToolResult explored = await this.sessions.RunAsync("code", "--reset", "-o", path);
        await this.sessions.RunAsync("open", "https://example.com/start", "--no-snapshot");
        ToolResult json = await this.sessions.RunAsync("code", "--reset", "--json");
        ToolResult after = await this.sessions.RunAsync("code");

        Assert.StartsWith("error[write-failed]", unwritable.Error);
        Assert.Equal([$"Wrote the code for program to {path}, and started the recording over."], explored.OutputLines);
        Assert.Contains("https://example.com/exploring", File.ReadAllText(path));
        Assert.Contains("\"reset\":true", json.Output);
        Assert.Contains("https://example.com/start", json.Output);
        Assert.DoesNotContain("https://example.com/exploring", json.Output);
        Assert.DoesNotContain("NavigateAsync", after.Output);
    }

    // The code sets its browser to answer dialogs as the session's did.
    [Theory]
    [InlineData("accept", true)]
    [InlineData("dismiss", false)]
    public async Task CodeAnswersDialogsAsTheSessionDid(string dialogs, bool accepts)
    {
        await this.sessions.RunAsync("session", "start", "--dialogs", dialogs);

        ToolResult program = await this.sessions.RunAsync("code");
        ToolResult test = await this.sessions.RunAsync("code", "--target", "xunit");

        Assert.Equal(accepts, program.Output.Contains("WithHeadlessOption(false).WithSessionCapability(\"unhandledPromptBehavior\", new UserPromptHandler() { Default = UserPromptHandlerType.Accept }));"));
        Assert.Equal(accepts, test.Output.Contains("protected override BrowserLauncherBuilder ConfigureLauncher(BrowserLauncherBuilder builder) => builder.WithSessionCapability(\"unhandledPromptBehavior\", new UserPromptHandler() { Default = UserPromptHandlerType.Accept });"));
    }

    [Theory]
    [InlineData("{\"command\":\"code\",\"arguments\":{\"target\":\"cobol\"}}")]
    [InlineData("{\"command\":\"expect\",\"arguments\":{}}")]
    [InlineData("{\"command\":\"click\",\"arguments\":{}}")]
    [InlineData("{\"command\":\"fill\",\"arguments\":{\"ref\":\"e2\"}}")]
    [InlineData("{\"command\":\"press\",\"arguments\":{\"ref\":\"e2\"}}")]
    [InlineData("{\"command\":\"select\",\"arguments\":{\"ref\":\"e2\"}}")]
    [InlineData("{\"command\":\"select\",\"arguments\":{\"ref\":\"e2\",\"values\":[]}}")]
    [InlineData("{\"command\":\"select\",\"arguments\":{\"ref\":\"e2\",\"values\":[1]}}")]
    [InlineData("{\"command\":\"select\",\"arguments\":{\"ref\":\"e2\",\"values\":\"Red\"}}")]
    [InlineData("{\"command\":\"select\",\"arguments\":[]}")]
    [InlineData("{\"command\":\"upload\",\"arguments\":{\"ref\":\"e2\"}}")]
    public async Task ActionWithArgumentsItCannotUseIsABadRequest(string request)
    {
        await this.StartWithSnapshotAsync();

        string answer = await InProcessSessions.ExchangeAsync(Path.Combine(this.sessions.Directory, "default.sock"), request + "\n");

        Assert.Contains("\"code\":\"bad-request\"", answer);
    }

    private static int PressedButton(JsonObject command)
    {
        return (int)command["params"]!["actions"]![0]!["actions"]!.AsArray().First(action => (string?)action!["type"] == "pointerDown")!["button"]!;
    }

    private static FakeEvent Navigation(string contextId, string url)
    {
        return new FakeEvent("browsingContext.navigationCommitted", new JsonObject() { ["context"] = contextId, ["navigation"] = "navigation-9", ["timestamp"] = 1790000000000, ["url"] = url, ["userContext"] = "default" });
    }

    private static FakeEvent Prompt(string contextId, string type, string message, string handler)
    {
        return new FakeEvent("browsingContext.userPromptOpened", new JsonObject() { ["context"] = contextId, ["handler"] = handler, ["message"] = message, ["type"] = type, ["userContext"] = "default" });
    }

    private static JsonObject LogEntry(string contextId, string type, string level, string text)
    {
        JsonObject entry = new() { ["type"] = type, ["level"] = level, ["source"] = new JsonObject() { ["realm"] = "realm-1", ["context"] = contextId }, ["text"] = text, ["timestamp"] = 1790000000000 };
        if (type == "console")
        {
            entry["method"] = "log";
            entry["args"] = new JsonArray(Text(text));
        }

        return entry;
    }

    private static JsonObject Text(string value) => new() { ["type"] = "string", ["value"] = value };

    private static FakeResponse Success(JsonObject value) => new(new JsonObject() { ["type"] = "success", ["result"] = value, ["realm"] = "realm-1" });

    private static JsonObject SnapshotValue(string text)
    {
        static JsonArray Property(string name, JsonNode value) => [name, value];
        static JsonObject Text(string value) => new() { ["type"] = "string", ["value"] = value };
        string[] refs = [.. RefPattern().Matches(text).Select(match => match.Groups[1].Value)];
        return new JsonObject()
        {
            ["type"] = "object",
            ["value"] = new JsonArray(
                Property("tree", Text("{\"role\":\"fragment\",\"name\":\"\",\"children\":[]}")),
                Property("text", Text(text)),
                Property("refs", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray([.. refs.Select(reference => Text(reference))]) }),
                Property("elements", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray([.. refs.Select(reference => Element($"node-{reference}"))]) }),
                Property("frames", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray() })),
        };
    }

    private static JsonObject Element(string sharedId) => new() { ["type"] = "node", ["sharedId"] = sharedId, ["value"] = new JsonObject() { ["nodeType"] = 1, ["childNodeCount"] = 0 } };

    [GeneratedRegex(@"\[ref=([a-z0-9]+)\]")]
    private static partial Regex RefPattern();

    // The snapshot, the page's title and load state, each ref's element, as connected and ready, and nothing for any
    // other script.
    private FakeResponse AnswerScript(JsonObject parameters)
    {
        string function = (string)parameters["functionDeclaration"]!;
        JsonObject value = function.Contains("snapshots.snapshot") ? SnapshotValue(this.snapshot)
            : function.Contains("document.title") ? new JsonObject() { ["type"] = "string", ["value"] = "Fake title" }
            : function.Contains("document.readyState") ? new JsonObject() { ["type"] = "string", ["value"] = "complete" }
            : function.Contains("isConnected") ? Element((string)parameters["arguments"]![0]!["sharedId"]!)
            : function.Contains("isInteractionReady") ? Readiness()
            : function.Contains("queryElementStates") ? new JsonObject() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("status", Text("success"))) }
            : function.Contains("queryElementState(") ? new JsonObject() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("received", Text(function.Contains("'enabled'") ? "enabled" : this.checkedState)), new JsonArray("isRadio", new JsonObject() { ["type"] = "boolean", ["value"] = false })) }
            : function.Contains("isElementVisible") ? new JsonObject() { ["type"] = "boolean", ["value"] = this.visible }
            : function.Contains("readTexts") ? new JsonObject() { ["type"] = "array", ["value"] = new JsonArray(Text("Form Welcome")) }
            : function.Contains("readValue") ? Text("Ada")
            : function.Contains("snapshots.validate") && ((string?)parameters["arguments"]![0]!["value"])!.Contains("not valid") ? Text("The template is not valid.")
            : function.Contains("snapshots.match") ? new JsonObject() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("matches", new JsonObject() { ["type"] = "boolean", ["value"] = true }), new JsonArray("actual", Text(this.snapshot))) }
            : function.Contains("selectText") ? new JsonObject() { ["type"] = "boolean", ["value"] = true }
            : new JsonObject() { ["type"] = "undefined" };
        return new FakeResponse(new JsonObject() { ["type"] = "success", ["result"] = value, ["realm"] = "realm-1" });
    }

    private static JsonObject Readiness()
    {
        static JsonObject Number(double value) => new() { ["type"] = "number", ["value"] = value };
        JsonObject offset = new() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("x", Number(5)), new JsonArray("y", Number(5))) };
        return new JsonObject() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("status", new JsonObject() { ["type"] = "string", ["value"] = "ready" }), new JsonArray("interactionOffset", offset)) };
    }

    private async Task<ToolResult> ExpectWithNoTabAsync()
    {
        await this.Session().RemoteEnd.RaiseEventAsync("browsingContext.contextDestroyed", new JsonObject() { ["context"] = this.Session().Contexts[0].Id, ["clientWindow"] = "window-1", ["url"] = "about:blank", ["userContext"] = "default", ["children"] = null, ["originalOpener"] = null });
        await this.sessions.RunAsync("tabs");
        return await this.sessions.RunAsync("expect", "url", "about:blank");
    }

    private async Task StartWithSnapshotAsync(params string[] startArguments)
    {
        await this.sessions.RunAsync(["session", "start", .. startArguments]);
        await this.sessions.RunAsync("snapshot");
    }

    private FakeSession Session() => this.sessions.Server.SessionFor("default");

    private IReadOnlyList<JsonObject> Commands(string method) => this.Session().RemoteEnd.CommandsFor(method);
}
