// <copyright file="CodeRecording.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using WebDriverBiDi;
using WebDriverBiDi.BrowsingContext;
using WebDriverBiDi.Script;
using WebDriverBiDi.Session;

/// <summary>
/// The recording of the actions a user takes in a browser's pages as C# code. Each action's statement settles when
/// the next action is recorded, or when the recording stops, so that typing into a field is one fill and the
/// clicks of a double click are one. Disposing it, or <see cref="StopAsync"/>, ends it.
/// </summary>
public sealed class CodeRecording : IAsyncDisposable
{
    private readonly Browser browser;
    private readonly CodeTarget target;
    private readonly bool showToolbar;
    private readonly bool maskPasswords;
    private readonly string channelId = $"dramaturge-code-{Guid.NewGuid():N}";
    private readonly object lockObject = new();
    private readonly List<IDisposable> observers = [];
    private readonly List<Task> documentCalls = [];
    private readonly List<string> statements = [];
    private readonly HashSet<string> namespaces = [];
    private readonly Dictionary<Page, string> pageVariables = [];
    private readonly Dictionary<Frame, string> frameVariables = [];
    private readonly HashSet<Page> actedPages = [];
    private readonly Dictionary<Page, StartedNavigation> navigations = [];
    private readonly Dictionary<Page, Dictionary<string, int?>> reportedNavigations = [];
    private readonly Dictionary<Frame, CancellationTokenSource> lookupSources = [];
    private readonly Dictionary<Page, string> libraryNavigations = [];
    private readonly ObservableEventInvocable<CodeStatementEventArgs> onStatement = new("automation.codeStatement");
    private readonly ObservableEventInvocable<CodeStatementEventArgs> onPendingStatement = new("automation.codePendingStatement");
    private readonly ObservableEventInvocable<LocatorPickedEventArgs> onLocatorPicked = new("automation.locatorPicked");
    private readonly SemaphoreSlim stopLock = new(1, 1);
    private Task processing = Task.CompletedTask;
    private RecordedStep? pending;
    private string? subscriptionId;
    private RememberedLocator? lastLocator;
    private string mode = "record";
    private string? toolbarLayout;
    private int downloadCount;
    private int stepCount;
    private bool isStopped;

    private CodeRecording(Browser browser, CodeRecordingOptions options)
    {
        this.browser = browser;
        this.target = options.Target;
        this.showToolbar = options.ShowToolbar;
        this.maskPasswords = options.MaskPasswords;
    }

    /// <summary>
    /// Gets the whole file for the recording's target, built from the statements settled so far and the one still
    /// open, as it is now.
    /// </summary>
    public string Code => this.Write(this.target);

    /// <summary>
    /// Gets an event raised with each statement once it settles, in order.
    /// </summary>
    public ObservableEvent<CodeStatementEventArgs> OnStatement => this.onStatement;

    /// <summary>
    /// Gets an event raised with the statement still open, each time it starts or changes: a fill that grows as the
    /// user types, or an action that a navigation, popup, download, or dialog then wraps or annotates. The same
    /// statement is raised by <see cref="OnStatement"/> once it settles.
    /// </summary>
    public ObservableEvent<CodeStatementEventArgs> OnPendingStatement => this.onPendingStatement;

    /// <summary>
    /// Gets an event raised with each element the user picks with the toolbar's Pick locator.
    /// </summary>
    public ObservableEvent<LocatorPickedEventArgs> OnLocatorPicked => this.onLocatorPicked;

    /// <summary>
    /// Gets the whole file for a target, which can differ from the recording's, once what the browser has reported so
    /// far is recorded: the statements settled, and the one still open.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>The file.</returns>
    /// <remarks>Each report is recorded within the recording's own time limits, as when it stops.</remarks>
    public async Task<string> GetCodeAsync(CodeTarget target)
    {
        await this.ProcessReceivedAsync().ConfigureAwait(false);
        return this.Write(target);
    }

