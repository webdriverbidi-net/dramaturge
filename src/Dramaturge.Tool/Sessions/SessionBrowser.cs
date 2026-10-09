// <copyright file="SessionBrowser.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Text.Json;
using WebDriverBiDi;
using WebDriverBiDi.Log;

/// <summary>
/// A session's browser, as commands drive it: its tabs, the one commands act on, each tab's latest snapshot, the
/// documents each frame has had, and the console messages not yet read.
/// </summary>
internal sealed class SessionBrowser : IDisposable
{
    private const int ConsoleLimit = 500;
    private const string NoTab = "no-tab";

    private readonly Browser browser;
    private readonly SessionSettings settings;
    private readonly HashSet<string> allowedOrigins;
    private readonly object lockObject = new();
    private readonly List<IDisposable> observers = [];
    private readonly Dictionary<string, int> documents = [];
    private readonly Dictionary<Page, PageSnapshot> snapshots = [];
    private readonly LinkedList<ConsoleEntry> console = [];
    private long consoleCount;
    private long consoleRead;
    private Page? current;

    private SessionBrowser(Browser browser, SessionSettings settings)
    {
        this.browser = browser;
        this.settings = settings;
        this.allowedOrigins = [.. settings.AllowedOrigins.Select(origin => Origin(new Uri(origin)))];
    }

    /// <summary>
    /// Starts following a session's browser, acting on its first tab, and, if the session allows only some origins,
    /// stops its pages from navigating to others.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="settings">The session's settings.</param>
    /// <param name="cancellationToken">A token that cancels the start.</param>
    /// <returns>The session's browser.</returns>
    public static async Task<SessionBrowser> StartAsync(Browser browser, SessionSettings settings, CancellationToken cancellationToken)
    {
        SessionBrowser session = new(browser, settings);
        lock (session.lockObject)
        {
            session.observers.Add(browser.Group.Driver.BrowsingContext.OnNavigationCommitted.AddObserver(e => session.OnDocument(e.BrowsingContextId)));
            session.observers.Add(browser.OnPageCreated.AddObserver(e => session.Watch(e.Page)));
            session.observers.Add(browser.OnPageClosed.AddObserver(e => session.OnClosed(e.Page)));
            foreach (Page page in browser.Pages)
            {
                session.Watch(page);
            }

            session.current = browser.Pages.FirstOrDefault();
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
                "screenshot" => Text(arguments, "path") is string path ? await this.ScreenshotAsync(path, Flag(arguments, "fullPage"), cancellationToken).ConfigureAwait(false) : BadArguments(command),
                "console" => this.ReadConsole(Flag(arguments, "all")),
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

    private static bool Flag(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static DaemonAnswer BadArguments(string command) => DaemonAnswer.Failed("bad-request", $"The arguments of '{command}' are missing or not valid.");

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
        }

        await page.NavigateAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
        return DaemonAnswer.Of(await this.DescribeAsync(page, snapshot, cancellationToken).ConfigureAwait(false));
    }

    // Runs an action on the current tab, if any, then describes the tab, with its snapshot unless asked not to.
    private async Task<DaemonAnswer> WithTabAsync(Func<Page, CancellationToken, Task>? action, bool snapshot, CancellationToken cancellationToken)
    {
        if (this.Current() is not Page page)
        {
            return DaemonAnswer.Failed(NoTab, "The session has no tab open.", "Open one with 'dramaturge open <url>'.");
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

    private async Task<DaemonAnswer> ScreenshotAsync(string path, bool fullPage, CancellationToken cancellationToken)
    {
        if (this.Current() is not Page page)
        {
            return DaemonAnswer.Failed(NoTab, "The session has no tab open.", "Open one with 'dramaturge open <url>'.");
        }

        byte[] image = await page.ScreenshotAsync(new PageScreenshotOptions() { FullPage = fullPage }, cancellationToken).ConfigureAwait(false);
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

    // The tab's address and title, and, if asked for, its snapshot, which becomes the tab's latest.
    private async Task<Dictionary<string, object?>> DescribeAsync(Page page, bool snapshot, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> described = new() { ["url"] = page.Url, ["title"] = await TitleAsync(page, cancellationToken).ConfigureAwait(false) };
        if (snapshot)
        {
            AriaSnapshot taken = await page.AriaSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            lock (this.lockObject)
            {
                this.snapshots[page] = new PageSnapshot(taken, page.Frames.ToDictionary(frame => frame.Id, frame => this.documents.GetValueOrDefault(frame.Id)));
            }

            described["snapshot"] = taken.ToString();
        }

        return described;
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

    private sealed record ConsoleEntry(long Number, Page Page, string Level, string Text);
}
