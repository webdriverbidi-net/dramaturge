// <copyright file="FrontPageTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.Text.Json.Nodes;
using Dramaturge.TestUtilities;
using WebDriverBiDi;
using WebDriverBiDi.BrowsingContext;
using WebDriverBiDi.Input;

// A page behind another is brought to the front before input or a screenshot, since a browser can neither draw nor,
// in Chrome, give input to it; a page the group knows is in front is not.
public class FrontPageTests
{
    [Fact]
    public async Task InputToAPageBehindAnotherBringsItToTheFrontOnce()
    {
        (BiDiDriver driver, FakeSession session, BrowserGroup group) = await ConnectAsync();
        await using BiDiDriver ownedDriver = driver;
        Page first = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        Page second = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);

        await second.Mouse.ClickAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);
        await first.Mouse.ClickAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);
        await first.Keyboard.PressAsync("a", cancellationToken: TestContext.Current.CancellationToken);
        await second.ScreenshotAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([first.Id, second.Id], Activated(session));
    }

    [Fact]
    public async Task PopupComesToTheFrontAndAFrameDoesNot()
    {
        (BiDiDriver driver, FakeSession session, BrowserGroup group) = await ConnectAsync();
        await using BiDiDriver ownedDriver = driver;
        Page opener = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await session.CreateFrameAsync(opener.Id);
        await opener.Mouse.ClickAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);
        await session.RemoteEnd.RaiseEventAsync("browsingContext.contextCreated", new JsonObject() { ["context"] = "popup-1", ["clientWindow"] = "window-1", ["url"] = "about:blank", ["userContext"] = "default", ["children"] = null, ["parent"] = null, ["originalOpener"] = opener.Id });
        await RoundTripAsync(driver);

        await opener.Mouse.ClickAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([opener.Id], Activated(session));
    }

    // Closing the page in front leaves the front unknown; closing another does not.
    [Fact]
    public async Task ClosingThePageInFrontLeavesTheFrontUnknown()
    {
        (BiDiDriver driver, FakeSession session, BrowserGroup group) = await ConnectAsync();
        await using BiDiDriver ownedDriver = driver;
        Page first = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        Page second = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        Page third = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);

        await DestroyAsync(driver, session, first.Id);
        await third.Mouse.ClickAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);
        await DestroyAsync(driver, session, third.Id);
        await second.Mouse.ClickAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([second.Id], Activated(session));
    }

    [Fact]
    public async Task PageBroughtToTheFrontIsNotBroughtAgain()
    {
        (BiDiDriver driver, FakeSession session, BrowserGroup group) = await ConnectAsync();
        await using BiDiDriver ownedDriver = driver;
        Page first = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);

        await first.BringToFrontAsync(TestContext.Current.CancellationToken);
        await first.Mouse.ClickAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([first.Id], Activated(session));
    }

    // A browser does not search the accessibility tree of a page behind another, so a role lookup brings it to the
    // front first; a CSS lookup, which it answers there, does not.
    [Fact]
    public async Task RoleLookupInAPageBehindAnotherBringsItToTheFrontOnce()
    {
        (BiDiDriver driver, FakeSession session, BrowserGroup group) = await ConnectAsync();
        await using BiDiDriver ownedDriver = driver;
        Page first = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);

        await first.Locate(new CssLocator("button")).CountAsync(TestContext.Current.CancellationToken);
        int activatedByCss = Activated(session).Count;
        await first.GetByRole("button").CountAsync(TestContext.Current.CancellationToken);
        await first.GetByRole("button").CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, activatedByCss);
        Assert.Equal([first.Id], Activated(session));
    }

    // A page open before the group connected may be behind another, so input to it brings it to the front.
    [Fact]
    public async Task PageOpenBeforeTheGroupConnectedIsBroughtToTheFront()
    {
        (BiDiDriver driver, FakeSession session) = await FakeSession.ConnectAsync();
        await using BiDiDriver ownedDriver = driver;
        FakeContext existing = session.AddContext();
        BrowserGroup group = await BrowserGroup.ConnectAsync(driver, cancellationToken: TestContext.Current.CancellationToken);
        Page page = group.DefaultBrowser.Pages.Single(candidate => candidate.Id == existing.Id);

        await page.Mouse.MoveAsync(10, 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([existing.Id], Activated(session));
    }

    // An answer comes after the events sent before it, so once one has come, they have been handled.
    private static async Task RoundTripAsync(BiDiDriver driver)
    {
        await driver.BrowsingContext.GetTreeAsync(new WebDriverBiDi.BrowsingContext.GetTreeCommandParameters(), cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task DestroyAsync(BiDiDriver driver, FakeSession session, string contextId)
    {
        await session.RemoteEnd.RaiseEventAsync("browsingContext.contextDestroyed", new JsonObject() { ["context"] = contextId, ["clientWindow"] = "window-1", ["url"] = "about:blank", ["userContext"] = "default", ["children"] = null, ["parent"] = null, ["originalOpener"] = null });
        await RoundTripAsync(driver);
    }

    private static List<string?> Activated(FakeSession session)
    {
        return [.. session.RemoteEnd.CommandsFor("browsingContext.activate").Select(command => (string?)command["params"]!["context"])];
    }

    private static async Task<ConnectedGroup> ConnectAsync()
    {
        (BiDiDriver driver, FakeSession session) = await FakeSession.ConnectAsync();
        session.RemoteEnd.AnswerWith("browsingContext.captureScreenshot", new JsonObject() { ["data"] = Convert.ToBase64String([1, 2, 3]) });
        BrowserGroup group = await BrowserGroup.ConnectAsync(driver, cancellationToken: TestContext.Current.CancellationToken);
        return new ConnectedGroup(driver, session, group);
    }

    private sealed record ConnectedGroup(BiDiDriver Driver, FakeSession Session, BrowserGroup Group);
}
