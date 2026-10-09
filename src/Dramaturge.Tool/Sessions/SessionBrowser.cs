// <copyright file="SessionBrowser.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WebDriverBiDi;
using WebDriverBiDi.BrowsingContext;
using WebDriverBiDi.Input;
using WebDriverBiDi.Log;
using WebDriverBiDi.Session;

/// <summary>
/// A session's browser, as commands drive it: its tabs, the one commands act on, each tab's latest snapshot, whose refs
/// actions take, the documents each frame has had, and the console messages not yet read.
/// </summary>
internal sealed class SessionBrowser : IDisposable
{
    private const int ConsoleLimit = 500;
    private const string NoTab = "no-tab";
    private const string SnapshotHint = "Run 'dramaturge snapshot' for the current tab's refs.";
    private const int EffectLimit = 100;

    private readonly Browser browser;
    private readonly CodeRecording recording;
    private readonly SessionSettings settings;
    private readonly HashSet<string> allowedOrigins;
    private readonly List<string> uploadDirectories;
    private readonly object lockObject = new();
    private readonly List<IDisposable> observers = [];
    private readonly Dictionary<string, int> documents = [];
    private readonly Dictionary<Page, PageSnapshot> snapshots = [];
    private readonly LinkedList<ConsoleEntry> console = [];
    private readonly LinkedList<Dictionary<string, object?>> effects = [];
    private readonly HashSet<Page> reportedPages = [];
    private long reportedConsole;
    private long consoleCount;
    private long consoleRead;
    private Page? current;

    private SessionBrowser(Browser browser, CodeRecording recording, SessionSettings settings)
    {
        this.browser = browser;
        this.recording = recording;
        this.settings = settings;
        this.allowedOrigins = [.. settings.AllowedOrigins.Select(origin => Origin(new Uri(origin)))];
        this.uploadDirectories = [.. settings.UploadDirectories.Select(directory => RealPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)];
    }

