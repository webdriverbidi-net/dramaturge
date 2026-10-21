// <copyright file="SessionOptions.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.CommandLine;
using Dramaturge.Browsers;
using WebDriverBiDi.Session;

/// <summary>
/// The command-line options that fix what a session allows, shared by every command that starts one: <c>session
/// start</c>, the <c>session serve</c> it runs, and <c>mcp</c>, whose host's sessions all get them.
/// </summary>
internal sealed class SessionOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionOptions"/> class.
    /// </summary>
    public SessionOptions()
    {
        this.Dialogs.AcceptOnlyFromAmong("dismiss", "accept");
    }

    /// <summary>
    /// Gets the option that shows the browser's window.
    /// </summary>
    public Option<bool> Headed { get; } = new("--headed") { Description = "Show the browser's window. By default the browser has none." };

    /// <summary>
    /// Gets the option of the browser's profile directory.
    /// </summary>
    public Option<string?> UserDataDirectory { get; } = new("--user-data-dir") { Description = "A profile directory for the browser to keep. By default it gets a temporary one." };

    /// <summary>
    /// Gets the option of the idle timeout, in minutes.
    /// </summary>
    public Option<int> IdleTimeout { get; } = new("--idle-timeout") { Description = "The minutes the session waits for a command before it stops.", DefaultValueFactory = _ => 30 };

    /// <summary>
    /// Gets the option that masks passwords in the session's code.
    /// </summary>
    public Option<bool> MaskPasswords { get; } = new("--mask-passwords") { Description = "Write values typed into, or expected of, password fields in the session's code as a read of the DRAMATURGE_PASSWORD environment variable." };

    /// <summary>
    /// Gets the option of the origins the session may navigate to.
    /// </summary>
    public Option<string[]> AllowOrigin { get; } = new("--allow-origin") { Description = "An origin, such as https://example.com, the session's pages may navigate to; give it once for each. By default every origin is allowed." };

    /// <summary>
    /// Gets the option that lets the session open file: URLs.
    /// </summary>
    public Option<bool> AllowFileUrls { get; } = new("--allow-file-urls") { Description = "Let the session open file: URLs." };

    /// <summary>
    /// Gets the option of how dialogs are answered.
    /// </summary>
    public Option<string> Dialogs { get; } = new("--dialogs") { Description = "How the browser answers the dialogs pages open, such as confirm: dismiss (the default) or accept.", DefaultValueFactory = _ => "dismiss" };

    /// <summary>
    /// Gets the option of the directories files may be uploaded from.
    /// </summary>
    public Option<string[]> AllowUploadFrom { get; } = new("--allow-upload-from") { Description = "A directory files may be uploaded from, with those below it, besides the one the session is started in; give it once for each." };

    /// <summary>
    /// Gets the option of the directory downloads are saved in.
    /// </summary>
    public Option<string?> Downloads { get; } = new("--downloads") { Description = "A directory to save downloads in. By default the session refuses them." };

    /// <summary>
    /// Gets the options, in the order they are shown.
    /// </summary>
    public IReadOnlyList<Option> All => [this.Headed, this.UserDataDirectory, this.IdleTimeout, this.MaskPasswords, this.AllowOrigin, this.AllowFileUrls, this.Dialogs, this.AllowUploadFrom, this.Downloads];

    /// <summary>
    /// Gets a session's settings from a command line.
    /// </summary>
    /// <param name="parseResult">The command line.</param>
    /// <param name="name">The session's name.</param>
    /// <param name="browser">The browser.</param>
    /// <param name="channel">The browser's release channel, or <see langword="null"/> for the default.</param>
    /// <returns>The settings. Files may be uploaded from the current directory, besides those given.</returns>
    public SessionSettings Settings(ParseResult parseResult, string name, BrowserKind browser, string? channel) => new(
        name,
        browser,
        channel,
        parseResult.GetValue(this.Headed),
        parseResult.GetValue(this.UserDataDirectory),
        TimeSpan.FromMinutes(parseResult.GetValue(this.IdleTimeout)),
        parseResult.GetValue(this.MaskPasswords),
        parseResult.GetValue(this.AllowOrigin)!,
        parseResult.GetValue(this.AllowFileUrls),
        parseResult.GetValue(this.Dialogs) == "accept" ? UserPromptHandlerType.Accept : UserPromptHandlerType.Dismiss,
        [Environment.CurrentDirectory, .. parseResult.GetValue(this.AllowUploadFrom)!],
        parseResult.GetValue(this.Downloads));
}