    /// <summary>
    /// Ends the recording: settles the last statement, and stops recording in the browser's documents. Stopping
    /// again does nothing.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the commands that stop recording in the documents.</param>
    /// <returns>The whole file, as <see cref="Code"/> gives it.</returns>
    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        await this.stopLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.isStopped)
            {
                return this.Code;
            }

            Task processed;
            lock (this.lockObject)
            {
                this.isStopped = true;
                foreach (IDisposable observer in this.observers)
                {
                    observer.Dispose();
                }

                processed = this.processing;
            }

            await processed.ConfigureAwait(false);
            Task[] calls;
            lock (this.lockObject)
            {
                calls = [.. this.documentCalls];
            }

            await Task.WhenAll(calls).ConfigureAwait(false);
            await this.RunStepAsync("Removing the code recording's event subscription", () => this.browser.Group.Driver.Session.UnsubscribeAsync(new UnsubscribeByIdsCommandParameters(this.subscriptionId!), cancellationToken: cancellationToken)).ConfigureAwait(false);
            foreach (Frame frame in this.browser.Pages.SelectMany(page => page.Frames))
            {
                await this.RunStepAsync($"Stopping the code recording in browsing context {frame.Id}", () => this.browser.Group.ScriptHost.CallRecorderAsync(frame.Id, "(recorder) => recorder.stop()", [], this.CreateBudget(cancellationToken))).ConfigureAwait(false);
            }

            RecordedStep? last;
            lock (this.lockObject)
            {
                last = this.pending;
                this.pending = null;
            }

            if (last is not null)
            {
                await this.SettleAsync(last).ConfigureAwait(false);
            }

            this.browser.ReleaseCodeRecording(this);
            return this.Code;
        }
        finally
        {
            this.stopLock.Release();
        }
    }

    /// <summary>
    /// Ends the recording, as <see cref="StopAsync"/> does.
    /// </summary>
    /// <returns>A task that completes when the recording has ended.</returns>
    public async ValueTask DisposeAsync()
    {
        await this.StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Starts recording a browser's actions as code, in the documents already loaded before returning.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="options">The recording's settings, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token that cancels starting.</param>
    /// <returns>The recording.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the browser is already recording code.</exception>
    internal static async Task<CodeRecording> StartAsync(Browser browser, CodeRecordingOptions? options, CancellationToken cancellationToken)
    {
        CodeRecording recording = new(browser, options ?? new CodeRecordingOptions());
        if (!browser.ClaimCodeRecording(recording))
        {
            throw new InvalidOperationException("The browser is already recording code.");
        }

        BiDiDriver driver = browser.Group.Driver;
        lock (recording.lockObject)
        {
            recording.observers.Add(driver.Script.OnMessage.AddObserver(recording.OnMessage));
            recording.observers.Add(driver.BrowsingContext.OnDomContentLoaded.AddObserver(e => recording.OnDocumentLoaded(e.BrowsingContextId)));
            recording.observers.Add(driver.BrowsingContext.OnNavigationStarted.AddObserver(e => recording.OnPageEvent(e.BrowsingContextId, frame => frame == frame.Page.MainFrame && e.Url != "about:blank", page => recording.OnNavigationStartedAsync(page, e.NavigationId, e.Url))));
            recording.observers.Add(driver.BrowsingContext.OnNavigationCommitted.AddObserver(e => recording.CancelLookups(e.BrowsingContextId)));
            recording.observers.Add(driver.BrowsingContext.OnNavigationCommitted.AddObserver(e => recording.OnPageEvent(e.BrowsingContextId, frame => frame == frame.Page.MainFrame, page => recording.OnNavigationCommittedAsync(page, e.NavigationId))));
            recording.observers.Add(driver.BrowsingContext.OnDownloadWillBegin.AddObserver(e => recording.OnPageEvent(e.BrowsingContextId, _ => true, page => recording.OnDownloadAsync(page, e.NavigationId))));
            recording.observers.Add(driver.BrowsingContext.OnUserPromptOpened.AddObserver(e => recording.OnPageEvent(e.BrowsingContextId, _ => true, page => recording.OnDialogAsync(page, e.PromptType, e.Message))));
            recording.observers.Add(browser.OnPageCreated.AddObserver(e => recording.Enqueue(() => recording.OnPopupAsync(e.Page))));
            recording.observers.Add(browser.OnPageClosed.AddObserver(e => recording.Enqueue(() => recording.OnPageClosedAsync(e.Page))));
        }

        try
        {
            // Subscribing before installing in the documents already loaded, so that none of their messages is missed.
            SubscribeCommandParameters subscription = new([driver.Script.OnMessage.EventName], userContexts: [browser.Id]);
            recording.subscriptionId = (await driver.Session.SubscribeAsync(subscription, cancellationToken: cancellationToken).ConfigureAwait(false)).SubscriptionId;
        }
        catch
        {
            lock (recording.lockObject)
            {
                recording.isStopped = true;
                recording.observers.ForEach(observer => observer.Dispose());
            }

            browser.ReleaseCodeRecording(recording);
            throw;
        }

        await Task.WhenAll(browser.Pages.SelectMany(page => page.Frames).Select(frame => recording.Install(frame.Id))).ConfigureAwait(false);
        return recording;
    }

    /// <summary>
    /// Runs the library's own navigation of a frame, which, in the main frame of a browser recording code, is
    /// recorded as its call, and none of the navigations it starts is an action's.
    /// </summary>
    /// <typeparam name="T">The type of the navigation's result.</typeparam>
    /// <param name="frame">The frame.</param>
    /// <param name="call">The call, after the page's variable, such as <c>ReloadAsync()</c>.</param>
    /// <param name="navigate">The navigation.</param>
    /// <returns>The navigation's result.</returns>
    internal static Task<T> RunNavigationAsync<T>(Frame frame, string call, Func<Task<T>> navigate)
    {
        return frame == frame.Page.MainFrame && frame.Page.Browser.ActiveCodeRecording is CodeRecording recording ? recording.RecordNavigationAsync(frame.Page, call, navigate) : navigate();
    }

    /// <summary>
    /// Records an assertion on a page of a browser recording code, once it is met.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="call">The call, after <c>Expect(page).</c>, such as <c>ToHaveTitleAsync("Home")</c>.</param>
    internal static void RecordPageAssertion(Page page, string call)
    {
        if (page.Browser.ActiveCodeRecording is CodeRecording recording)
        {
            recording.Enqueue(() => recording.ChangeAsync(settled =>
            {
                settled.AddRange(recording.TakePending());
                settled.Add(new($"await Expect({recording.GetPageVariable(page, settled)}).{call};", [CodeWriter.AssertionsNamespace]));
            }));
        }
    }

    private static bool IsWithin(Frame frame, Frame ancestor)
    {
        for (Frame? current = frame; current is not null; current = current.ParentFrame)
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPasswordField(JsonElement facts)
    {
        return facts.TryGetProperty("tagName", out JsonElement tagName) && tagName.GetString() == "input"
            && facts.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String && string.Equals(type.GetString()!.Trim(), "password", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] LocatorNamespaces(string locator) => locator.Contains("new CssLocator(") ? [CodeWriter.BrowsingContextNamespace] : [];

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    private static string Capitalize(string name) => char.ToUpperInvariant(name[0]) + name.Substring(1);

    // A step for an action, or null for an action the recording does not know. A password field's value is written as
    // the masked password when passwords are masked. Text the locator already finds the element by is not asserted
    // again: the element being visible is the assertion.
    private static RecordedStep? CreateStep(JsonElement action, string locator, string? matchedText, bool maskPasswords)
    {
        bool masked = maskPasswords && IsPasswordField(action.GetProperty("target"));
        string Value(string property) => masked ? CodeWriter.MaskedPassword : CodeWriter.Literal(action.GetProperty(property).GetString()!);
        string[] locatorNamespaces = masked ? [.. LocatorNamespaces(locator), CodeWriter.SystemNamespace] : LocatorNamespaces(locator);
        string[] assertionNamespaces = [.. locatorNamespaces, CodeWriter.AssertionsNamespace];
        RecordedStep Action(string call, IReadOnlyList<string> namespaces, string? mergeKey = null, int clickCount = 0) => new($"await {locator}.{call};", namespaces, mergeKey, clickCount) { Call = $"{locator}.{call}" };
        RecordedStep Assertion(string call) => new($"await Expect({locator}).{call};", assertionNamespaces, SettlesAtOnce: true);
        string? Modifiers() => CodeWriter.Modifiers(Strings(action.GetProperty("modifiers")));

        switch (action.GetProperty("kind").GetString())
        {
            case "click":
                string button = Capitalize(action.GetProperty("button").GetString()!);
                int clickCount = action.GetProperty("clickCount").GetInt32();
                string? modifiers = Modifiers();
                string options = CodeWriter.Options(
                    button == "Left" ? null : $"Button = PointerButton.{button}",
                    clickCount > 2 ? $"ClickCount = {clickCount}" : null,
                    modifiers is null ? null : $"Modifiers = {modifiers}");
                string method = clickCount == 2 ? "DblClickAsync" : "ClickAsync";
                return Action($"{method}({options})", button == "Left" ? locatorNamespaces : [.. locatorNamespaces, CodeWriter.InputNamespace], $"click {locator} {button} {modifiers}", clickCount);
            case "check":
                return Action("CheckAsync()", locatorNamespaces);
            case "uncheck":
                return Action("UncheckAsync()", locatorNamespaces);
            case "fill":
                return Action($"FillAsync({Value("value")})", locatorNamespaces, $"fill {locator}");
            case "press":
                string pressed = action.GetProperty("key").GetString()!;
                string? key = CodeWriter.Key(pressed);
                if (key is null)
                {
                    return new($"// The key {CodeWriter.Literal(pressed)} was pressed, which is neither a member of Keys nor a character.", []);
                }

                string pressOptions = CodeWriter.Options(Modifiers() is string held ? $"Modifiers = {held}" : null);
                return Action($"PressAsync({key}{(pressOptions.Length == 0 ? string.Empty : ", " + pressOptions)})", key.StartsWith("Keys.", StringComparison.Ordinal) ? [.. locatorNamespaces, CodeWriter.InputNamespace] : locatorNamespaces);
            case "select":
                return Action($"SelectOptionAsync({CodeWriter.Collection(Strings(action.GetProperty("values")).Select(value => $"SelectOption.ByValue({CodeWriter.Literal(value)})"))})", locatorNamespaces);
            case "setInputFiles":
                RecordedStep files = Action($"SetInputFilesAsync({CodeWriter.Collection(Strings(action.GetProperty("files")).Select(CodeWriter.Literal))})", locatorNamespaces, $"files {locator}");
                return action.TryGetProperty("paths", out _) ? files : files with { Note = "// The page knows only the files' names; replace each with the file's path." };
            case "assertVisible":
                return Assertion("ToBeVisibleAsync()");
            case "assertText":
                string text = action.GetProperty("text").GetString()!;
                return Assertion(matchedText?.Contains(text) == true ? "ToBeVisibleAsync()" : $"ToContainTextAsync({CodeWriter.Literal(text)})");
            case "assertValue":
                return Assertion($"ToHaveValueAsync({Value("value")})");
            case "assertChecked":
                return Assertion(action.GetProperty("checked").GetBoolean() ? "ToBeCheckedAsync()" : "Not.ToBeCheckedAsync()");
            case "assertSnapshot":
                return Assertion($"ToMatchAriaSnapshotAsync({CodeWriter.RawLiteral(action.GetProperty("snapshot").GetString()!)})");
            default:
                return null;
        }
    }

    // Completes once what the browser has reported so far is recorded.
    private Task ProcessReceivedAsync()
    {
        lock (this.lockObject)
        {
            return this.processing;
        }
    }

    // The whole file for a target, from the statements settled so far and the one still open.
    private string Write(CodeTarget target)
    {
        lock (this.lockObject)
        {
            BrowserGroup group = this.browser.Group;
            return this.pending is RecordedStep open
                ? CodeWriter.File(target, group.BrowserName, group.Options.TestIdAttribute, [.. this.statements, open.Text], this.namespaces.Union(open.Namespaces))
                : CodeWriter.File(target, group.BrowserName, group.Options.TestIdAttribute, this.statements, this.namespaces);
        }
    }

    private void OnDocumentLoaded(string contextId)
    {
        if (this.browser.Group.FindFrame(contextId)?.Page.Browser == this.browser)
        {
            this.InstallUnlessStopped(contextId);
        }
    }

    [ExcludeFromCodeCoverage] // Only a document loaded as the recording stops finds it stopped.
    private void InstallUnlessStopped(string contextId)
    {
        lock (this.lockObject)
        {
            if (!this.isStopped)
            {
                this.documentCalls.Add(this.Install(contextId));
            }
        }
    }

    // Starts recording in a document, once its scripts are installed; a document that closes first is not recorded.
    private Task Install(string contextId)
    {
        ChannelValue channel = new(new ChannelProperties(this.channelId) { SerializationOptions = new SerializationOptions() { MaxDomDepth = 0 }, Ownership = ResultOwnership.None });
        string current;
        string? layout;
        lock (this.lockObject)
        {
            current = this.mode;
            layout = this.toolbarLayout;
        }

        return this.RunStepAsync(
            $"Starting the code recording in browsing context {contextId}",
            () => this.browser.Group.ScriptHost.CallRecorderAsync(contextId, "(recorder, send, testIdAttribute, mode, showToolbar, layout) => recorder.start(send, testIdAttribute, mode, showToolbar, layout)", [channel, LocalValue.String(this.browser.Group.Options.TestIdAttribute), LocalValue.String(current), LocalValue.Boolean(this.showToolbar), layout is null ? LocalValue.Null : LocalValue.String(layout)], this.CreateBudget(CancellationToken.None)));
    }

    // The sender's frame is found as the message arrives: its page may close before the message is handled.
    private void OnMessage(MessageEventArgs e)
    {
        Frame? frame = e.Source.BrowsingContextId is string contextId ? this.browser.Group.FindFrame(contextId) : null;
        if (e.ChannelId == this.channelId && frame?.Page.Browser == this.browser)
        {
            RemoteValueList data = e.Data.As<CollectionRemoteValue>().Value!;
            if (!this.TakeToolbarLayout(data))
            {
                CancellationToken lookups = this.GetLookupToken(frame);
                this.Enqueue(() => this.HandleAsync(frame, data, lookups));
            }
        }
    }

    // An event in one of the browser's frames that satisfies a condition is handled for the frame's page.
    private void OnPageEvent(string contextId, Func<Frame, bool> condition, Func<Page, Task> handle)
    {
        if (this.browser.Group.FindFrame(contextId) is Frame frame && frame.Page.Browser == this.browser && condition(frame))
        {
            this.Enqueue(() => handle(frame.Page));
        }
    }

    // Handles messages and events one at a time, in the order they arrive.
    private void Enqueue(Func<Task> handle)
    {
        lock (this.lockObject)
        {
            if (!this.isStopped)
            {
                this.processing = this.processing.ContinueWith(_ => this.RunStepAsync("Recording a message or an event", handle), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            }
        }
    }

    private async Task HandleAsync(Frame frame, RemoteValueList data, CancellationToken lookups)
    {
        using JsonDocument document = JsonDocument.Parse(data[0].As<StringRemoteValue>().Value);
        JsonElement message = document.RootElement;
        string kind = message.GetProperty("kind").GetString()!;
        if (kind == "mode")
        {
            await this.ChooseModeAsync(frame, message.GetProperty("mode").GetString()!).ConfigureAwait(false);
            return;
        }

        if (kind == "navigate")
        {
            this.OnNavigationReported(frame, message.GetProperty("url").GetString()!, message.TryGetProperty("steps", out JsonElement steps) && steps.ValueKind == JsonValueKind.Number ? steps.GetInt32() : null);
            return;
        }

        RecordedStep? before;
        lock (this.lockObject)
        {
            before = this.pending;
        }

        if (await this.DescribeFramesAsync(frame, lookups).ConfigureAwait(false) is not List<FrameStep> frames)
        {
            return;
        }

        GeneratedLocator found = await this.FindLocatorAsync(frame, message, [.. data.Skip(1).Select(element => element.As<NodeRemoteValue>())], lookups).ConfigureAwait(false);
        List<RecordedStep> settled = [];
        LocatorPickedEventArgs? picked = null;
        lock (this.lockObject)
        {
            this.actedPages.Add(frame.Page);
            string pageVariable = this.GetPageVariable(frame.Page, settled);
            string locator = $"{this.DeclareFrames(frame, frames, pageVariable, settled)}.{found.Code}";
            switch (kind)
            {
                case "pick":
                    settled.AddRange(this.TakePending());
                    picked = new LocatorPickedEventArgs(frame.Page, found.Locator, locator);
                    break;
                default:
                    if (CreateStep(message, locator, found.MatchedText, this.maskPasswords) is RecordedStep created)
                    {
                        RecordedStep step = created with { Page = frame.Page, PageVariable = pageVariable, Number = ++this.stepCount };
                        if (step.SettlesAtOnce || this.pending is null || !step.Replaces(this.pending))
                        {
                            settled.AddRange(this.TakePending());
                        }

                        if (step.SettlesAtOnce)
                        {
                            settled.Add(step);
                        }
                        else
                        {
                            this.pending = step;
                        }
                    }

                    break;
            }
        }

        await this.PublishAsync(settled, before).ConfigureAwait(false);
        if (picked is not null)
        {
            await this.onLocatorPicked.InvokeNotifyObserversAsync(picked).ConfigureAwait(false);
        }
    }

    // The navigation's window opens before its command is sent and closes once it completes, in the order of the
    // events, so each navigation that starts in it is the library's; the call is recorded as the window closes, as a
    // navigation within the document starts none.
    // What the browser reported before the navigation, such as the action that preceded it, is recorded first, so that
    // looking up its elements does not overlap the navigation.
    private async Task<T> RecordNavigationAsync<T>(Page page, string call, Func<Task<T>> navigate)
    {
        this.Enqueue(() => this.ChangeAsync(_ => this.libraryNavigations[page] = call));
        await this.ProcessReceivedAsync().ConfigureAwait(false);
        bool succeeded = false;
        try
        {
            T result = await navigate().ConfigureAwait(false);
            succeeded = true;
            return result;
        }
        finally
        {
            this.Enqueue(() => this.ChangeAsync(settled =>
            {
                this.libraryNavigations.Remove(page);
                if (succeeded)
                {
                    settled.AddRange(this.TakePending());
                    settled.Add(new($"await {this.GetPageVariable(page, settled)}.{call};", []));
                }
            }));
        }
    }

    // The token that cancels an action's lookups in its frame's document, taken as its message arrives, which is
    // before any navigation replacing the document that sent it commits.
    private CancellationToken GetLookupToken(Frame frame)
    {
        lock (this.lockObject)
        {
            if (!this.lookupSources.TryGetValue(frame, out CancellationTokenSource? source))
            {
                source = new CancellationTokenSource();
                this.lookupSources[frame] = source;
            }

            return source.Token;
        }
    }

    // A navigation that commits cancels the lookups in the documents it replaced, its frame's and those of the frames
    // within it, which would otherwise look in the new document, or, in Chrome, may never be answered.
    private void CancelLookups(string contextId)
    {
        if (this.browser.Group.FindFrame(contextId) is not Frame navigated)
        {
            return;
        }

        List<CancellationTokenSource> cancelled = [];
        lock (this.lockObject)
        {
            foreach (Frame frame in this.lookupSources.Keys.Where(frame => IsWithin(frame, navigated)).ToList())
            {
                cancelled.Add(this.lookupSources[frame]);
                this.lookupSources.Remove(frame);
            }
        }

        cancelled.ForEach(source => source.Cancel());
    }

    // The toolbar's latest position and state are where it starts in each document that loads later, so its message
    // is taken as it arrives, before any later document's load is handled.
    private bool TakeToolbarLayout(RemoteValueList data)
    {
        string json = data[0].As<StringRemoteValue>().Value;
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("kind").GetString() != "toolbar")
        {
            return false;
        }

        lock (this.lockObject)
        {
            this.toolbarLayout = json;
        }

        return true;
    }

    // A document reports its navigation before the navigation commits, though not always before it starts.
    // A traversal of the page's history is reported with its steps, negative for back.
    private void OnNavigationReported(Frame frame, string url, int? steps)
    {
        lock (this.lockObject)
        {
            if (frame == frame.Page.MainFrame)
            {
                if (!this.reportedNavigations.TryGetValue(frame.Page, out Dictionary<string, int?>? reports))
                {
                    reports = [];
                    this.reportedNavigations[frame.Page] = reports;
                }

                reports[url] = steps;
            }
        }
    }

    // A page's latest navigation replaces any earlier one that has not committed.
    private Task OnNavigationStartedAsync(Page page, string? navigationId, string url)
    {
        lock (this.lockObject)
        {
            int? actionNumber = this.pending is { Call: not null } action && action.Page == page ? action.Number : null;
            this.navigations[page] = new StartedNavigation(navigationId, url, actionNumber, this.libraryNavigations.ContainsKey(page));
        }

        return Task.CompletedTask;
    }

    // A navigation is the effect of the action pending when it started, if that action is still pending and has caused
    // none yet. Otherwise its document's report, which Chrome sometimes loses, tells the page's own navigation, which
    // replay repeats, from the user's, as when typing an address; a popup's first, before it is acted in, is its
    // opener's. A reported traversal of the history is the user's, by the browser's back or forward, since a page
    // rarely traverses its history without an action; a step back or forward is written as one.
    private Task OnNavigationCommittedAsync(Page page, string? navigationId)
    {
        return this.ChangeAsync(settled =>
        {
            if (!this.navigations.TryGetValue(page, out StartedNavigation? started) || started.NavigationId != navigationId)
            {
                return;
            }

            this.navigations.Remove(page);
            int? steps = null;
            bool reported = this.reportedNavigations.TryGetValue(page, out Dictionary<string, int?>? reports) && reports.TryGetValue(started.Url, out steps);
            this.reportedNavigations.Remove(page);
            if (started.Library)
            {
                return;
            }

            if (this.pending is { Call: not null, Navigated: false } action && action.Number == started.ActionNumber)
            {
                this.pending = action.WithEffect(null, "RunAndWaitForNavigationAsync") with { Navigated = true };
            }
            else if ((!reported || steps is not null) && (page.Opener is null || this.actedPages.Contains(page)))
            {
                string call = steps switch
                {
                    -1 => "GoBackAsync()",
                    1 => "GoForwardAsync()",
                    _ => $"NavigateAsync({CodeWriter.Literal(started.Url)})",
                };
                settled.AddRange(this.TakePending());
                settled.Add(new($"await {this.GetPageVariable(page, settled)}.{call};", []));
            }
        });
    }

    private Task OnPopupAsync(Page popup)
    {
        return this.ChangeAsync(_ =>
        {
            if (this.pending is { Call: not null } action && action.Page == popup.Opener && !action.HasEffect)
            {
                this.pending = action.WithEffect($"Page {this.NameVariable(popup)} = ", "RunAndWaitForPopupAsync");
            }
        });
    }

    // A navigation that becomes a download never commits.
    private Task OnDownloadAsync(Page page, string? navigationId)
    {
        return this.ChangeAsync(_ =>
        {
            if (this.navigations.TryGetValue(page, out StartedNavigation? started) && started.NavigationId == navigationId)
            {
                this.navigations.Remove(page);
            }

            if (this.pending is { Call: not null } action && action.Page == page && !action.HasEffect)
            {
                string variable = this.downloadCount++ == 0 ? "download" : $"download{this.downloadCount - 1}";
                this.pending = action.WithEffect($"Download {variable} = ", "RunAndWaitForDownloadAsync");
            }
        });
    }

    // A dialog is noted; the browser handles it as the session's prompt behaviour says, on replay as when recording.
    private Task OnDialogAsync(Page page, UserPromptType type, string message)
    {
        string kind = type.ToString().ToLowerInvariant();
        return this.ChangeAsync(settled =>
        {
            if (this.pending is { Call: not null } action && action.Page == page)
            {
                this.pending = action with { Comments = [.. action.Comments, $"// Opens a dialog ({kind}): {CodeWriter.Literal(message)}"] };
            }
            else
            {
                settled.AddRange(this.TakePending());
                settled.Add(new($"// A dialog opened ({kind}): {CodeWriter.Literal(message)}", []));
            }
        });
    }

    // Closing the first page ends the session, rather than being a step of it.
    private Task OnPageClosedAsync(Page page)
    {
        return this.ChangeAsync(settled =>
        {
            if (this.pageVariables.TryGetValue(page, out string? variable) && variable != "page")
            {
                settled.AddRange(this.TakePending());
                settled.Add(new($"await {variable}.CloseAsync();", []));
            }
        });
    }

    // Changes the pending and settled statements under the lock, then settles those it settled.
    private async Task ChangeAsync(Action<List<RecordedStep>> change)
    {
        List<RecordedStep> settled = [];
        RecordedStep? before;
        lock (this.lockObject)
        {
            before = this.pending;
            change(settled);
        }

        await this.PublishAsync(settled, before).ConfigureAwait(false);
    }

    // Settles the steps, then reports the open statement if it started or changed since it was the one before.
    private async Task PublishAsync(List<RecordedStep> settled, RecordedStep? before)
    {
        foreach (RecordedStep step in settled)
        {
            await this.SettleAsync(step).ConfigureAwait(false);
        }

        RecordedStep? after;
        lock (this.lockObject)
        {
            after = this.pending;
        }

        if (after is not null && (before is null || before.Number != after.Number || before.Text != after.Text))
        {
            await this.onPendingStatement.InvokeNotifyObserversAsync(new CodeStatementEventArgs(after.Text)).ConfigureAwait(false);
        }
    }

    // The frames from the page's main frame down to a frame that have no variable yet, each with its element's
    // locator in its parent, or null when a frame's element cannot be found, as in a shadow root.
    private async Task<List<FrameStep>?> DescribeFramesAsync(Frame frame, CancellationToken lookups)
    {
        List<FrameStep> frames = [];
        for (Frame current = frame; current.ParentFrame is Frame parent; current = parent)
        {
            lock (this.lockObject)
            {
                if (this.frameVariables.ContainsKey(current))
                {
                    break;
                }
            }

            if (await this.FindFrameElementAsync(parent, current, lookups).ConfigureAwait(false) is not string code)
            {
                await this.browser.Group.LogAsync($"An action in browsing context {frame.Id} was not recorded: the element of frame {current.Id} was not found in its parent.", WebDriverBiDiLogLevel.Warn).ConfigureAwait(false);
                return null;
            }

            frames.Insert(0, new FrameStep(current, code));
        }

        return frames;
    }

    // The locator, in its parent, of a frame's element, or null when it cannot be found.
    private async Task<string?> FindFrameElementAsync(Frame parent, Frame child, CancellationToken lookups)
    {
        TimeBudget budget = this.CreateBudget(lookups);
        RemoteValueList? values;
        try
        {
            RemoteValue described = await this.browser.Group.ScriptHost.CallRecorderAsync(parent.Id, "(recorder, testIdAttribute) => recorder.describeFrames(testIdAttribute)", [LocalValue.String(this.browser.Group.Options.TestIdAttribute)], budget).ConfigureAwait(false);
            values = described.As<CollectionRemoteValue>().Value!.Select(entry => entry.As<CollectionRemoteValue>().Value!).FirstOrDefault(entry => entry[0].As<WindowProxyRemoteValue>().Value.BrowsingContextId == child.Id);
        }
        catch (Exception)
        {
            // A parent that cannot describe its frames, as one that has closed, finds none.
            return null;
        }

        if (values is null)
        {
            return null;
        }

        JsonElement description;
        using (JsonDocument document = JsonDocument.Parse(values[1].As<StringRemoteValue>().Value))
        {
            description = document.RootElement.Clone();
        }

        NodeRemoteValue[] elements = [.. values.Skip(2).Select(element => element.As<NodeRemoteValue>())];
        return (await LocatorGenerator.GenerateAsync(parent, description.GetProperty("target"), [.. description.GetProperty("ancestors").EnumerateArray()], elements, budget).ConfigureAwait(false)).Code;
    }

    // Declares the frames without variables, top down, each after settling the pending statement, and gives the
    // variable of the frame acted in: its page's for a main frame.
    private string DeclareFrames(Frame frame, List<FrameStep> frames, string pageVariable, List<RecordedStep> settled)
    {
        string Variable(Frame target) => target.ParentFrame is null ? pageVariable : this.frameVariables[target];
        foreach ((Frame declared, string code) in frames)
        {
            settled.AddRange(this.TakePending());
            string variable = this.frameVariables.Count == 0 ? "frame" : $"frame{this.frameVariables.Count}";
            settled.Add(new($"Frame {variable} = await {Variable(declared.ParentFrame!)}.{code}.ContentFrameAsync();", LocatorNamespaces(code)));
            this.frameVariables[declared] = variable;
        }

        return Variable(frame);
    }

    // Using the toolbar settles the pending statement.
    private async Task ChooseModeAsync(Frame source, string chosenMode)
    {
        List<RecordedStep> settled;
        lock (this.lockObject)
        {
            settled = [.. this.TakePending()];
            this.mode = chosenMode;
        }

        this.SetModeInOtherDocuments(source, chosenMode);
        foreach (RecordedStep step in settled)
        {
            await this.SettleAsync(step).ConfigureAwait(false);
        }
    }

    // The element's locator; consecutive actions on one element, such as the fills of typing, share the first's.
    private async Task<GeneratedLocator> FindLocatorAsync(Frame frame, JsonElement message, IReadOnlyList<NodeRemoteValue> elements, CancellationToken lookups)
    {
        if (this.lastLocator is { } last && last.Frame == frame && last.ElementId == elements[0].SharedId)
        {
            return last.Generated;
        }

        GeneratedLocator generated = await LocatorGenerator.GenerateAsync(frame, message.GetProperty("target"), [.. message.GetProperty("ancestors").EnumerateArray()], elements, this.CreateBudget(lookups)).ConfigureAwait(false);
        this.lastLocator = new RememberedLocator(frame, elements[0].SharedId, generated);
        return generated;
    }

    // The page's variable, declaring a page after the first, after settling the pending statement, when it has none.
    private string GetPageVariable(Page page, List<RecordedStep> settled)
    {
        if (!this.pageVariables.TryGetValue(page, out string? variable))
        {
            variable = this.NameVariable(page);
            if (variable != "page")
            {
                settled.AddRange(this.TakePending());
                settled.Add(new($"Page {variable} = await page.Browser.NewPageAsync();", []));
            }
        }

        return variable;
    }

    private string NameVariable(Page page)
    {
        string variable = this.pageVariables.Count == 0 ? "page" : $"page{this.pageVariables.Count}";
        this.pageVariables[page] = variable;
        return variable;
    }

    // The document that chose the mode has applied it already.
    private void SetModeInOtherDocuments(Frame source, string chosenMode)
    {
        lock (this.lockObject)
        {
            foreach (Frame frame in this.browser.Pages.SelectMany(page => page.Frames).Where(frame => frame != source))
            {
                this.documentCalls.Add(this.RunStepAsync(
                    $"Setting the code recording's mode in browsing context {frame.Id}",
                    () => this.browser.Group.ScriptHost.CallRecorderAsync(frame.Id, "(recorder, mode) => recorder.setMode(mode)", [LocalValue.String(chosenMode)], this.CreateBudget(CancellationToken.None))));
            }
        }
    }

    private IEnumerable<RecordedStep> TakePending()
    {
        RecordedStep? taken = this.pending;
        this.pending = null;
        return taken is null ? [] : [taken];
    }

    private async Task SettleAsync(RecordedStep step)
    {
        lock (this.lockObject)
        {
            this.statements.Add(step.Text);
            this.namespaces.UnionWith(step.Namespaces);
        }

        await this.onStatement.InvokeNotifyObserversAsync(new CodeStatementEventArgs(step.Text)).ConfigureAwait(false);
    }

    private TimeBudget CreateBudget(CancellationToken cancellationToken)
    {
        return new TimeBudget(this.browser.Group.Options.NavigationTimeout, this.browser.Group.Options.TimeProvider, cancellationToken);
    }

    // Runs a step that may fail, as when its document has closed, logging the failure.
    private async Task RunStepAsync(string description, Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this.browser.Group.LogAsync($"{description} failed: {ex.Message}", WebDriverBiDiLogLevel.Warn).ConfigureAwait(false);
        }
    }

    // An action's or an assertion's statement; a step with a merge key replaces the pending step with the same key: a
    // fill of the same field, or the next click of the same series, unless an effect or a dialog was attached to it.
    // An assertion settles at once. An action's call, on its page, can be wrapped in a wait for what it causes. A note
    // above the statement, such as one for files known only by their names, does not stop a replacement.
    private sealed record RecordedStep(string Statement, IReadOnlyList<string> Namespaces, string? MergeKey = null, int ClickCount = 0, bool SettlesAtOnce = false)
    {
        public string? Call { get; init; }

        public int Number { get; init; }

        public Page? Page { get; init; }

        public string? PageVariable { get; init; }

        public bool HasEffect { get; init; }

        public bool Navigated { get; init; }

        public IReadOnlyList<string> Comments { get; init; } = [];

        public string? Note { get; init; }

        public string Text => string.Join("\n", [.. this.Note is null ? [] : new[] { this.Note }, .. this.Comments, this.Statement]);

        public bool Replaces(RecordedStep earlier)
        {
            return this.MergeKey is not null && this.MergeKey == earlier.MergeKey && (this.ClickCount == 0 || this.ClickCount == earlier.ClickCount + 1) && !earlier.HasEffect && earlier.Comments.Count == 0;
        }

        // The first effect wraps the call; a later one, such as a navigation after a popup, is left out.
        public RecordedStep WithEffect(string? declaration, string wait)
        {
            return this.HasEffect ? this : this with { Statement = $"{declaration}await {this.PageVariable}.{wait}(() => {this.Call});", HasEffect = true };
        }
    }

    private sealed record RememberedLocator(Frame Frame, string? ElementId, GeneratedLocator Generated);

    private sealed record FrameStep(Frame Frame, string Code);

    // The action pending when a navigation started, by its number, or null for none, and whether the library
    // started it.
    private sealed record StartedNavigation(string? NavigationId, string Url, int? ActionNumber, bool Library);
}
