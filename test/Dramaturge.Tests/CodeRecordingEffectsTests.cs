// <copyright file="CodeRecordingEffectsTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.Text.Json.Nodes;
using Dramaturge.TestUtilities;
using WebDriverBiDi;
using WebDriverBiDi.BrowsingContext;
using static Dramaturge.TestUtilities.RecorderMessages;

// No locator candidate resolves on the fake, so each element is located by its CSS path.
public class CodeRecordingEffectsTests
{
    private const string NavigationStarted = "browsingContext.navigationStarted";
    private const string NavigationCommitted = "browsingContext.navigationCommitted";

    [Fact]
    public async Task NavigationTheActionCausedIsWaitedFor()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Click("#next"));
        await StartAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/next"));
        await CommitAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/again"));
        await StartAsync(driver, session, page.Id, "https://example.com/again", "navigation-again");
        await CommitAsync(driver, session, page.Id, "https://example.com/again", "navigation-again");
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["await page.RunAndWaitForNavigationAsync(() => page.Locate(new CssLocator(\"#next\")).ClickAsync());"], Statements(code));
    }

    [Fact]
    public async Task NavigationReportedBeforeItStartsIsTheActionsEffect()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Click("#go"));
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/next"));
        await StartAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await CommitAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["await page.RunAndWaitForNavigationAsync(() => page.Locate(new CssLocator(\"#go\")).ClickAsync());"], Statements(code));
    }

    // Chrome sometimes loses a document's report, so the action pending as a navigation starts is credited with it
    // without one; an action causes one navigation at most.
    [Fact]
    public async Task NavigationWithoutAReportIsThePendingActionsOnceAndThenTheUsers()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        FakeContext frame = await session.CreateFrameAsync(page.Id);
        await WaitUntilAsync(() => page.Frames.Count == 2);
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Click("#go"));
        await SendAsync(driver, session, channel, frame.Id, Navigate("https://example.com/next"));
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/elsewhere"));
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/other"));
        await StartAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await CommitAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await StartAsync(driver, session, page.Id, "https://example.com/typed", "navigation-typed");
        await CommitAsync(driver, session, page.Id, "https://example.com/typed", "navigation-typed");
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.RunAndWaitForNavigationAsync(() => page.Locate(new CssLocator(\"#go\")).ClickAsync());",
                "await page.NavigateAsync(\"https://example.com/typed\");",
            ],
            Statements(code));
    }

    [Fact]
    public async Task NavigationThePageStartedWithoutThePendingActionIsNotRecorded()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/redirect"));
        await StartAsync(driver, session, page.Id, "https://example.com/redirect", "navigation-redirect");
        await CommitAsync(driver, session, page.Id, "https://example.com/redirect", "navigation-redirect");
        await SendAsync(driver, session, channel, page.Id, Click("#first"));
        await StartAsync(driver, session, page.Id, "https://example.com/superseded", "navigation-superseded");
        await SendAsync(driver, session, channel, page.Id, Click("#second"));
        await StartAsync(driver, session, page.Id, "https://example.com/timer", "navigation-timer");
        await SendAsync(driver, session, channel, page.Id, Click("#third"));
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/timer"));
        await CommitAsync(driver, session, page.Id, "https://example.com/superseded", "navigation-superseded");
        await CommitAsync(driver, session, page.Id, "https://example.com/timer", "navigation-timer");

        // A key the code cannot press is no action for a navigation to wait for.
        await SendAsync(driver, session, channel, page.Id, Action("press", "#notes", new JsonField("key", "ContextMenu"), new JsonField("modifiers", new JsonArray())));
        await StartAsync(driver, session, page.Id, "https://example.com/menu", "navigation-menu");
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/menu"));
        await CommitAsync(driver, session, page.Id, "https://example.com/menu", "navigation-menu");
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.Locate(new CssLocator(\"#first\")).ClickAsync();",
                "await page.Locate(new CssLocator(\"#second\")).ClickAsync();",
                "await page.Locate(new CssLocator(\"#third\")).ClickAsync();",
                "// The key \"ContextMenu\" was pressed, which is neither a member of Keys nor a character.",
            ],
            Statements(code));
    }

    [Fact]
    public async Task NavigationNoActionCausedIsANavigation()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        FakeContext frame = await session.CreateFrameAsync(page.Id);
        await WaitUntilAsync(() => page.Frames.Count == 2);
        Page second = await page.Browser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        Browser other = await page.Browser.Group.CreateBrowserAsync(cancellationToken: TestContext.Current.CancellationToken);
        Page otherPage = await other.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await StartAsync(driver, session, page.Id, "https://example.com/", "navigation-page");
        await CommitAsync(driver, session, page.Id, "https://example.com/", "navigation-page");
        await StartAsync(driver, session, page.Id, "about:blank", "navigation-blank");
        await CommitAsync(driver, session, page.Id, "about:blank", "navigation-blank");
        await StartAsync(driver, session, frame.Id, "https://example.com/frame", "navigation-frame");
        await CommitAsync(driver, session, frame.Id, "https://example.com/frame", "navigation-frame");
        await StartAsync(driver, session, otherPage.Id, "https://example.com/other", "navigation-other");
        await CommitAsync(driver, session, otherPage.Id, "https://example.com/other", "navigation-other");
        await StartAsync(driver, session, "unknown-context", "https://example.com/unknown", "navigation-unknown");
        await CommitAsync(driver, session, "unknown-context", "https://example.com/unknown", "navigation-unknown");
        await SendAsync(driver, session, channel, page.Id, Fill("#search", "a"));
        await StartAsync(driver, session, second.Id, "https://example.com/second", "navigation-second");
        await CommitAsync(driver, session, second.Id, "https://example.com/second", "navigation-second");
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.NavigateAsync(\"https://example.com/\");",
                "await page.Locate(new CssLocator(\"#search\")).FillAsync(\"a\");",
                "Page page1 = await page.Browser.NewPageAsync();",
                "await page1.NavigateAsync(\"https://example.com/second\");",
            ],
            Statements(code));
    }

    // The lookups may take as long as a navigation, so only their cancelling as it commits settles the action in time.
    [Fact]
    public async Task NavigationCancelsTheLookupsInTheDocumentsItReplaces()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync(TimeSpan.FromMinutes(10));
        await using BiDiDriver ownedDriver = driver;
        FakeContext frame = await session.CreateFrameAsync(page.Id);
        await WaitUntilAsync(() => page.Frames.Count == 2);
        session.RemoteEnd.NeverAnswer("browsingContext.locateNodes");
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);
        TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recording.OnStatement.AddObserver(_ => settled.TrySetResult());
        JsonObject save = Click("#save");
        save["target"]!["testId"] = "save";
        save["target"]!["role"] = "button";
        save["target"]!["name"] = "Save";
        JsonObject fill = Fill("#name", "Ada");
        fill["target"]!["role"] = "textbox";
        fill["target"]!["name"] = "Name";

        await SendAsync(driver, session, channel, page.Id, save, "node-save");
        await session.RemoteEnd.WaitForCommandAsync("browsingContext.locateNodes");
        await SendAsync(driver, session, channel, frame.Id, Click("#inner"));
        await StartAsync(driver, session, frame.Id, "https://example.com/frame", "navigation-frame");
        await CommitAsync(driver, session, frame.Id, "https://example.com/frame", "navigation-frame");
        await StartAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/next"));
        await CommitAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        session.RemoteEnd.AnswerWith("browsingContext.locateNodes", parameters => ProtocolJson.Nodes((string?)parameters["locator"]!["type"] == "accessibility" ? ["node-name"] : []));
        await SendAsync(driver, session, channel, page.Id, fill, "node-name");
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.RunAndWaitForNavigationAsync(() => page.GetByTestId(\"save\").ClickAsync());",
                "await page.GetByRole(\"textbox\", \"Name\").FillAsync(\"Ada\");",
            ],
            Statements(code));
    }

    // A navigation that starts while the library navigates is the library's, so the pending click is not credited with it.
    [Fact]
    public async Task LibraryNavigationsAreRecordedAsTheirCalls()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        FakeContext child = await session.CreateFrameAsync(page.Id);
        await WaitUntilAsync(() => page.Frames.Count == 2);
        int[] traversals = [0];
        session.RemoteEnd.AnswerWith("browsingContext.traverseHistory", parameters => new FakeResponse(new JsonObject(), [new FakeEvent("browsingContext.historyUpdated", new JsonObject() { ["context"] = (string)parameters["context"]!, ["timestamp"] = 1790000000000, ["url"] = $"https://example.com/{Interlocked.Increment(ref traversals[0])}", ["userContext"] = "default" })]));
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Click("#stay"));
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RemoteEnd.HoldAnswers("browsingContext.navigate", release.Task);
        Task<string> navigating = page.NavigateAsync("https://example.com/next", ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await session.RemoteEnd.WaitForCommandAsync("browsingContext.navigate");
        await StartAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await CommitAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        release.SetResult();
        await navigating;
        session.RemoteEnd.StopHolding("browsingContext.navigate");
        await page.ReloadAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await page.GoBackAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await page.GoForwardAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await page.Frames.Single(frame => frame.Id == child.Id).NavigateAsync("https://example.com/child", ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        session.RemoteEnd.FailWith("browsingContext.navigate", "unknown error", "net::ERR_ABORTED");
        await Assert.ThrowsAsync<WebDriverBiDiCommandException>(() => page.NavigateAsync("https://example.com/aborted", ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken));
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.Locate(new CssLocator(\"#stay\")).ClickAsync();",
                "await page.NavigateAsync(\"https://example.com/next\");",
                "await page.ReloadAsync();",
                "await page.GoBackAsync();",
                "await page.GoForwardAsync();",
            ],
            Statements(code));
    }

    [Fact]
    public async Task LibrarySelectionAndFilesAreReportedToTheRecorder()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await GetChannelAsync(session);
        session.RemoteEnd.AnswerWith("browsingContext.locateNodes", _ => ProtocolJson.Nodes(["node-control"]));
        JsonObject selected = new() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("status", new JsonObject() { ["type"] = "string", ["value"] = "selected" }), new JsonArray("values", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray(new JsonObject() { ["type"] = "string", ["value"] = "g" }) })) };
        session.RemoteEnd.AnswerWith("script.callFunction", parameters => ProtocolJson.Success(((string)parameters["functionDeclaration"]!).Contains("selectOptions") ? (JsonObject)selected.DeepClone() : new JsonObject() { ["type"] = "undefined" }));

        await page.Locate(new CssLocator("#color")).SelectOptionAsync([SelectOption.ByValue("g")], new ActionOptions() { Force = true }, TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#file")).SetInputFilesAsync(["/files/upload.txt"], cancellationToken: TestContext.Current.CancellationToken);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        JsonObject[] reports = [.. session.RemoteEnd.CommandsFor("script.callFunction").Select(command => command["params"]!.AsObject()).Where(parameters => ((string)parameters["functionDeclaration"]!).Contains("recorder.report"))];
        Assert.Equal(2, reports.Length);
        Assert.Contains("recorder.reportSelect(element)", (string)reports[0]["functionDeclaration"]!);
        Assert.Equal("node-control", (string?)reports[0]["arguments"]![0]!["sharedId"]);
        Assert.Contains("recorder.reportFiles(element, files)", (string)reports[1]["functionDeclaration"]!);
        Assert.Equal("/files/upload.txt", (string?)reports[1]["arguments"]![1]!["value"]![0]!["value"]);
    }

    // The browser's own change event names the files; the library's report of the same input, with their paths, replaces it.
    [Fact]
    public async Task FilesSetAgainOnOneInputReplaceTheEarlierStatement()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Action("setInputFiles", "#file", new JsonField("files", new JsonArray("upload.txt"))));
        await SendAsync(driver, session, channel, page.Id, Action("setInputFiles", "#file", new JsonField("files", new JsonArray("/files/upload.txt"))));
        await SendAsync(driver, session, channel, page.Id, Action("setInputFiles", "#other", new JsonField("files", new JsonArray("/files/other.txt"))));
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.Locate(new CssLocator(\"#file\")).SetInputFilesAsync([\"/files/upload.txt\"]);",
                "await page.Locate(new CssLocator(\"#other\")).SetInputFilesAsync([\"/files/other.txt\"]);",
            ],
            Statements(code));
    }

    [Fact]
    public async Task PopupTheActionOpenedIsItsResult()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Click("#open"));
        Page popup = await OpenPopupAsync(session, page);
        await StartAsync(driver, session, popup.Id, "https://example.com/popup", "navigation-popup");
        await CommitAsync(driver, session, popup.Id, "https://example.com/popup", "navigation-popup");
        await StartAsync(driver, session, page.Id, "https://example.com/opener", "navigation-opener");
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/opener"));
        await CommitAsync(driver, session, page.Id, "https://example.com/opener", "navigation-opener");
        await SendAsync(driver, session, channel, popup.Id, Click("#inside"));
        await SendAsync(driver, session, channel, popup.Id, new JsonObject() { ["kind"] = "mode", ["mode"] = "record" });
        await StartAsync(driver, session, popup.Id, "https://example.com/typed", "navigation-typed");
        await CommitAsync(driver, session, popup.Id, "https://example.com/typed", "navigation-typed");
        Page unopened = await OpenPopupAsync(session, page);
        await SendAsync(driver, session, channel, unopened.Id, Click("#elsewhere"));
        await popup.CloseAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.CloseAsync(cancellationToken: TestContext.Current.CancellationToken);
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "Page page1 = await page.RunAndWaitForPopupAsync(() => page.Locate(new CssLocator(\"#open\")).ClickAsync());",
                "await page1.Locate(new CssLocator(\"#inside\")).ClickAsync();",
                "await page1.NavigateAsync(\"https://example.com/typed\");",
                "Page page2 = await page.Browser.NewPageAsync();",
                "await page2.Locate(new CssLocator(\"#elsewhere\")).ClickAsync();",
                "await page1.CloseAsync();",
            ],
            Statements(code));
    }

    [Fact]
    public async Task DownloadTheActionStartedIsItsResult()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", WillBegin(page.Id)));
        await SendAsync(driver, session, channel, page.Id, Click("#report"));
        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", WillBegin(page.Id)));
        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", WillBegin(page.Id)));
        await SendAsync(driver, session, channel, page.Id, Click("#export"));
        await StartAsync(driver, session, page.Id, "https://example.com/export", "navigation-export");
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/export"));
        await CommitAsync(driver, session, page.Id, "https://example.com/export", "navigation-export");
        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", WillBegin(page.Id)));
        await SendAsync(driver, session, channel, page.Id, Click("#archive"));
        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", WillBegin(page.Id)));

        // A navigation that becomes a download is the download, and never commits.
        await SendAsync(driver, session, channel, page.Id, Click("#file"));
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/file"));
        await StartAsync(driver, session, page.Id, "https://example.com/file", "navigation-file");
        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", WillBegin(page.Id, "navigation-other")));
        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.downloadWillBegin", WillBegin(page.Id, "navigation-file")));
        await CommitAsync(driver, session, page.Id, "https://example.com/file", "navigation-file");
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "Download download = await page.RunAndWaitForDownloadAsync(() => page.Locate(new CssLocator(\"#report\")).ClickAsync());",
                "await page.RunAndWaitForNavigationAsync(() => page.Locate(new CssLocator(\"#export\")).ClickAsync());",
                "Download download1 = await page.RunAndWaitForDownloadAsync(() => page.Locate(new CssLocator(\"#archive\")).ClickAsync());",
                "Download download2 = await page.RunAndWaitForDownloadAsync(() => page.Locate(new CssLocator(\"#file\")).ClickAsync());",
            ],
            Statements(code));
    }

    [Fact]
    public async Task DialogIsNotedBeforeTheActionThatOpenedIt()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.userPromptOpened", Prompt(page.Id, "alert", "Hi")));
        await SendAsync(driver, session, channel, page.Id, Fill("#name", "A"));
        await RaiseAsync(driver, session.RemoteEnd.RaiseEventAsync("browsingContext.userPromptOpened", Prompt(page.Id, "confirm", "Sure?")));
        await SendAsync(driver, session, channel, page.Id, Fill("#name", "Ad"));
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "// A dialog opened (alert): \"Hi\"",
                "// Opens a dialog (confirm): \"Sure?\"",
                "await page.Locate(new CssLocator(\"#name\")).FillAsync(\"A\");",
                "await page.Locate(new CssLocator(\"#name\")).FillAsync(\"Ad\");",
            ],
            Statements(code));
    }

    [Fact]
    public async Task FramesAreDeclaredByTheirElementsBeforeTheirFirstStatement()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        FakeContext outer = await session.CreateFrameAsync(page.Id);
        FakeContext inner = await session.CreateFrameAsync(outer.Id);
        FakeContext hidden = await session.CreateFrameAsync(page.Id);
        await WaitUntilAsync(() => page.Frames.Count == 4);
        AnswerFrames(session, new()
        {
            [page.Id] = [new FrameElement(outer.Id, "#outer"), new FrameElement("unrelated-context", "#unrelated")],
            [outer.Id] = [new FrameElement(inner.Id, "#inner")],
        });
        List<string> messages = [];
        page.Browser.Group.OnLogMessage.AddObserver(e => messages.Add(e.Message));
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);

        await SendAsync(driver, session, channel, page.Id, Fill("#search", "a"));
        await SendAsync(driver, session, channel, inner.Id, Click("#go"));

        // The navigation commits once the click's frames are described, which it would otherwise cancel.
        await WaitUntilAsync(() => session.RemoteEnd.CommandsFor("script.callFunction").Count(command => ((string)command["params"]!["functionDeclaration"]!).Contains("describeFrames")) == 2);
        await NetworkEvents.FlushAsync(driver);
        await StartAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await SendAsync(driver, session, channel, page.Id, Navigate("https://example.com/next"));
        await CommitAsync(driver, session, page.Id, "https://example.com/next", "navigation-next");
        await SendAsync(driver, session, channel, outer.Id, Action("assertVisible", "#title"));
        await SendAsync(driver, session, channel, hidden.Id, Click("#hidden"));
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.Locate(new CssLocator(\"#search\")).FillAsync(\"a\");",
                "Frame frame = await page.GetByTitle(\"Payment\").ContentFrameAsync();",
                "Frame frame1 = await frame.Locate(new CssLocator(\"#inner\")).ContentFrameAsync();",
                "await page.RunAndWaitForNavigationAsync(() => frame1.Locate(new CssLocator(\"#go\")).ClickAsync());",
                "await Expect(frame.Locate(new CssLocator(\"#title\"))).ToBeVisibleAsync();",
            ],
            Statements(code));
        Assert.Contains(messages, message => message.Contains($"An action in browsing context {hidden.Id} was not recorded: the element of frame {hidden.Id} was not found in its parent."));
    }

    [Fact]
    public async Task FrameWhoseParentCannotDescribeItsFramesIsNotRecorded()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        FakeContext frame = await session.CreateFrameAsync(page.Id);
        await WaitUntilAsync(() => page.Frames.Count == 2);
        CodeRecording recording = await page.Browser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        string channel = await GetChannelAsync(session);
        session.RemoteEnd.FailWith("script.callFunction", "no such frame", "The document has gone.");

        await SendAsync(driver, session, channel, frame.Id, Click("#go"));
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(Statements(code));
    }

    private static async Task<OpenedPage> OpenPageAsync(TimeSpan? navigationTimeout = null)
    {
        (BiDiDriver driver, FakeSession session) = await FakeSession.ConnectAsync();
        BrowserGroup group = await BrowserGroup.ConnectAsync(driver, new DramaturgeOptions() { NavigationTimeout = navigationTimeout ?? TimeSpan.FromSeconds(5) }, TestContext.Current.CancellationToken);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        AnswerFrames(session, []);
        return new OpenedPage(driver, session, page);
    }

    // Answers the recorder's installs, and each document's description of its frame elements: each frame's context
    // and CSS path; #outer also has a title, which no lookup finds, as when the frame has gone, and #inner a nameable ancestor.
    private static void AnswerFrames(FakeSession session, Dictionary<string, FrameElement[]> frameElements)
    {
        session.RemoteEnd.AnswerWith("script.callFunction", parameters =>
        {
            if (!((string)parameters["functionDeclaration"]!).Contains("describeFrames"))
            {
                return ProtocolJson.Success(new JsonObject() { ["type"] = "undefined" });
            }

            JsonArray described = [];
            foreach ((string contextId, string cssPath) in frameElements.GetValueOrDefault((string)parameters["target"]!["context"]!, []))
            {
                JsonObject target = new() { ["cssPath"] = cssPath, ["labels"] = new JsonArray() };
                if (cssPath == "#outer")
                {
                    target["title"] = "Payment";
                }

                JsonArray ancestors = cssPath == "#inner" ? [new JsonObject() { ["cssPath"] = "#wrap", ["labels"] = new JsonArray() }] : [];
                JsonObject facts = new() { ["target"] = target, ["ancestors"] = ancestors };
                described.Add(new JsonObject()
                {
                    ["type"] = "array",
                    ["value"] = new JsonArray(
                        new JsonObject() { ["type"] = "window", ["value"] = new JsonObject() { ["context"] = contextId } },
                        new JsonObject() { ["type"] = "string", ["value"] = facts.ToJsonString() },
                        Node($"frame-element-{contextId}")),
                });
            }

            return ProtocolJson.Success(new JsonObject() { ["type"] = "array", ["value"] = described });
        });
    }

    // Opens a page as one the page's script opened, once the recording has seen it.
    private static async Task<Page> OpenPopupAsync(FakeSession session, Page opener)
    {
        TaskCompletionSource<Page> created = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable observer = opener.Browser.OnPageCreated.AddObserver(e => created.TrySetResult(e.Page));
        FakeContext context = session.AddContext();
        await session.RemoteEnd.RaiseEventAsync("browsingContext.contextCreated", new JsonObject()
        {
            ["context"] = context.Id,
            ["clientWindow"] = $"window-for-{context.Id}",
            ["originalOpener"] = opener.Id,
            ["url"] = "about:blank",
            ["userContext"] = "default",
            ["children"] = null,
        });
        return await created.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static async Task RaiseAsync(BiDiDriver driver, Task raised)
    {
        await raised;
        await NetworkEvents.FlushAsync(driver);
    }

    private static Task StartAsync(BiDiDriver driver, FakeSession session, string contextId, string url, string navigationId)
    {
        return RaiseAsync(driver, session.RaiseNavigationEventAsync(NavigationStarted, contextId, url, navigationId));
    }

    private static Task CommitAsync(BiDiDriver driver, FakeSession session, string contextId, string url, string navigationId)
    {
        return RaiseAsync(driver, session.RaiseNavigationEventAsync(NavigationCommitted, contextId, url, navigationId));
    }

    private static JsonObject Navigate(string url) => new() { ["kind"] = "navigate", ["url"] = url };

    private static JsonObject WillBegin(string contextId, string? navigationId = null)
    {
        return new JsonObject() { ["context"] = contextId, ["navigation"] = navigationId, ["timestamp"] = 1790000000000, ["url"] = "https://example.com/report.txt", ["suggestedFilename"] = "report.txt", ["download"] = $"download-{Guid.NewGuid():N}" };
    }

    private static JsonObject Prompt(string contextId, string type, string message)
    {
        return new JsonObject() { ["context"] = contextId, ["type"] = type, ["handler"] = "ignore", ["message"] = message, ["userContext"] = "default" };
    }

    private static JsonObject Action(string kind, string cssPath, params JsonField[] details)
    {
        JsonObject action = new()
        {
            ["kind"] = kind,
            ["target"] = new JsonObject() { ["cssPath"] = cssPath, ["labels"] = new JsonArray() },
            ["ancestors"] = new JsonArray(),
        };
        foreach ((string name, JsonNode value) in details)
        {
            action[name] = value;
        }

        return action;
    }

    private static JsonObject Click(string cssPath) => Action("click", cssPath, new JsonField("button", "left"), new JsonField("clickCount", 1), new JsonField("modifiers", new JsonArray()));

    private static JsonObject Fill(string cssPath, string value) => Action("fill", cssPath, new JsonField("value", value));

    private static string[] Statements(string programCode)
    {
        return [.. programCode.Split('\n').SkipWhile(line => !line.StartsWith("Page page = ", StringComparison.Ordinal)).Skip(1).Where(line => line.Length > 0)];
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed record FrameElement(string ContextId, string CssPath);
}
