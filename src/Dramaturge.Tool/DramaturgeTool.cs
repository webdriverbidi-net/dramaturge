// <copyright file="DramaturgeTool.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json;
using Dramaturge.Browsers;
using Dramaturge.Tool.Sessions;
using WebDriverBiDi;
using WebDriverBiDi.Session;

/// <summary>
/// The dramaturge tool: installs, lists, and removes, under its browsers command, the browsers and drivers that
/// Dramaturge.Browsers caches, and records the actions a user takes in a browser as C#.
/// </summary>
public static class DramaturgeTool
{
    private const string CommandName = "dramaturge";

    private static readonly Dictionary<string, object?> NoArguments = [];

    /// <summary>
    /// Runs the tool.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">The writer for results.</param>
    /// <param name="error">The writer for progress and errors.</param>
    /// <param name="createDownloadOptions">
    /// Creates the download options from the --path value, if given, and a progress reporter, or <see langword="null"/>
    /// for the options the environment variables configure.
    /// </param>
    /// <param name="configureLauncher">
    /// Gives the launcher of the browser codegen records in, from the one the command configures, or
    /// <see langword="null"/> for that one.
    /// </param>
    /// <param name="outputIsTerminal">A value indicating whether the output is a terminal, on which codegen shows the statement still open.</param>
    /// <param name="sessionHost">Where sessions are kept and how their daemons start, or <see langword="null"/> for the defaults of <see cref="SessionHost"/>.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    /// <returns>A task whose result is the exit code: 0 on success, 1 if anything failed, 2 for arguments that cannot be used, and 3 when the session named is not running.</returns>
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, Func<string?, IProgress<BrowserDownloadProgress>, BrowserDownloadOptions>? createDownloadOptions = null, Func<BrowserLauncherBuilder, BrowserLauncherBuilder>? configureLauncher = null, bool outputIsTerminal = false, SessionHost? sessionHost = null, CancellationToken cancellationToken = default)
    {
        createDownloadOptions ??= CreateDefaultDownloadOptions;
        configureLauncher ??= builder => builder;
        sessionHost ??= new SessionHost();
        ProgressWriter progress = new(error);
        Option<string?> pathOption = new("--path")
        {
            Description = "The cache directory. Defaults to DRAMATURGE_BROWSERS_PATH, or the per-user cache directory.",
            Recursive = true,
        };

        Argument<string[]> installTargets = new("targets")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "The browsers and drivers to install, as name[@channel|@milestone|@version], such as chrome@beta, chromedriver@131, or firefox@134.0. Names: chrome, chrome-headless-shell, firefox, chromedriver, geckodriver, msedgedriver.",
        };
        Option<bool> installDryRun = new("--dry-run") { Description = "Show the version and URL each target resolves to, and whether it is cached, without downloading." };
        Command install = new("install", "Download browsers and drivers into the cache.") { installTargets, installDryRun };
        install.SetAction((parseResult, token) => InstallAsync(
            parseResult.GetValue(installTargets)!,
            parseResult.GetValue(installDryRun),
            createDownloadOptions(parseResult.GetValue(pathOption), progress),
            output,
            error,
            token));

        Command list = new("list", "List the browsers and drivers in the cache.");
        list.SetAction(parseResult => List(createDownloadOptions(parseResult.GetValue(pathOption), progress), output, error));

        Argument<string[]> uninstallTargets = new("targets")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "The installations to remove, as name[@channel|@milestone|@version], such as chrome@canary or chromedriver@131.",
        };
        Option<bool> uninstallDryRun = new("--dry-run") { Description = "Show what would be removed, without removing it." };
        Command uninstall = new("uninstall", "Remove browsers and drivers from the cache.") { uninstallTargets, uninstallDryRun };
        uninstall.SetAction((parseResult, token) => ClearAsync(
            parseResult.GetValue(uninstallTargets)!,
            parseResult.GetValue(uninstallDryRun),
            createDownloadOptions(parseResult.GetValue(pathOption), progress),
            output,
            error,
            token));

        Option<bool> clearDryRun = new("--dry-run") { Description = "Show what would be removed, without removing it." };
        Command clear = new("clear", "Remove every browser and driver from the cache.") { clearDryRun };
        clear.SetAction((parseResult, token) => ClearAsync(
            [],
            parseResult.GetValue(clearDryRun),
            createDownloadOptions(parseResult.GetValue(pathOption), progress),
            output,
            error,
            token));

        Command browsers = new("browsers", "Install, list, and remove the browsers and drivers that Dramaturge.Browsers caches.") { install, list, uninstall, clear };
        browsers.Options.Add(pathOption);

        Argument<string?> codegenUrl = new("url") { Arity = ArgumentArity.ZeroOrOne, Description = "The address to open first." };
        Option<CodeTarget> codegenTarget = new("--target") { Description = "The kind of file to write.", DefaultValueFactory = _ => CodeTarget.Program };
        Option<BrowserKind> codegenBrowser = new("--browser") { Description = "The browser to record in.", DefaultValueFactory = _ => BrowserKind.Chrome };
        Option<string?> codegenChannel = new("--channel") { Description = "The browser's release channel, as browsers install names it: stable (the default), beta, dev, and canary for Chrome and Edge, or nightly and esr for Firefox." };
        Option<string?> codegenTestId = new("--test-id-attribute") { Description = "The attribute test IDs are read from. Defaults to data-testid." };
        Option<string?> codegenOutput = new("--output", "-o") { Description = "A file to keep the whole code in as it is recorded." };
        Option<bool> codegenMaskPasswords = new("--mask-passwords") { Description = "Write values typed into, or expected of, password fields as a read of the DRAMATURGE_PASSWORD environment variable." };
        Command codegen = new("codegen", "Record the actions you take in a browser as C#, until you close the browser or press Ctrl+C.") { codegenUrl, codegenTarget, codegenBrowser, codegenChannel, codegenTestId, codegenOutput, codegenMaskPasswords, pathOption };
        codegen.SetAction((parseResult, token) => Codegen.RunAsync(
            new CodegenSettings(
                parseResult.GetValue(codegenUrl),
                parseResult.GetValue(codegenTarget),
                parseResult.GetValue(codegenBrowser),
                parseResult.GetValue(codegenChannel),
                parseResult.GetValue(codegenTestId),
                parseResult.GetValue(codegenOutput),
                parseResult.GetValue(codegenMaskPasswords),
                outputIsTerminal),
            createDownloadOptions(parseResult.GetValue(pathOption), progress),
            configureLauncher,
            output,
            error,
            token));

        Option<string> sessionOption = new("--session") { Description = "The session's name.", DefaultValueFactory = _ => SessionPaths.DefaultName };
        Option<bool> jsonOption = new("--json") { Description = "Write the result, or the error, as one line of JSON." };
        Command session = CreateSessionCommand(args, pathOption, sessionOption, jsonOption, createDownloadOptions, progress, configureLauncher, sessionHost, output, error);

        // A RootCommand would name itself for the entry assembly, Dramaturge.Tool, rather than the command.
        Command root = new(CommandName, "Installs, lists, and removes the browsers and drivers that Dramaturge.Browsers caches, records browser actions as C#, and runs browser sessions that commands drive.") { browsers, codegen, session };
        foreach (Command command in CreatePageCommands(sessionOption, jsonOption, sessionHost, output, error))
        {
            root.Subcommands.Add(command);
        }

        root.Options.Add(new HelpOption());
        root.Options.Add(new VersionOption());

        // Arguments that cannot be parsed are a usage error, written as any other command's error would be; an
        // unrecognized token is the error that matters when there is one.
        ParseResult parseResult = root.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            List<ParseError> unrecognized = [.. parseResult.Errors.Where(problem => parseResult.UnmatchedTokens.Any(token => problem.Message.Contains($"'{token}'", StringComparison.Ordinal)))];
            IEnumerable<ParseError> reported = unrecognized.Count > 0 ? unrecognized : parseResult.Errors;
            string hint = parseResult.UnmatchedTokens.FirstOrDefault() == "wait" ? "To wait until a condition is met, use 'dramaturge expect'." : $"Run '{CommandPath(parseResult.CommandResult)} --help' for usage.";
            SessionError usage = new(CommandOutput.Usage, string.Join(" ", reported.Select(problem => problem.Message)), hint);
            return Task.FromResult(CommandOutput.Write(CommandOutcome.Failure(usage), args.Contains("--json"), output, error));
        }

        return parseResult.InvokeAsync(new InvocationConfiguration() { Output = output, Error = error }, cancellationToken);
    }

    // The settings start takes are passed on, as they were given, to the serve it starts.
    private static Command CreateSessionCommand(string[] args, Option<string?> pathOption, Option<string> sessionOption, Option<bool> jsonOption, Func<string?, IProgress<BrowserDownloadProgress>, BrowserDownloadOptions> createDownloadOptions, ProgressWriter progress, Func<BrowserLauncherBuilder, BrowserLauncherBuilder> configureLauncher, SessionHost host, TextWriter output, TextWriter error)
    {
        Option<BrowserKind> browserOption = new("--browser") { Description = "The browser to drive.", DefaultValueFactory = _ => BrowserKind.Chrome };
        Option<string?> channelOption = new("--channel") { Description = "The browser's release channel, as browsers install names it: stable (the default), beta, dev, and canary for Chrome and Edge, or nightly and esr for Firefox." };
        Option<bool> headedOption = new("--headed") { Description = "Show the browser's window. By default the browser has none." };
        Option<string?> userDataDirectoryOption = new("--user-data-dir") { Description = "A profile directory for the browser to keep. By default it gets a temporary one." };
        Option<int> idleTimeoutOption = new("--idle-timeout") { Description = "The minutes the session waits for a command before it stops.", DefaultValueFactory = _ => 30 };
        Option<bool> maskPasswordsOption = new("--mask-passwords") { Description = "Write values typed into, or expected of, password fields in the session's code as a read of the DRAMATURGE_PASSWORD environment variable." };
        Option<string[]> allowOriginOption = new("--allow-origin") { Description = "An origin, such as https://example.com, the session's pages may navigate to; give it once for each. By default every origin is allowed." };
        Option<bool> allowFileUrlsOption = new("--allow-file-urls") { Description = "Let the session open file: URLs." };
        Option<string> dialogsOption = new("--dialogs") { Description = "How the browser answers the dialogs pages open, such as confirm: dismiss (the default) or accept.", DefaultValueFactory = _ => "dismiss" };
        dialogsOption.AcceptOnlyFromAmong("dismiss", "accept");
        Option<string?> downloadsOption = new("--downloads") { Description = "A directory to save downloads in. By default the session refuses them." };
        Option<string[]> allowUploadFromOption = new("--allow-upload-from") { Description = "A directory files may be uploaded from, with those below it, besides the one the session is started in; give it once for each." };
        Option<bool> detachedOption = new("--detached") { Hidden = true };
        SessionSettings Settings(ParseResult parseResult) => new(
            parseResult.GetValue(sessionOption)!,
            parseResult.GetValue(browserOption),
            parseResult.GetValue(channelOption),
            parseResult.GetValue(headedOption),
            parseResult.GetValue(userDataDirectoryOption),
            TimeSpan.FromMinutes(parseResult.GetValue(idleTimeoutOption)),
            parseResult.GetValue(maskPasswordsOption),
            parseResult.GetValue(allowOriginOption) ?? [],
            parseResult.GetValue(allowFileUrlsOption),
            parseResult.GetValue(dialogsOption) == "accept" ? UserPromptHandlerType.Accept : UserPromptHandlerType.Dismiss,
            [Environment.CurrentDirectory, .. parseResult.GetValue(allowUploadFromOption) ?? []],
            parseResult.GetValue(downloadsOption));
        Func<ParseResult, CancellationToken, Task<int>> Written(Func<ParseResult, CancellationToken, Task<CommandOutcome>> run) => WrittenAction(run, jsonOption, output, error);

        Command start = new("start", "Start a browser session that later commands drive, and that stops with session stop, when its browser closes, or when it is idle.") { sessionOption, browserOption, channelOption, headedOption, userDataDirectoryOption, idleTimeoutOption, maskPasswordsOption, allowOriginOption, allowFileUrlsOption, dialogsOption, allowUploadFromOption, downloadsOption, pathOption, jsonOption };
        start.SetAction(Written((parseResult, token) => StartSessionAsync(["session", "serve", .. args.Skip(2)], Settings(parseResult), host, error, token)));

        Command serve = new("serve", "Serve a session; session start runs it.") { Hidden = true };
        foreach (Option option in start.Options)
        {
            serve.Options.Add(option);
        }

        serve.Options.Add(detachedOption);
        serve.SetAction((parseResult, token) => SessionDaemon.ServeAsync(
            Settings(parseResult),
            createDownloadOptions(parseResult.GetValue(pathOption), progress),
            configureLauncher,
            SessionPaths.Prepare(host.Directory),
            host.TimeProvider,
            parseResult.GetValue(detachedOption),
            output,
            token));

        Command list = new("list", "List the sessions running.") { jsonOption };
        list.SetAction(Written((parseResult, token) => ListSessionsAsync(host, token)));

        Command stop = new("stop", "Stop a session, closing its browser.") { sessionOption, jsonOption };
        stop.SetAction(Written((parseResult, token) => StopSessionAsync(parseResult.GetValue(sessionOption)!, host, token)));

        return new Command("session", "Start, list, and stop browser sessions that commands drive.") { start, list, stop, serve };
    }

    // A session already running under the name is left alone; a socket nothing answers is what a session that did not
    // stop cleanly left, and is removed.
    private static async Task<CommandOutcome> StartSessionAsync(string[] serveArguments, SessionSettings settings, SessionHost host, TextWriter progress, CancellationToken cancellationToken)
    {
        string? problem = SessionPaths.CheckName(settings.Name);
        if (problem is null && settings.IdleTimeout <= TimeSpan.Zero)
        {
            problem = "--idle-timeout must be at least 1 minute.";
        }

        problem ??= settings.AllowedOrigins.FirstOrDefault(origin => !IsOrigin(origin)) is string notOrigin ? $"'{notOrigin}' is not an origin, such as https://example.com." : null;
        problem ??= settings.UploadDirectories.Append(settings.DownloadDirectory ?? Environment.CurrentDirectory).FirstOrDefault(directory => !Directory.Exists(directory)) is string notDirectory ? $"'{notDirectory}' is not a directory." : null;

        try
        {
            _ = settings.Channel is null ? default : Target.ParseChannel(settings.Browser, settings.Channel);
        }
        catch (ArgumentException ex)
        {
            problem ??= ex.Message;
        }

        if (problem is not null)
        {
            return CommandOutcome.Failure(new SessionError(CommandOutput.Usage, problem));
        }

        string socketPath = SessionPaths.SocketPath(SessionPaths.Prepare(host.Directory), settings.Name);
        if (File.Exists(socketPath))
        {
            SessionReply reply = await SessionClient.SendAsync(socketPath, settings.Name, "status", NoArguments, cancellationToken).ConfigureAwait(false);
            if (reply.Error is null)
            {
                return CommandOutcome.Failure(new SessionError("session-exists", $"A session named '{settings.Name}' is running.", "Stop it with 'dramaturge session stop', or name another with --session."));
            }

            File.Delete(socketPath);
        }

        if (await host.StartDaemon(serveArguments, progress, cancellationToken).ConfigureAwait(false) is string failure)
        {
            return CommandOutcome.Failure(new SessionError("start-failed", failure));
        }

        string browser = settings.Browser.ToString().ToLowerInvariant();
        return CommandOutcome.Success(new Dictionary<string, object?>() { ["name"] = settings.Name, ["browser"] = browser }, $"Started session '{settings.Name}' ({browser}).");
    }

    // Each socket that answers is a session running; one that does not is removed.
    private static async Task<CommandOutcome> ListSessionsAsync(SessionHost host, CancellationToken cancellationToken)
    {
        List<Dictionary<string, object?>> sessions = [];
        foreach (string socketPath in Directory.GetFiles(SessionPaths.Prepare(host.Directory), "*.sock").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(socketPath);
            SessionReply reply = await SessionClient.SendAsync(socketPath, name, "status", NoArguments, cancellationToken).ConfigureAwait(false);
            if (reply.Result is JsonElement status)
            {
                sessions.Add(new Dictionary<string, object?>() { ["name"] = name, ["browser"] = status.GetProperty("browser").GetString(), ["started"] = status.GetProperty("started").GetString() });
            }
            else
            {
                File.Delete(socketPath);
            }
        }

        string[] text = sessions.Count == 0 ? ["No sessions are running."] : [.. sessions.Select(session => $"{session["name"]}\t{session["browser"]}\tstarted {session["started"]}")];
        return CommandOutcome.Success(new Dictionary<string, object?>() { ["sessions"] = sessions }, text);
    }

    private static async Task<CommandOutcome> StopSessionAsync(string name, SessionHost host, CancellationToken cancellationToken)
    {
        if (SessionPaths.CheckName(name) is string problem)
        {
            return CommandOutcome.Failure(new SessionError(CommandOutput.Usage, problem));
        }

        string socketPath = SessionPaths.SocketPath(SessionPaths.Prepare(host.Directory), name);
        SessionReply reply = await SessionClient.SendAsync(socketPath, name, "stop", NoArguments, cancellationToken).ConfigureAwait(false);
        if (reply.Error is SessionError failure)
        {
            File.Delete(socketPath);
            return CommandOutcome.Failure(failure);
        }

        return CommandOutcome.Success(new Dictionary<string, object?>() { ["name"] = name }, $"Stopped session '{name}'.");
    }

    // An origin is an http or https address with nothing after its host and port.
    private static bool IsOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && SessionBrowser.Origin(uri) == origin.TrimEnd('/').ToLowerInvariant();
    }

    private static Func<ParseResult, CancellationToken, Task<int>> WrittenAction(Func<ParseResult, CancellationToken, Task<CommandOutcome>> run, Option<bool> jsonOption, TextWriter output, TextWriter error)
    {
        return async (parseResult, token) => CommandOutput.Write(await run(parseResult, token).ConfigureAwait(false), parseResult.GetValue(jsonOption), output, error);
    }

    // The commands that drive a session's browser, each sent to the session's daemon.
    private static Command[] CreatePageCommands(Option<string> sessionOption, Option<bool> jsonOption, SessionHost host, TextWriter output, TextWriter error)
    {
        Option<bool> noSnapshotOption = new("--no-snapshot") { Description = "Leave out the page's snapshot." };
        Argument<string> urlArgument = new("url") { Description = "The address to open, such as https://example.com/." };
        Argument<int> indexArgument = new("index") { Description = "The tab's number, as tabs lists it." };
        Option<string> outputOption = new("--output", "-o") { Description = "The file to save the screenshot in, as PNG.", Required = true };
        Option<bool> fullPageOption = new("--full-page") { Description = "Capture the whole page, not only what the window shows." };
        Option<bool> allOption = new("--all") { Description = "Show every message kept, not only those since the last console command." };
        Option<string?> screenshotRefOption = new("--ref") { Description = "The ref of an element to capture alone, such as e7." };
        Argument<string> refArgument = new("ref") { Description = "The element's ref, such as e7, from the current tab's latest snapshot." };
        Argument<string> textArgument = new("text") { Description = "The text to fill the field with, in place of what it has." };
        Argument<string> keyArgument = new("key") { Description = "The key, such as Enter, a, or Control+a." };
        Argument<string[]> valuesArgument = new("values") { Description = "The options to select, by the labels the snapshot shows.", Arity = ArgumentArity.OneOrMore };
        Argument<string[]> filesArgument = new("files") { Description = "The files to upload.", Arity = ArgumentArity.OneOrMore };
        Option<bool> doubleOption = new("--double") { Description = "Click twice, as a double click." };
        Option<string> buttonOption = new("--button") { Description = "The mouse button: left (the default), right, or middle.", DefaultValueFactory = _ => "left" };
        buttonOption.AcceptOnlyFromAmong("left", "right", "middle");
        Argument<string[]> conditionArgument = new("condition")
        {
            Description = "The condition: e5 visible, hidden, enabled, checked, or unchecked; e5 text <text> or value <value>; e5 snapshot [<template>]; or, of the tab, url <url>, title <title>, or text <text>.",
            Arity = ArgumentArity.OneOrMore,
        };
        Option<int?> timeoutOption = new("--timeout") { Description = "The seconds to wait for the condition; the default is 5." };
        Option<CodeTarget> codeTargetOption = new("--target") { Description = "The kind of file: program (the default), xunit, nunit, mstest, or tunit.", DefaultValueFactory = _ => CodeTarget.Program };
        Option<string?> codeOutputOption = new("--output", "-o") { Description = "A file to write the code to, instead of showing it." };
        Option<bool> resetOption = new("--reset") { Description = "Then start the recording over: the code comes from what is done next. Open the page the test starts from next." };
        Option<bool> byValueOption = new("--by-value") { Description = "Match options by their values, not their labels." };
        Command Page(string name, string description, Func<ParseResult, Dictionary<string, object?>> arguments, params Symbol[] symbols)
        {
            Command command = new(name, description);
            foreach (Symbol symbol in symbols)
            {
                if (symbol is Argument argument)
                {
                    command.Add(argument);
                }
                else
                {
                    command.Add((Option)symbol);
                }
            }

            command.Add(sessionOption);
            command.Add(jsonOption);
            command.SetAction(WrittenAction((parseResult, token) => SendToSessionAsync(host, parseResult.GetValue(sessionOption)!, name, arguments(parseResult), token), jsonOption, output, error));
            return command;
        }

        Dictionary<string, object?> Snapshot(ParseResult parseResult) => new() { ["snapshot"] = !parseResult.GetValue(noSnapshotOption) };
        Dictionary<string, object?> Act(ParseResult parseResult, Dictionary<string, object?>? more = null) => new([new("ref", parseResult.GetValue(refArgument)), new("snapshot", !parseResult.GetValue(noSnapshotOption)), .. more ?? []]);
        return
        [
            Page("open", "Open an address in the session's current tab, and show its snapshot.", parseResult => new() { ["url"] = parseResult.GetValue(urlArgument), ["snapshot"] = !parseResult.GetValue(noSnapshotOption) }, urlArgument, noSnapshotOption),
            Page("back", "Go back in the current tab's history, and show its snapshot.", Snapshot, noSnapshotOption),
            Page("forward", "Go forward in the current tab's history, and show its snapshot.", Snapshot, noSnapshotOption),
            Page("reload", "Reload the current tab, and show its snapshot.", Snapshot, noSnapshotOption),
            Page("url", "Show the current tab's address and title.", _ => new() { ["snapshot"] = false }),
            Page("snapshot", "Show the current tab's accessibility snapshot, whose refs, such as e7, name its elements.", _ => new() { ["snapshot"] = true }),
            Page("tabs", "List the session's tabs.", _ => []),
            Page("tab", "Make a tab the current one.", parseResult => new() { ["index"] = parseResult.GetValue(indexArgument) }, indexArgument),
            Page("screenshot", "Save a screenshot of the current tab, or of an element in it.", parseResult => new() { ["path"] = Path.GetFullPath(parseResult.GetValue(outputOption)!), ["fullPage"] = parseResult.GetValue(fullPageOption), ["ref"] = parseResult.GetValue(screenshotRefOption) }, outputOption, fullPageOption, screenshotRefOption),
            Page("click", "Click an element, and show what changed.", parseResult => Act(parseResult, new() { ["double"] = parseResult.GetValue(doubleOption), ["button"] = parseResult.GetValue(buttonOption) }), refArgument, doubleOption, buttonOption, noSnapshotOption),
            Page("fill", "Fill a field with text, and show what changed.", parseResult => Act(parseResult, new() { ["text"] = parseResult.GetValue(textArgument) }), refArgument, textArgument, noSnapshotOption),
            Page("press", "Press a key in an element, and show what changed.", parseResult => Act(parseResult, new() { ["key"] = parseResult.GetValue(keyArgument) }), refArgument, keyArgument, noSnapshotOption),
            Page("select", "Select options in a select element, and show what changed.", parseResult => Act(parseResult, new() { ["values"] = parseResult.GetValue(valuesArgument), ["byValue"] = parseResult.GetValue(byValueOption) }), refArgument, valuesArgument, byValueOption, noSnapshotOption),
            Page("check", "Check a checkbox or radio button, and show what changed.", parseResult => Act(parseResult), refArgument, noSnapshotOption),
            Page("uncheck", "Uncheck a checkbox, and show what changed.", parseResult => Act(parseResult), refArgument, noSnapshotOption),
            Page("hover", "Move the mouse over an element, and show what changed.", parseResult => Act(parseResult), refArgument, noSnapshotOption),
            Page("upload", "Choose files for a file input, and show what changed.", parseResult => Act(parseResult, new() { ["files"] = parseResult.GetValue(filesArgument)!.Select(Path.GetFullPath).ToArray() }), refArgument, filesArgument, noSnapshotOption),
            Page("expect", "Wait until a condition is met, and assert it; the session's code records the assertion.", parseResult => new() { ["condition"] = parseResult.GetValue(conditionArgument), ["timeout"] = parseResult.GetValue(timeoutOption) }, conditionArgument, timeoutOption),
            Page("code", "Show the session's recording as C#: a program, or, with --target, a test class.", parseResult => new() { ["target"] = parseResult.GetValue(codeTargetOption).ToString().ToLowerInvariant(), ["path"] = parseResult.GetValue(codeOutputOption) is string path ? Path.GetFullPath(path) : null, ["reset"] = parseResult.GetValue(resetOption) }, codeTargetOption, codeOutputOption, resetOption),
            Page("console", "Show the console messages, and uncaught errors, of the session's tabs since the last console command.", parseResult => new() { ["all"] = parseResult.GetValue(allOption) }, allOption),
        ];
    }

    private static async Task<CommandOutcome> SendToSessionAsync(SessionHost host, string name, string command, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        if (SessionPaths.CheckName(name) is string problem)
        {
            return CommandOutcome.Failure(new SessionError(CommandOutput.Usage, problem));
        }

        SessionReply reply = await SessionClient.SendAsync(SessionPaths.SocketPath(SessionPaths.Prepare(host.Directory), name), name, command, arguments, cancellationToken).ConfigureAwait(false);
        return reply.Result is JsonElement result ? CommandOutcome.Success(result, SessionText.Render(command, result)) : CommandOutcome.Failure(reply.Error!);
    }

    private static string CommandPath(System.CommandLine.Parsing.CommandResult command)
    {
        return command.Parent is System.CommandLine.Parsing.CommandResult parent ? $"{CommandPath(parent)} {command.Command.Name}" : command.Command.Name;
    }

    private static BrowserDownloadOptions CreateDefaultDownloadOptions(string? path, IProgress<BrowserDownloadProgress> progress)
    {
        return path is null ? new BrowserDownloadOptions() { Progress = progress } : new BrowserDownloadOptions() { CacheDirectory = path, Progress = progress };
    }

    private static async Task<int> InstallAsync(string[] targetTexts, bool dryRun, BrowserDownloadOptions options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (ParseTargets(targetTexts, Installer.ValidateForInstall, error) is not List<Target> targets)
        {
            return 1;
        }

        int exitCode = 0;
        foreach (Target target in targets)
        {
            try
            {
                if (dryRun)
                {
                    ResolvedDownload resolved = await Installer.ResolveAsync(target, options, cancellationToken).ConfigureAwait(false);
                    output.WriteLine($"{target.Text}: {resolved} ({(resolved.IsCached ? "cached" : "to download")}) {resolved.Url}");
                }
                else
                {
                    string path = await Installer.InstallAsync(target, options, cancellationToken).ConfigureAwait(false);
                    output.WriteLine($"{target.Text}: {path}");
                }
            }
            catch (Exception ex) when (ex is WebDriverBiDiException || ex is ArgumentException || ex is NotSupportedException)
            {
                error.WriteLine($"{target.Text}: {ex.Message}");
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private static int List(BrowserDownloadOptions options, TextWriter output, TextWriter error)
    {
        IReadOnlyList<CachedInstallation> installations;
        try
        {
            installations = BrowserCache.List(options);
        }
        catch (BrowserDownloadException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        if (installations.Count == 0)
        {
            output.WriteLine($"No browsers or drivers are cached in {options.CacheDirectory}.");
            return 0;
        }

        foreach (CachedInstallation installation in installations)
        {
            string size = string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", installation.Size / 1048576.0);
            string resolved = installation.LastResolved is DateTimeOffset lastResolved ? lastResolved.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-";
            output.WriteLine($"{installation,-40} {installation.Channel ?? "driver",-8} {size,10}  {resolved,-10}  {installation.Directory}");
        }

        return 0;
    }

    private static async Task<int> ClearAsync(string[] targetTexts, bool dryRun, BrowserDownloadOptions options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (ParseTargets(targetTexts, Installer.ValidateForRemoval, error) is not List<Target> targets)
        {
            return 1;
        }

        List<CachedInstallation> selected;
        try
        {
            selected = [.. BrowserCache.List(options).Where(installation => targets.Count == 0 || targets.Any(target => Installer.Selects(target, installation)))];
        }
        catch (BrowserDownloadException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        if (selected.Count == 0)
        {
            output.WriteLine("Nothing to remove.");
            return 0;
        }

        int exitCode = 0;
        foreach (CachedInstallation installation in selected)
        {
            if (dryRun)
            {
                output.WriteLine($"Would remove {installation} ({installation.Channel ?? "driver"})");
                continue;
            }

            try
            {
                await BrowserCache.RemoveAsync(installation, options, cancellationToken).ConfigureAwait(false);
                output.WriteLine($"Removed {installation} ({installation.Channel ?? "driver"})");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is WebDriverBiDiException)
            {
                error.WriteLine($"{installation}: {ex.Message}");
                exitCode = 1;
            }
        }

        return exitCode;
    }

    // Every target is checked before anything is done, so that a mistyped one does not leave the rest half done.
    private static List<Target>? ParseTargets(string[] targetTexts, Action<Target> validate, TextWriter error)
    {
        List<Target> targets = [];
        bool isValid = true;
        foreach (string text in targetTexts)
        {
            try
            {
                Target target = Target.Parse(text);
                validate(target);
                targets.Add(target);
            }
            catch (ArgumentException ex)
            {
                error.WriteLine(ex.Message);
                isValid = false;
            }
        }

        return isValid ? targets : null;
    }
}
