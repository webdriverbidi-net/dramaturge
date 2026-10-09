// <copyright file="NavigationGateTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.Text.Json.Nodes;
using Dramaturge.TestUtilities;
using WebDriverBiDi;
using WebDriverBiDi.BrowsingContext;

// A page's element lookups and the navigations the library starts on it never overlap. Each test holds one command's
// answer, then checks, after a script's answer has come back, that the other command has not been sent.
public class NavigationGateTests
{
    [Fact]
    public async Task NavigationWaitsForTheLookupsInFlight()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        ElementLocator button = page.Locate(new CssLocator("#go"));
        await button.CountAsync(TestContext.Current.CancellationToken);
        await page.ReloadAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RemoteEnd.HoldAnswers("browsingContext.locateNodes", release.Task);

        Task<int> lookup = button.CountAsync(TestContext.Current.CancellationToken);
        await session.RemoteEnd.WaitForCommandAsync("browsingContext.locateNodes", 2);
        Task<string> reload = page.ReloadAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await RoundTripAsync(page);
        int reloadsWhileLooking = session.RemoteEnd.CommandsFor("browsingContext.reload").Count;
        release.SetResult();
        await lookup;
        await reload;

        Assert.Equal(1, reloadsWhileLooking);
        Assert.Equal(2, session.RemoteEnd.CommandsFor("browsingContext.reload").Count);
    }

    [Fact]
    public async Task LookupWaitsForANavigationInProgress()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RemoteEnd.HoldAnswers("browsingContext.traverseHistory", release.Task);
        session.RemoteEnd.AnswerWith("browsingContext.traverseHistory", parameters => new FakeResponse(new JsonObject(), [new FakeEvent("browsingContext.historyUpdated", new JsonObject() { ["context"] = (string)parameters["context"]!, ["timestamp"] = 1790000000000, ["url"] = "https://example.com/back", ["userContext"] = "default" })]));

        Task<string> back = page.GoBackAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await session.RemoteEnd.WaitForCommandAsync("browsingContext.traverseHistory");
        Task<int> lookup = page.Locate(new CssLocator("#go")).CountAsync(TestContext.Current.CancellationToken);
        await RoundTripAsync(page);
        int lookupsWhileNavigating = session.RemoteEnd.CommandsFor("browsingContext.locateNodes").Count;
        release.SetResult();
        await back;
        await lookup;

        Assert.Equal(0, lookupsWhileNavigating);
        Assert.Single(session.RemoteEnd.CommandsFor("browsingContext.locateNodes"));
    }

    [Fact]
    public async Task NavigationWaitsForAnotherInProgress()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RemoteEnd.HoldAnswers("browsingContext.navigate", release.Task);

        Task<string> navigation = page.NavigateAsync("https://example.com/next", ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await session.RemoteEnd.WaitForCommandAsync("browsingContext.navigate");
        Task<string> reload = page.ReloadAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await RoundTripAsync(page);
        int reloadsWhileNavigating = session.RemoteEnd.CommandsFor("browsingContext.reload").Count;
        release.SetResult();
        await navigation;
        await reload;

        Assert.Equal(0, reloadsWhileNavigating);
        Assert.Single(session.RemoteEnd.CommandsFor("browsingContext.reload"));
    }

    // A lookup in a document the page has since replaced, which Chrome may never answer, holds no navigation back.
    [Fact]
    public async Task CommittedDocumentReleasesTheNavigationsWaitingForItsLookups()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RemoteEnd.HoldAnswers("browsingContext.locateNodes", release.Task);

        Task<int> lookup = page.Locate(new CssLocator("#go")).CountAsync(TestContext.Current.CancellationToken);
        await session.RemoteEnd.WaitForCommandAsync("browsingContext.locateNodes");
        Task<string> reload = page.ReloadAsync(ReadinessState.None, cancellationToken: TestContext.Current.CancellationToken);
        await session.RaiseNavigationEventAsync("browsingContext.navigationCommitted", page.Id, "https://example.com/elsewhere");
        await reload;
        bool lookupStillPending = !lookup.IsCompleted;
        release.SetResult();
        await lookup;

        Assert.True(lookupStillPending);
        Assert.Single(session.RemoteEnd.CommandsFor("browsingContext.reload"));
    }

    [Fact]
    public async Task NavigationThatWaitsTooLongForTheLookupsTimesOut()
    {
        (BiDiDriver driver, FakeSession session, Page page) = await OpenPageAsync();
        await using BiDiDriver ownedDriver = driver;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RemoteEnd.HoldAnswers("browsingContext.locateNodes", release.Task);

        Task<int> lookup = page.Locate(new CssLocator("#go")).CountAsync(TestContext.Current.CancellationToken);
        await session.RemoteEnd.WaitForCommandAsync("browsingContext.locateNodes");
        WebDriverBiDiTimeoutException exception = await Assert.ThrowsAsync<WebDriverBiDiTimeoutException>(() => page.ReloadAsync(ReadinessState.None, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        release.SetResult();
        await lookup;

        Assert.Contains("the page's element lookups to finish", exception.Message);
        Assert.Empty(session.RemoteEnd.CommandsFor("browsingContext.reload"));
    }

    private static async Task RoundTripAsync(Page page)
    {
        await page.EvaluateAsync<object?>("() => null", cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<OpenedPage> OpenPageAsync()
    {
        (BiDiDriver driver, FakeSession session) = await FakeSession.ConnectAsync();
        BrowserGroup group = await BrowserGroup.ConnectAsync(driver, cancellationToken: TestContext.Current.CancellationToken);
        session.RemoteEnd.AnswerWith("script.callFunction", _ => new FakeResponse(new JsonObject() { ["type"] = "success", ["result"] = new JsonObject() { ["type"] = "null" }, ["realm"] = "realm-1" }));
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        return new OpenedPage(driver, session, page);
    }

    private sealed record OpenedPage(BiDiDriver Driver, FakeSession Session, Page Page);
}
