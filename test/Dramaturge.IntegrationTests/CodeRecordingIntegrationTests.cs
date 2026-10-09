// <copyright file="CodeRecordingIntegrationTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.Text.Json;
using Dramaturge.Browsers;
using Dramaturge.TestUtilities;
using WebDriverBiDi.BrowsingContext;
using WebDriverBiDi.Input;
using static Dramaturge.Assertions;

public class CodeRecordingIntegrationTests
{
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task UsersActionsAreRecordedAsStatements(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(server.UrlFor("recording.html"), cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        List<string> statements = [];
        TaskCompletionSource allSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recording.OnStatement.AddObserver(e =>
        {
            statements.Add(e.Statement);
            if (statements.Count == 4)
            {
                allSettled.TrySetResult();
            }
        });

        await page.Locate(new CssLocator("#go")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#name")).PressSequentiallyAsync("Ada", cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#agree")).CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#done")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Using the toolbar settles the last action, once its messages, sent before, have arrived.
        await ClickToolbarAsync(page, "Record");
        await allSettled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.GetByRole(\"button\", \"Go\").ClickAsync();",
                "await page.GetByRole(\"textbox\", \"Name\").FillAsync(\"Ada\");",
                "await page.GetByRole(\"checkbox\", \"Agree\").CheckAsync();",
                "await page.GetByRole(\"button\", \"Done\").ClickAsync();",
            ],
            statements);
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task ToolbarPicksLocatorsAndAddsAssertions(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(server.UrlFor("recording.html"), cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        List<string> statements = [];
        TaskCompletionSource allSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recording.OnStatement.AddObserver(e =>
        {
            statements.Add(e.Statement);
            if (statements.Count == 9)
            {
                allSettled.TrySetResult();
            }
        });
        TaskCompletionSource<LocatorPickedEventArgs> picked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recording.OnLocatorPicked.AddObserver(e => picked.TrySetResult(e));
        ElementLocator message = page.Locate(new CssLocator("#message"));

        await page.Locate(new CssLocator("#go")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Assert visible");
        await message.ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Assert text");
        await message.ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.Keyboard.PressAsync(Keys.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#name")).PressSequentiallyAsync("Ada", cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Assert value");
        await page.Locate(new CssLocator("#name")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#agree")).CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Assert value");
        await page.Locate(new CssLocator("#agree")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Assert snapshot");
        await page.Locate(new CssLocator("#list > li")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Pick locator");
        await page.Locate(new CssLocator("#go")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Record");
        await page.Locate(new CssLocator("#done")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Record");
        await page.Locate(new CssLocator("#done")).DblClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ClickToolbarAsync(page, "Record");
        await allSettled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        LocatorPickedEventArgs pick = await picked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        // Firefox has no text locator (bug 1869538), so its text candidates are passed over.
        bool hasTextLocator = browserKind != BrowserKind.Firefox;
        string paragraph = hasTextLocator ? "page.GetByText(\"Hello\")" : "page.GetByRole(\"paragraph\")";
        string item = hasTextLocator ? "page.GetByText(\"One\", exact: true)" : "page.GetByRole(\"listitem\")";
        Assert.Equal(
            [
                "await page.GetByRole(\"button\", \"Go\").ClickAsync();",
                $"await Expect({paragraph}).ToBeVisibleAsync();",
                hasTextLocator ? $"await Expect({paragraph}).ToBeVisibleAsync();" : $"await Expect({paragraph}).ToContainTextAsync(\"Hello\");",
                "await page.GetByRole(\"textbox\", \"Name\").FillAsync(\"Ada\");",
                "await Expect(page.GetByRole(\"textbox\", \"Name\")).ToHaveValueAsync(\"Ada\");",
                "await page.GetByRole(\"checkbox\", \"Agree\").CheckAsync();",
                "await Expect(page.GetByRole(\"checkbox\", \"Agree\")).ToBeCheckedAsync();",
                $"await Expect({item}).ToMatchAriaSnapshotAsync(\"\"\"\n    - listitem: One\n    \"\"\");",
                "await page.GetByRole(\"button\", \"Done\").DblClickAsync();",
            ],
            statements);
        Assert.Equal("page.GetByRole(\"button\", \"Go\")", pick.Code);
        Assert.Same(page, pick.Page);
        Assert.Equal(1, await pick.Locator.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await page.EvaluateAsync<double>("() => document.querySelectorAll('dramaturge-toolbar').length", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task EffectsAndFramesAreRecordedWithTheActions(BrowserKind browserKind)
    {
        string folder = Directory.CreateTempSubdirectory("dramaturge-recording-").FullName;
        try
        {
            await using TestPageServer server = await TestPageServer.StartAsync();
            await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
            await group.DefaultBrowser.AllowDownloadsAsync(folder, TestContext.Current.CancellationToken);
            Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
            page.OnDialog.AddObserver(e => e.Dialog.DismissAsync());
            await page.NavigateAsync(server.UrlFor("recording-effects.html"), cancellationToken: TestContext.Current.CancellationToken);
            CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
            List<string> statements = [];
            TaskCompletionSource allSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            recording.OnStatement.AddObserver(e =>
            {
                statements.Add(e.Statement);
                if (statements.Count == 5)
                {
                    allSettled.TrySetResult();
                }
            });
            Frame child = await page.Locate(new CssLocator("#frame")).ContentFrameAsync(cancellationToken: TestContext.Current.CancellationToken);

            await child.Locate(new CssLocator("#inner")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
            await page.Locate(new CssLocator("#confirm")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
            await page.RunAndWaitForDownloadAsync(() => page.Locate(new CssLocator("#report")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);
            await page.RunAndWaitForNavigationAsync(() => page.Locate(new CssLocator("#next")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);

            // The new document's toolbar settles the last action, once the recording has started in it.
            await page.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
            await ClickToolbarAsync(page, "Record");
            await allSettled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await recording.StopAsync(TestContext.Current.CancellationToken);

            // Chrome's role lookup finds nothing in a child frame, so the button there is found by its text.
            string inner = browserKind == BrowserKind.Chrome ? "frame.GetByText(\"Inner\")" : "frame.GetByRole(\"button\", \"Inner\")";
            Assert.Equal(
                [
                    "Frame frame = await page.GetByTitle(\"Child\").ContentFrameAsync();",
                    $"await {inner}.ClickAsync();",
                    "// Opens a dialog (confirm): \"Sure?\"\nawait page.GetByRole(\"button\", \"Confirm\").ClickAsync();",
                    "Download download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(\"link\", \"Report\").ClickAsync());",
                    "await page.RunAndWaitForNavigationAsync(() => page.GetByRole(\"link\", \"Next\").ClickAsync());",
                ],
                statements);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task PopupIsTheResultOfTheActionThatOpenedIt(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();

        // Chrome never answers script commands in a popup (crbug.com/571161446), so recording there waits out this timeout and records nothing.
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind, new DramaturgeOptions() { NavigationTimeout = TimeSpan.FromSeconds(5) });
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(server.UrlFor("recording-effects.html"), cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        bool recordsInPopups = browserKind != BrowserKind.Chrome;
        List<string> statements = [];
        TaskCompletionSource allSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recording.OnStatement.AddObserver(e =>
        {
            statements.Add(e.Statement);
            if (statements.Count == (recordsInPopups ? 2 : 1))
            {
                allSettled.TrySetResult();
            }
        });

        // The next action settles the last; a toolbar button does too, so the one that turns recording off comes last.
        Page popup = await page.RunAndWaitForPopupAsync(() => page.Locate(new CssLocator("#popup")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);
        if (recordsInPopups)
        {
            await popup.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
            await popup.Locate(new CssLocator("#inner")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        await ClickToolbarAsync(recordsInPopups ? popup : page, "Record");

        await allSettled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        string[] expected = ["Page page1 = await page.RunAndWaitForPopupAsync(() => page.GetByRole(\"button\", \"Open\").ClickAsync());"];
        Assert.Equal(recordsInPopups ? [.. expected, "await page1.GetByRole(\"button\", \"Inner\").ClickAsync();"] : expected, statements);
    }

    // A protocol navigation is the browser's own, as typing an address is.
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task NavigationAfterTheActionsOwnIsTheUsers(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        string start = server.UrlFor("recording-navigation.html");
        await page.NavigateAsync(start, cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        List<string> statements = [];
        TaskCompletionSource allSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recording.OnStatement.AddObserver(e =>
        {
            statements.Add(e.Statement);
            if (statements.Count == 8)
            {
                allSettled.TrySetResult();
            }
        });

        async Task TypeAddressAsync(string url)
        {
            await group.Driver.BrowsingContext.NavigateAsync(new NavigateCommandParameters(page.Id, url) { Wait = ReadinessState.Complete }, cancellationToken: TestContext.Current.CancellationToken);
            await page.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
        }

        foreach (string button in new[] { "#location", "#assign", "#replace", "#later" })
        {
            await page.RunAndWaitForNavigationAsync(() => page.Locate(new CssLocator(button)).ClickAsync(cancellationToken: TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);
            await TypeAddressAsync(start);
        }

        await allSettled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        string Waited(string name) => $"await page.RunAndWaitForNavigationAsync(() => page.GetByRole(\"button\", \"{name}\").ClickAsync());";
        string Typed(string url) => $"await page.NavigateAsync(\"{url}\");";
        Assert.Equal(
            [
                Waited("Location"),
                Typed(start),
                Waited("Assign"),
                Typed(start),
                Waited("Replace"),
                Typed(start),
                Waited("Later"),
                Typed(start),
            ],
            statements);
    }

    // The page takes neither a protocol navigation nor a script's selection for the user's, so Dramaturge's calls report themselves.
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task LibraryActionsAreRecordedAsTheirCalls(BrowserKind browserKind)
    {
        string upload = Path.Combine(Directory.CreateTempSubdirectory("dramaturge-recording-").FullName, "upload.txt");
        File.WriteAllText(upload, "upload");
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        string start = server.UrlFor("recording-library.html");
        string second = server.UrlFor("second.html");
        await page.NavigateAsync(start, cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        List<string> statements = [];
        recording.OnStatement.AddObserver(e => statements.Add(e.Statement));

        await page.Locate(new CssLocator("#stay")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(second, cancellationToken: TestContext.Current.CancellationToken);
        await page.GoBackAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.GoForwardAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.GoBackAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.ReloadAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#color")).SelectOptionAsync([SelectOption.ByValue("g")], cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#file")).SetInputFilesAsync([upload], cancellationToken: TestContext.Current.CancellationToken);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "await page.GetByRole(\"button\", \"Stay\").ClickAsync();",
                $"await page.NavigateAsync(\"{second}\");",
                "await page.GoBackAsync();",
                "await page.GoForwardAsync();",
                "await page.GoBackAsync();",
                "await page.ReloadAsync();",
                "await page.GetByRole(\"combobox\", \"Color\").SelectOptionAsync([SelectOption.ByValue(\"g\")]);",
                $"await page.GetByLabel(\"File\").SetInputFilesAsync([{JsonSerializer.Serialize(upload)}]);",
            ],
            statements);
    }

    // Assertions of the kinds the toolbar adds, and the page's URL and title, are recorded once they are met; others are not.
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task MetAssertionsAreRecorded(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        string url = server.UrlFor("recording.html");
        await page.NavigateAsync(url, cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        List<string> statements = [];
        recording.OnStatement.AddObserver(e => statements.Add(e.Statement));
        ElementLocator name = page.Locate(new CssLocator("#name"));
        ElementLocator agree = page.Locate(new CssLocator("#agree"));
        ElementLocator message = page.Locate(new CssLocator("#message"));

        await name.FillAsync("Ada", cancellationToken: TestContext.Current.CancellationToken);
        await Expect(name).ToHaveValueAsync("Ada", cancellationToken: TestContext.Current.CancellationToken);
        await agree.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Expect(agree).ToBeCheckedAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Expect(message).ToBeVisibleAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Expect(message).ToContainTextAsync("Hell", cancellationToken: TestContext.Current.CancellationToken);
        await Expect(page.Locate(new CssLocator("#list"))).ToMatchAriaSnapshotAsync("- listitem: One", cancellationToken: TestContext.Current.CancellationToken);
        await Expect(page).ToHaveTitleAsync("Recording", cancellationToken: TestContext.Current.CancellationToken);
        await Expect(page).ToHaveUrlAsync(url, cancellationToken: TestContext.Current.CancellationToken);
        await Expect(page.Locate(new CssLocator("#go"))).ToBeEnabledAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Expect(message).Not.ToBeHiddenAsync(cancellationToken: TestContext.Current.CancellationToken);
        await agree.UncheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Expect(agree).Not.ToBeCheckedAsync(cancellationToken: TestContext.Current.CancellationToken);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        // Firefox cannot look elements up by their rendered text, so the paragraph is found by its role there.
        string paragraph = browserKind == BrowserKind.Chrome ? "page.GetByText(\"Hello\")" : "page.GetByRole(\"paragraph\")";
        Assert.Equal(
            [
                "await page.GetByRole(\"textbox\", \"Name\").FillAsync(\"Ada\");",
                "await Expect(page.GetByRole(\"textbox\", \"Name\")).ToHaveValueAsync(\"Ada\");",
                "await page.GetByRole(\"checkbox\", \"Agree\").CheckAsync();",
                "await Expect(page.GetByRole(\"checkbox\", \"Agree\")).ToBeCheckedAsync();",
                $"await Expect({paragraph}).ToBeVisibleAsync();",
                browserKind == BrowserKind.Chrome ? $"await Expect({paragraph}).ToBeVisibleAsync();" : $"await Expect({paragraph}).ToContainTextAsync(\"Hell\");",
                "await Expect(page.GetByRole(\"list\")).ToMatchAriaSnapshotAsync(\"\"\"\n    - listitem: One\n    \"\"\");",
                "await Expect(page).ToHaveTitleAsync(\"Recording\");",
                $"await Expect(page).ToHaveUrlAsync(\"{url}\");",
                "await page.GetByRole(\"checkbox\", \"Agree\").UncheckAsync();",
                "await Expect(page.GetByRole(\"checkbox\", \"Agree\")).Not.ToBeCheckedAsync();",
            ],
            statements);
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task ToolbarMovesCollapsesAndKeepsItsLayoutInLaterDocuments(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(server.UrlFor("recording.html"), cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
        ToolbarBox before = await GetToolbarBoxAsync(page);

        await page.Mouse.MoveAsync(before.Left + 13, before.Top + (before.Height / 2), cancellationToken: TestContext.Current.CancellationToken);
        await page.Mouse.DownAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.Mouse.MoveAsync(before.Left + 13 - 40, before.Top + (before.Height / 2) + 200, steps: 5, cancellationToken: TestContext.Current.CancellationToken);
        await page.Mouse.UpAsync(cancellationToken: TestContext.Current.CancellationToken);
        ToolbarBox moved = await GetToolbarBoxAsync(page);
        await page.Mouse.ClickAsync(moved.Left + moved.Width - 17, moved.Top + (moved.Height / 2), cancellationToken: TestContext.Current.CancellationToken);
        ToolbarBox collapsed = await GetToolbarBoxAsync(page);

        // The toolbar's layout reaches the recording as a message, which the next document is started with.
        await page.Locate(new CssLocator("#go")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.ReloadAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
        ToolbarBox reloaded = await GetToolbarBoxAsync(page);
        await page.Mouse.ClickAsync(reloaded.Left + (reloaded.Width / 2), reloaded.Top + (reloaded.Height / 2), cancellationToken: TestContext.Current.CancellationToken);
        ToolbarBox expanded = await GetToolbarBoxAsync(page);
        await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ToolbarBox(before.Left - 40, before.Top + 200, before.Width, before.Height), moved);
        Assert.Equal(moved.Left, collapsed.Left);
        Assert.Equal(moved.Top, collapsed.Top);
        Assert.True(collapsed.Width < 200, $"The collapsed toolbar was {collapsed.Width}px wide.");
        Assert.Equal(collapsed, reloaded);
        Assert.Equal(moved, expanded);
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task RecordingWithoutTheToolbarStillRecords(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(server.UrlFor("recording.html"), cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(new CodeRecordingOptions() { ShowToolbar = false }, TestContext.Current.CancellationToken);

        await page.Locate(new CssLocator("#go")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.ReloadAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.Locate(new CssLocator("#done")).ClickAsync(cancellationToken: TestContext.Current.CancellationToken);
        bool hasToolbar = await page.EvaluateAsync<bool>("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(hasToolbar);
        Assert.Contains("await page.GetByRole(\"button\", \"Go\").ClickAsync();\nawait page.ReloadAsync();\nawait page.GetByRole(\"button\", \"Done\").ClickAsync();\n", code);
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task PasswordsAreMaskedWhenAskedFor(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(server.UrlFor("recording-library.html"), cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(new CodeRecordingOptions() { MaskPasswords = true }, TestContext.Current.CancellationToken);
        ElementLocator password = page.Locate(new CssLocator("#password"));

        await password.FillAsync("s3cret", cancellationToken: TestContext.Current.CancellationToken);
        await Expect(password).ToHaveValueAsync("s3cret", cancellationToken: TestContext.Current.CancellationToken);
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        // ARIA gives a password field no role; Chrome's accessibility lookup finds it as a text box anyway.
        string field = browserKind == BrowserKind.Chrome ? "page.GetByRole(\"textbox\", \"Password\")" : "page.GetByLabel(\"Password\")";
        const string Masked = "Environment.GetEnvironmentVariable(\"DRAMATURGE_PASSWORD\") ?? \"\"";
        Assert.Contains($"await {field}.FillAsync({Masked});\nawait Expect({field}).ToHaveValueAsync({Masked});\n", code);
        Assert.DoesNotContain("s3cret", code);
    }

    // A protocol traversal stands in for the browser's back and forward buttons, which no test can press.
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task UsersTraversalIsRecordedAsBackOrForward(BrowserKind browserKind)
    {
        Assert.SkipWhen(browserKind == BrowserKind.Chrome, "Chrome does not report every traversal of the history to the page, so one may be recorded as NavigateAsync instead.");
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        string start = server.UrlFor("recording.html");
        string second = server.UrlFor("second.html");
        await page.NavigateAsync(start, cancellationToken: TestContext.Current.CancellationToken);
        CodeRecording recording = await group.DefaultBrowser.RecordCodeAsync(cancellationToken: TestContext.Current.CancellationToken);
        async Task TraverseAsync(int delta, string url)
        {
            await page.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
            await group.Driver.BrowsingContext.TraverseHistoryAsync(new TraverseHistoryCommandParameters(page.Id, delta), cancellationToken: TestContext.Current.CancellationToken);
            await page.WaitForUrlAsync(url, cancellationToken: TestContext.Current.CancellationToken);
        }

        await page.NavigateAsync(second, cancellationToken: TestContext.Current.CancellationToken);
        await TraverseAsync(-1, start);
        await TraverseAsync(1, second);
        await page.WaitForFunctionAsync("() => document.querySelector('dramaturge-toolbar') !== null", cancellationToken: TestContext.Current.CancellationToken);
        string code = await recording.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains($"await page.NavigateAsync(\"{second}\");\nawait page.GoBackAsync();\nawait page.GoForwardAsync();\n", code);
    }

    private static async Task<ToolbarBox> GetToolbarBoxAsync(Page page)
    {
        string json = await page.EvaluateAsync<string>("() => JSON.stringify(document.querySelector('dramaturge-toolbar').getBoundingClientRect())", cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument rect = JsonDocument.Parse(json);
        return new ToolbarBox(rect.RootElement.GetProperty("left").GetDouble(), rect.RootElement.GetProperty("top").GetDouble(), rect.RootElement.GetProperty("width").GetDouble(), rect.RootElement.GetProperty("height").GetDouble());
    }

    // The toolbar's bar has a 1px border and 4px padding, around a 16px grip, its six buttons in this order, and a
    // 24px collapse button, 4px apart.
    private static async Task ClickToolbarAsync(Page page, string button)
    {
        string[] buttons = ["Record", "Pick locator", "Assert visible", "Assert text", "Assert value", "Assert snapshot"];
        ToolbarBox box = await GetToolbarBoxAsync(page);
        double width = (box.Width - 10 - 16 - 24 - (7 * 4)) / buttons.Length;
        double left = box.Left + 5 + 16 + 4 + (Array.IndexOf(buttons, button) * (width + 4));
        await page.Mouse.ClickAsync(left + (width / 2), box.Top + (box.Height / 2), cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed record ToolbarBox(double Left, double Top, double Width, double Height);
}