    /// <summary>
    /// Starts following a session's browser, acting on its first tab, and, if the session allows only some origins,
    /// stops its pages from navigating to others.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="recording">The recording of the session as code.</param>
    /// <param name="settings">The session's settings.</param>
    /// <param name="cancellationToken">A token that cancels the start.</param>
    /// <returns>The session's browser.</returns>
    public static async Task<SessionBrowser> StartAsync(Browser browser, CodeRecording recording, SessionSettings settings, CancellationToken cancellationToken)
    {
        SessionBrowser session = new(browser, recording, settings);
        lock (session.lockObject)
        {
            session.observers.Add(browser.Group.Driver.BrowsingContext.OnNavigationCommitted.AddObserver(e => session.OnDocument(e.BrowsingContextId)));

            // The driver's events, unlike the pages' own, are observed as they arrive, before the answer to an action.
            session.observers.Add(browser.Group.Driver.BrowsingContext.OnUserPromptOpened.AddObserver(e => session.Notice(new Dictionary<string, object?>() { ["kind"] = "dialog", ["type"] = e.PromptType.ToString().ToLowerInvariant(), ["message"] = e.Message, ["answer"] = e.Handler == UserPromptHandlerType.Accept ? "accepted" : "dismissed" })));
            session.observers.Add(browser.Group.Driver.BrowsingContext.OnDownloadWillBegin.AddObserver(e => session.Notice(new Dictionary<string, object?>() { ["kind"] = "download", ["fileName"] = e.SuggestedFileName, ["url"] = e.Url, ["saved"] = settings.DownloadDirectory is not null })));
            session.observers.Add(browser.OnPageCreated.AddObserver(e => session.Watch(e.Page)));
            session.observers.Add(browser.OnPageClosed.AddObserver(e => session.OnClosed(e.Page)));
            foreach (Page page in browser.Pages)
            {
                session.Watch(page);
            }

            session.current = browser.Pages.FirstOrDefault();
            session.reportedPages.UnionWith(browser.Pages);
        }

        if (settings.DownloadDirectory is null)
        {
            await browser.DenyDownloadsAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await browser.AllowDownloadsAsync(Path.GetFullPath(settings.DownloadDirectory), cancellationToken).ConfigureAwait(false);
        }

        if (settings.AllowedOrigins.Count > 0)
        {
            await browser.RouteAsync(request => request.Destination is "document" or "iframe" && !session.IsAllowed(request.Url), route => route.AbortAsync(), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return session;
    }

    /// <summary>
    /// Gets the origin of an address, as the session's allowed origins are written.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <returns>The origin, such as <c>https://example.com</c>.</returns>
    public static string Origin(Uri url) => url.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

    /// <summary>
    /// Gets the full path of a file or directory with every link along it followed, so that no link can lead out of
    /// a directory the path appears to be in.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path with no links in it.</returns>
    public static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string resolved = Path.GetPathRoot(full)!;
        foreach (string part in full[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            FileSystemInfo? target = Directory.Exists(resolved) ? Directory.ResolveLinkTarget(resolved, true) : File.ResolveLinkTarget(resolved, true);
            if (target is not null)
            {
                resolved = RealPath(target.FullName);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Runs a command on the browser.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="arguments">The command's arguments: an object, whose flags are false when absent.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    /// <returns>The answer, or <see langword="null"/> for a command the browser does not know.</returns>
    public async Task<DaemonAnswer?> RunAsync(string command, JsonElement arguments, CancellationToken cancellationToken)
    {
        bool snapshot = Flag(arguments, "snapshot");
        try
        {
            return command switch
            {
                "open" => Text(arguments, "url") is string url ? await this.OpenAsync(url, snapshot, cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "back" => await this.WithTabAsync((page, token) => page.GoBackAsync(cancellationToken: token), snapshot, cancellationToken).ConfigureAwait(false),
                "forward" => await this.WithTabAsync((page, token) => page.GoForwardAsync(cancellationToken: token), snapshot, cancellationToken).ConfigureAwait(false),
                "reload" => await this.WithTabAsync((page, token) => page.ReloadAsync(cancellationToken: token), snapshot, cancellationToken).ConfigureAwait(false),
                "url" or "snapshot" => await this.WithTabAsync(null, snapshot, cancellationToken).ConfigureAwait(false),
                "tabs" => await this.TabsAsync(cancellationToken).ConfigureAwait(false),
                "tab" => Number(arguments, "index") is int index ? await this.SwitchAsync(index, cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "screenshot" => Text(arguments, "path") is string path ? await this.ScreenshotAsync(path, Flag(arguments, "fullPage"), Text(arguments, "ref"), cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "click" => await this.ActAsync(command, arguments, (locator, result, token) => ClickAsync(locator, arguments, result, token), snapshot, cancellationToken).ConfigureAwait(false),
                "fill" => Text(arguments, "text") is string text ? await this.ActAsync(command, arguments, (locator, _, token) => locator.FillAsync(text, cancellationToken: token), snapshot, cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "press" => Text(arguments, "key") is string key ? await this.PressAsync(arguments, key, snapshot, cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "select" => Texts(arguments, "values") is List<string> values ? await this.ActAsync(command, arguments, (locator, result, token) => SelectAsync(locator, values, Flag(arguments, "byValue"), result, token), snapshot, cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "check" => await this.ActAsync(command, arguments, (locator, _, token) => locator.CheckAsync(cancellationToken: token), snapshot, cancellationToken).ConfigureAwait(false),
                "uncheck" => await this.ActAsync(command, arguments, (locator, _, token) => locator.UncheckAsync(cancellationToken: token), snapshot, cancellationToken).ConfigureAwait(false),
                "hover" => await this.ActAsync(command, arguments, (locator, _, token) => locator.HoverAsync(cancellationToken: token), snapshot, cancellationToken).ConfigureAwait(false),
                "expect" => Texts(arguments, "condition") is List<string> words ? await this.ExpectAsync(words, Number(arguments, "timeout"), cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "upload" => Texts(arguments, "files") is List<string> files ? await this.UploadAsync(arguments, files, snapshot, cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "console" => this.ReadConsole(Flag(arguments, "all")),
                "code" => await this.WriteCodeAsync(Text(arguments, "target"), Text(arguments, "path"), cancellationToken).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (WebDriverBiDiException ex)
        {
            return DaemonAnswer.Failed("browser-error", ex.Message);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this.lockObject)
        {
            this.observers.ForEach(observer => observer.Dispose());
            this.observers.Clear();
        }
    }

    private static string? Text(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? Number(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
    }

    // A non-empty array of strings.
    private static List<string>? Texts(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
            ? [.. value.EnumerateArray().Select(item => item.GetString()!)]
            : null;
    }

    private static bool Flag(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static DaemonAnswer BadArguments(string command) => DaemonAnswer.Failed("bad-request", $"The arguments of '{command}' are missing or not valid.");

    private static DaemonAnswer NoTabOpen() => DaemonAnswer.Failed(NoTab, "The session has no tab open.", "Open one with 'dramaturge open <url>'.");

    private static Task ClickAsync(ElementLocator locator, JsonElement arguments, Dictionary<string, object?> result, CancellationToken cancellationToken)
    {
        ClickOptions options = new() { Button = Text(arguments, "button") switch { "right" => PointerButton.Right, "middle" => PointerButton.Middle, _ => PointerButton.Left } };
        result["double"] = Flag(arguments, "double");
        return Flag(arguments, "double") ? locator.DblClickAsync(options, cancellationToken) : locator.ClickAsync(options, cancellationToken);
    }

    // A condition, as expect takes it: url, title, or text and a value, or a ref and a state, or text or value and a
    // value, or snapshot and, if any, a template.
    private static Condition? ParseCondition(List<string> words)
    {
        return words switch
        {
            ["url" or "title", string value] => new Condition(words[0], null, value),
            ["text", string value] => new Condition("page-text", null, value),
            [string reference, "visible" or "hidden" or "enabled" or "checked" or "unchecked" or "snapshot"] => new Condition(words[1], reference, null),
            [string reference, "text" or "value" or "snapshot", string value] => new Condition(words[1], reference, value),
            _ => null,
        };
    }

    private static string Describe(Condition condition)
    {
        return condition.Kind switch
        {
            "url" => $"the tab's URL is {condition.Value}",
            "title" => $"the tab's title is \"{condition.Value}\"",
            "page-text" => $"the tab's page contains \"{condition.Value}\"",
            "unchecked" => $"{condition.Reference} is not checked",
            "text" => $"{condition.Reference} contains \"{condition.Value}\"",
            "value" => $"{condition.Reference} has the value \"{condition.Value}\"",
            "snapshot" => $"{condition.Reference} matches {(condition.Value is null ? "its snapshot" : "the snapshot given")}",
            _ => $"{condition.Reference} is {condition.Kind}",
        };
    }

    // A key, such as Enter, a, or Control+Shift+a: modifier keys, then a key named as Keys names it, or a character.
    private static KeyPress? ParseKey(string chord)
    {
        string key = chord.EndsWith('+') ? "+" : chord[(chord.LastIndexOf('+') + 1)..];
        string? value = key.Length == 1 ? key : typeof(Keys).GetField(key, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)?.GetValue(null) as string;
        KeyModifiers modifiers = KeyModifiers.None;
        foreach (string name in chord[..^key.Length].Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.GetNames<KeyModifiers>().FirstOrDefault(modifier => modifier != nameof(KeyModifiers.None) && string.Equals(modifier, name, StringComparison.OrdinalIgnoreCase)) is not string modifierName)
            {
                return null;
            }

            modifiers |= Enum.Parse<KeyModifiers>(modifierName);
        }

        return value is null ? null : new KeyPress(value, modifiers);
    }

    // Options are matched by label, as a snapshot shows them, unless by value is asked for.
    private static async Task SelectAsync(ElementLocator locator, List<string> values, bool byValue, Dictionary<string, object?> result, CancellationToken cancellationToken)
    {
        result["selected"] = await locator.SelectOptionAsync(values.Select(value => byValue ? SelectOption.ByValue(value) : SelectOption.ByLabel(value)), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static Task PressAsync(ElementLocator locator, string key, KeyPress press, Dictionary<string, object?> result, CancellationToken cancellationToken)
    {
        result["key"] = key;
        return locator.PressAsync(press.Key, new KeyActionOptions() { Modifiers = press.Modifiers }, cancellationToken);
    }

    private static Task UploadAsync(ElementLocator locator, List<string> files, Dictionary<string, object?> result, CancellationToken cancellationToken)
    {
        result["files"] = files.Count;
        return locator.SetInputFilesAsync(files, cancellationToken: cancellationToken);
    }

    private static async Task<string> TitleAsync(Page page, CancellationToken cancellationToken)
    {
        try
        {
            return await page.EvaluateAsync<string>("() => document.title", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebDriverBiDiException or InvalidCastException)
        {
            // A document that is going away, or whose title cannot be read, has none to give.
            return string.Empty;
        }
    }

    // An address of the about: scheme, such as a new tab's, is always allowed.
    private bool IsAllowed(string url)
    {
        return this.allowedOrigins.Count == 0
            || (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "about" || this.allowedOrigins.Contains(Origin(uri))));
    }

    private async Task<DaemonAnswer> OpenAsync(string url, bool snapshot, CancellationToken cancellationToken)
    {
        // On Unix, a path such as /page parses as a file: URL; an address must name its scheme.
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !url.StartsWith($"{uri.Scheme}:", StringComparison.OrdinalIgnoreCase))
        {
            return DaemonAnswer.Failed(CommandOutput.Usage, $"'{url}' is not an absolute URL, such as https://example.com/.");
        }

        if (uri.IsFile && !this.settings.AllowFileUrls)
        {
            return DaemonAnswer.Failed("file-url-not-allowed", "The session does not open file: URLs.", "Start the session with --allow-file-urls to open them.");
        }

        if (!this.IsAllowed(url))
        {
            return DaemonAnswer.Failed("origin-not-allowed", $"{Origin(uri)} is not an origin the session allows.", "Start the session with --allow-origin for each origin it may open.");
        }

        Page page = this.Current() ?? await this.browser.NewPageAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        lock (this.lockObject)
        {
            this.current = page;
            this.reportedPages.Add(page);
        }

        await page.NavigateAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
        return DaemonAnswer.Of(await this.DescribeAsync(page, snapshot, cancellationToken).ConfigureAwait(false));
    }

    // Runs an action on the current tab, if any, then describes the tab, with its snapshot unless asked not to.
    private async Task<DaemonAnswer> WithTabAsync(Func<Page, CancellationToken, Task>? action, bool snapshot, CancellationToken cancellationToken)
    {
        if (this.Current() is not Page page)
        {
            return NoTabOpen();
        }

        if (action is not null)
        {
            await action(page, cancellationToken).ConfigureAwait(false);
        }

        return DaemonAnswer.Of(await this.DescribeAsync(page, snapshot, cancellationToken).ConfigureAwait(false));
    }

    private async Task<DaemonAnswer> TabsAsync(CancellationToken cancellationToken)
    {
        Page? currentPage = this.Current();
        IReadOnlyList<Page> pages = this.browser.Pages;
        List<Dictionary<string, object?>> tabs = [];
        for (int i = 0; i < pages.Count; i++)
        {
            tabs.Add(new Dictionary<string, object?>() { ["index"] = i + 1, ["url"] = pages[i].Url, ["title"] = await TitleAsync(pages[i], cancellationToken).ConfigureAwait(false), ["current"] = pages[i] == currentPage });
        }

        return DaemonAnswer.Of(new Dictionary<string, object?>() { ["tabs"] = tabs });
    }

    private async Task<DaemonAnswer> SwitchAsync(int index, CancellationToken cancellationToken)
    {
        IReadOnlyList<Page> pages = this.browser.Pages;
        if (index < 1 || index > pages.Count)
        {
            return DaemonAnswer.Failed(NoTab, $"There is no tab [{index}].", "'dramaturge tabs' lists the tabs.");
        }

        Page page = pages[index - 1];
        await page.BringToFrontAsync(cancellationToken).ConfigureAwait(false);
        lock (this.lockObject)
        {
            this.current = page;
        }

        Dictionary<string, object?> described = await this.DescribeAsync(page, false, cancellationToken).ConfigureAwait(false);
        described["index"] = index;
        return DaemonAnswer.Of(described);
    }

    // The whole tab, or, given a ref, its element.
    private async Task<DaemonAnswer> ScreenshotAsync(string path, bool fullPage, string? reference, CancellationToken cancellationToken)
    {
        if (this.Current() is not Page page)
        {
            return NoTabOpen();
        }

        byte[] image;
        if (reference is null)
        {
            image = await page.ScreenshotAsync(new PageScreenshotOptions() { FullPage = fullPage }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Target target = this.Find(page, reference);
            if (target.Failure is not null)
            {
                return target.Failure;
            }

            await page.BringToFrontAsync(cancellationToken).ConfigureAwait(false);
            image = await target.Locator!.ScreenshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await File.WriteAllBytesAsync(path, image, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DaemonAnswer.Failed("write-failed", $"The screenshot could not be written to {path}: {ex.Message}");
        }

        return DaemonAnswer.Of(new Dictionary<string, object?>() { ["path"] = path, ["bytes"] = image.Length });
    }

    // Runs an action on the element of a ref in the current tab's latest snapshot, then reports whether it navigated
    // the tab, the dialogs, downloads, tabs opened, and errors in the console since the last action, which can include
    // those of the last action that came after it, and, unless asked not to, the snapshot after it, as its changes
    // from the one before when that is shorter.
    private async Task<DaemonAnswer> ActAsync(string command, JsonElement arguments, Func<ElementLocator, Dictionary<string, object?>, CancellationToken, Task> action, bool snapshot, CancellationToken cancellationToken)
    {
        if (Text(arguments, "ref") is not string reference)
        {
            return BadArguments(command);
        }

        if (this.Current() is not Page page)
        {
            return NoTabOpen();
        }

        Target target = this.Find(page, reference);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        Dictionary<string, object?> result = new() { ["ref"] = reference };
        int documentsBefore;
        lock (this.lockObject)
        {
            documentsBefore = this.documents.GetValueOrDefault(page.MainFrame.Id);
        }

        try
        {
            // Chrome renders no frames in a tab behind another, and an action waits for frames.
            await page.BringToFrontAsync(cancellationToken).ConfigureAwait(false);
            await action(target.Locator!, result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebDriverBiDiException or InvalidOperationException or ArgumentException)
        {
            return DaemonAnswer.Failed("action-failed", ex.Message);
        }

        string? taken = null;
        if (snapshot)
        {
            try
            {
                taken = (await this.TakeSnapshotAsync(page, cancellationToken).ConfigureAwait(false)).ToString();
            }
            catch (Exception ex) when (ex is WebDriverBiDiException or InvalidOperationException)
            {
                // The page can be changing, as when the action navigated it.
                result["snapshotError"] = ex.Message;
            }
        }

        bool navigated;
        lock (this.lockObject)
        {
            navigated = this.documents.GetValueOrDefault(page.MainFrame.Id) != documentsBefore;
        }

        result["url"] = page.Url;
        result["navigated"] = navigated;
        if (navigated)
        {
            result["title"] = await TitleAsync(page, cancellationToken).ConfigureAwait(false);
            result["snapshot"] = taken;
        }
        else if (taken is not null)
        {
            IReadOnlyList<string> changes = SnapshotDiff.Describe(target.Snapshot!.Snapshot.ToString(), taken);
            if (changes.Count < taken.Split('\n').Length)
            {
                result["changes"] = changes;
            }
            else
            {
                result["snapshot"] = taken;
            }
        }

        result["effects"] = this.TakeEffects();
        return DaemonAnswer.Of(result);
    }

    // The effects since they were last taken, then the tabs opened, and the errors in the console, since then.
    private List<Dictionary<string, object?>> TakeEffects()
    {
        lock (this.lockObject)
        {
            List<Page> pages = [.. this.browser.Pages];
            List<Dictionary<string, object?>> taken =
            [
                .. this.effects,
                .. pages.Where(this.reportedPages.Add).Select(opened => new Dictionary<string, object?>() { ["kind"] = "tab", ["index"] = pages.IndexOf(opened) + 1, ["url"] = opened.Url }),
                .. this.console.Where(entry => entry.Number > this.reportedConsole && entry.Level == "error").Select(entry => new Dictionary<string, object?>() { ["kind"] = "error", ["text"] = entry.Text }),
            ];
            this.effects.Clear();
            this.reportedConsole = this.consoleCount;
            return taken;
        }
    }

    private async Task<DaemonAnswer> PressAsync(JsonElement arguments, string key, bool snapshot, CancellationToken cancellationToken)
    {
        if (ParseKey(key) is not KeyPress press)
        {
            return DaemonAnswer.Failed(CommandOutput.Usage, $"'{key}' is not a key, such as Enter, a, or Control+a.");
        }

        return await this.ActAsync("press", arguments, (locator, result, token) => PressAsync(locator, key, press, result, token), snapshot, cancellationToken).ConfigureAwait(false);
    }

    // Waits until a condition is met, through the library's expectations, which the session's code records when they
    // are of a kind it records.
    private async Task<DaemonAnswer> ExpectAsync(List<string> words, int? seconds, CancellationToken cancellationToken)
    {
        if (seconds <= 0)
        {
            return DaemonAnswer.Failed(CommandOutput.Usage, "--timeout must be at least 1 second.");
        }

        if (ParseCondition(words) is not Condition condition)
        {
            return DaemonAnswer.Failed(CommandOutput.Usage, $"'{string.Join(" ", words)}' is not a condition, such as 'e5 visible', 'e5 text Saved', or 'url https://example.com/'.", "Run 'dramaturge expect --help' for the conditions.");
        }

        if (this.Current() is not Page page)
        {
            return NoTabOpen();
        }

        ElementLocator? locator = null;
        if (condition.Reference is string reference)
        {
            Target target = this.Find(page, reference);
            if (target.Failure is not null)
            {
                return target.Failure;
            }

            locator = target.Locator;
        }

        TimeSpan timeout = seconds is int given ? TimeSpan.FromSeconds(given) : this.browser.Group.Options.ExpectTimeout;
        string described = Describe(condition);
        Dictionary<string, object?> result = new() { ["condition"] = described };
        try
        {
            await page.BringToFrontAsync(cancellationToken).ConfigureAwait(false);
            switch (condition.Kind)
            {
                case "url":
                    await Assertions.Expect(page).ToHaveUrlAsync(condition.Value!, timeout: timeout, cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case "title":
                    await Assertions.Expect(page).ToHaveTitleAsync(condition.Value!, timeout: timeout, cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case "page-text":
                    await Assertions.Expect(page.Locate(new CssLocator("body"))).ToContainTextAsync(condition.Value!, timeout: timeout, cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case "visible":
                    await Assertions.Expect(locator!).ToBeVisibleAsync(timeout, cancellationToken).ConfigureAwait(false);
                    break;
                case "hidden":
                    await Assertions.Expect(locator!).ToBeHiddenAsync(timeout, cancellationToken).ConfigureAwait(false);
                    break;
                case "enabled":
                    await Assertions.Expect(locator!).ToBeEnabledAsync(timeout, cancellationToken).ConfigureAwait(false);
                    break;
                case "checked":
                    await Assertions.Expect(locator!).ToBeCheckedAsync(timeout, cancellationToken).ConfigureAwait(false);
                    break;
                case "unchecked":
                    await Assertions.Expect(locator!).Not.ToBeCheckedAsync(timeout, cancellationToken).ConfigureAwait(false);
                    break;
                case "text":
                    await Assertions.Expect(locator!).ToContainTextAsync(condition.Value!, timeout: timeout, cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case "value":
                    await Assertions.Expect(locator!).ToHaveValueAsync(condition.Value!, timeout, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    // Without a template, the element's snapshot now is the one expected.
                    string template = condition.Value ?? (await locator!.AriaSnapshotAsync(new AriaSnapshotOptions() { IncludeRefs = false }, timeout, cancellationToken).ConfigureAwait(false)).ToString();
                    await Assertions.Expect(locator!).ToMatchAriaSnapshotAsync(template, timeout, cancellationToken).ConfigureAwait(false);
                    result["template"] = template;
                    break;
            }
        }
        catch (Exception ex) when (ex is ExpectationFailedException or WebDriverBiDiTimeoutException)
        {
            return new DaemonAnswer(null, new SessionError("expectation-failed", $"Not met within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} second{(timeout == TimeSpan.FromSeconds(1) ? string.Empty : "s")}: {described}.", null, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return DaemonAnswer.Failed(CommandOutput.Usage, ex.Message);
        }

        return DaemonAnswer.Of(result);
    }

    // Only files in the session's upload directories, or below them, links followed, are uploaded.
    private async Task<DaemonAnswer> UploadAsync(JsonElement arguments, List<string> files, bool snapshot, CancellationToken cancellationToken)
    {
        foreach (string file in files)
        {
            if (!File.Exists(file))
            {
                return DaemonAnswer.Failed("no-file", $"There is no file {file}.");
            }

            string real = RealPath(file);
            if (!this.uploadDirectories.Any(directory => real.StartsWith(directory, StringComparison.Ordinal)))
            {
                return DaemonAnswer.Failed("upload-not-allowed", $"{file} is not in a directory the session uploads from.", "Start the session in a directory above it, or with --allow-upload-from.");
            }
        }

        return await this.ActAsync("upload", arguments, (locator, result, token) => UploadAsync(locator, files, result, token), snapshot, cancellationToken).ConfigureAwait(false);
    }

    // The element of a ref in the tab's latest snapshot, while the ref's frame still has the document it was taken from.
    private Target Find(Page page, string reference)
    {
        lock (this.lockObject)
        {
            if (!this.snapshots.TryGetValue(page, out PageSnapshot? latest))
            {
                return new Target(null, null, DaemonAnswer.Failed("unknown-ref", $"The current tab has no snapshot to take {reference} from.", SnapshotHint));
            }

            ElementLocator locator;
            try
            {
                locator = latest.Snapshot.Locator(reference);
            }
            catch (ArgumentException)
            {
                return new Target(null, null, DaemonAnswer.Failed("unknown-ref", $"The current tab's latest snapshot has no ref {reference}.", SnapshotHint));
            }

            return latest.Documents.GetValueOrDefault(locator.Frame.Id) == this.documents.GetValueOrDefault(locator.Frame.Id)
                ? new Target(locator, latest, null)
                : new Target(null, null, DaemonAnswer.Failed("stale-ref", $"{reference} is from a document its frame has since replaced.", SnapshotHint));
        }
    }

    // The session's code for a target, the program by default, as text, or written to a file.
    private async Task<DaemonAnswer> WriteCodeAsync(string? targetName, string? path, CancellationToken cancellationToken)
    {
        if (Enum.GetNames<CodeTarget>().FirstOrDefault(name => string.Equals(name, targetName ?? nameof(CodeTarget.Program), StringComparison.OrdinalIgnoreCase)) is not string name)
        {
            return BadArguments("code");
        }

        CodeTarget target = Enum.Parse<CodeTarget>(name);
        string code = await this.recording.GetCodeAsync(target).ConfigureAwait(false);
        Dictionary<string, object?> result = new() { ["target"] = name.ToLowerInvariant() };
        if (path is null)
        {
            result["code"] = code;
        }
        else
        {
            try
            {
                await File.WriteAllTextAsync(path, code, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return DaemonAnswer.Failed("write-failed", $"The code could not be written to {path}: {ex.Message}");
            }

            result["path"] = path;
        }

        result["notes"] = this.settings.Dialogs == UserPromptHandlerType.Accept
            ? (string[])["The session accepted dialogs, but the code does not answer them, so they are dismissed when it runs; set the browser's unhandledPromptBehavior to accept them."]
            : [];
        return DaemonAnswer.Of(result);
    }

    // The messages since the last read, or all those kept, each with its tab, if the tab is still open.
    private DaemonAnswer ReadConsole(bool all)
    {
        List<Dictionary<string, object?>> messages;
        lock (this.lockObject)
        {
            List<Page> pages = [.. this.browser.Pages];
            messages = [.. this.console.Where(entry => all || entry.Number > this.consoleRead).Select(entry => new Dictionary<string, object?>()
            {
                ["tab"] = pages.IndexOf(entry.Page) is int index and >= 0 ? index + 1 : null,
                ["level"] = entry.Level,
                ["text"] = entry.Text,
            })];
            this.consoleRead = this.consoleCount;
        }

        return DaemonAnswer.Of(new Dictionary<string, object?>() { ["messages"] = messages });
    }

    // The tab's address and title, and, if asked for, its snapshot.
    private async Task<Dictionary<string, object?>> DescribeAsync(Page page, bool snapshot, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> described = new() { ["url"] = page.Url, ["title"] = await TitleAsync(page, cancellationToken).ConfigureAwait(false) };
        if (snapshot)
        {
            described["snapshot"] = (await this.TakeSnapshotAsync(page, cancellationToken).ConfigureAwait(false)).ToString();
        }

        return described;
    }

    // A snapshot, which becomes the tab's latest.
    private async Task<AriaSnapshot> TakeSnapshotAsync(Page page, CancellationToken cancellationToken)
    {
        AriaSnapshot taken = await page.AriaSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        lock (this.lockObject)
        {
            this.snapshots[page] = new PageSnapshot(taken, page.Frames.ToDictionary(frame => frame.Id, frame => this.documents.GetValueOrDefault(frame.Id)));
        }

        return taken;
    }

    private Page? Current()
    {
        lock (this.lockObject)
        {
            return this.current;
        }
    }

    private void OnDocument(string contextId)
    {
        lock (this.lockObject)
        {
            this.documents[contextId] = this.documents.GetValueOrDefault(contextId) + 1;
        }
    }

    private void Watch(Page page)
    {
        lock (this.lockObject)
        {
            this.observers.Add(page.OnConsoleMessage.AddObserver(e => this.Log(page, e.Level switch { LogLevel.Debug => "debug", LogLevel.Info => "info", LogLevel.Warn => "warning", _ => "error" }, e.Text)));
            this.observers.Add(page.OnPageError.AddObserver(e => this.Log(page, "error", $"Uncaught {e.Message}")));
        }
    }

    // The tab that closes is no longer acted on: the last one left is, if any is.
    private void OnClosed(Page page)
    {
        lock (this.lockObject)
        {
            this.snapshots.Remove(page);
            if (this.current == page)
            {
                this.current = this.browser.Pages.LastOrDefault(open => open != page);
            }
        }
    }

    // Kept for the next action to report; the newest are kept.
    private void Notice(Dictionary<string, object?> effect)
    {
        lock (this.lockObject)
        {
            this.effects.AddLast(effect);
            if (this.effects.Count > EffectLimit)
            {
                this.effects.RemoveFirst();
            }
        }
    }

    private void Log(Page page, string level, string text)
    {
        lock (this.lockObject)
        {
            this.console.AddLast(new ConsoleEntry(++this.consoleCount, page, level, text));
            if (this.console.Count > ConsoleLimit)
            {
                this.console.RemoveFirst();
            }
        }
    }

    // A tab's latest snapshot, with the number of documents each of its frames had had when it was taken.
    private sealed record PageSnapshot(AriaSnapshot Snapshot, IReadOnlyDictionary<string, int> Documents);

    // A condition expect waits for: of the tab, or, with a ref, of its element.
    private sealed record Condition(string Kind, string? Reference, string? Value);

    // A key to press, as the browser takes it, with the modifier keys held down.
    private sealed record KeyPress(string Key, KeyModifiers Modifiers);

    // The element of a ref, with the snapshot it is from, or why there is none.
    private sealed record Target(ElementLocator? Locator, PageSnapshot? Snapshot, DaemonAnswer? Failure);

    private sealed record ConsoleEntry(long Number, Page Page, string Level, string Text);
}
