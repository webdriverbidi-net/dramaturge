// <copyright file="SessionSettings.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Globalization;
using Dramaturge.Browsers;
using WebDriverBiDi.Session;

/// <summary>
/// The settings a session is started with, fixed for its life.
/// </summary>
/// <param name="Name">The session's name.</param>
/// <param name="Browser">The browser.</param>
/// <param name="Channel">The browser's release channel, as a target names it, or <see langword="null"/> for the default.</param>
/// <param name="Headed">A value indicating whether the browser has a window.</param>
/// <param name="UserDataDirectory">The browser's profile directory, or <see langword="null"/> for a temporary one.</param>
/// <param name="IdleTimeout">How long the daemon waits for a command before it stops.</param>
/// <param name="MaskPasswords">A value indicating whether the session's code masks passwords.</param>
/// <param name="AllowedOrigins">The origins the session's pages may navigate to, as given, such as <c>https://example.com</c>, or none for any.</param>
/// <param name="AllowFileUrls">A value indicating whether the session may open <c>file:</c> URLs.</param>
/// <param name="Dialogs">How the browser answers dialogs: <see cref="UserPromptHandlerType.Dismiss"/> or <see cref="UserPromptHandlerType.Accept"/>.</param>
/// <param name="UploadDirectories">The directories files may be uploaded from, with those below them: the directory the session was started in, then those given, as given.</param>
/// <param name="DownloadDirectory">The directory downloads are saved in, as given, or <see langword="null"/> to refuse them.</param>
internal sealed record SessionSettings(string Name, BrowserKind Browser, string? Channel, bool Headed, string? UserDataDirectory, TimeSpan IdleTimeout, bool MaskPasswords, IReadOnlyList<string> AllowedOrigins, bool AllowFileUrls, UserPromptHandlerType Dialogs, IReadOnlyList<string> UploadDirectories, string? DownloadDirectory)
{
    /// <summary>
    /// Gets the arguments of the <c>session serve</c> that runs a daemon with these settings. The directory the
    /// daemon is started in is its first upload directory, so only the others are passed.
    /// </summary>
    /// <param name="cachePath">The browser cache directory, or <see langword="null"/> for the default.</param>
    /// <returns>The arguments.</returns>
    public IReadOnlyList<string> ServeArguments(string? cachePath)
    {
        List<string> arguments = ["session", "serve", "--session", this.Name, "--browser", this.Browser.ToString(), "--idle-timeout", ((int)this.IdleTimeout.TotalMinutes).ToString(CultureInfo.InvariantCulture), "--dialogs", this.Dialogs == UserPromptHandlerType.Accept ? "accept" : "dismiss"];
        void Add(string option, string? value)
        {
            if (value is not null)
            {
                arguments.Add(option);
                arguments.Add(value);
            }
        }

        Add("--channel", this.Channel);
        Add("--user-data-dir", this.UserDataDirectory);
        Add("--downloads", this.DownloadDirectory);
        Add("--path", cachePath);
        foreach (string origin in this.AllowedOrigins)
        {
            Add("--allow-origin", origin);
        }

        foreach (string directory in this.UploadDirectories.Skip(1))
        {
            Add("--allow-upload-from", directory);
        }

        if (this.Headed)
        {
            arguments.Add("--headed");
        }

        if (this.MaskPasswords)
        {
            arguments.Add("--mask-passwords");
        }

        if (this.AllowFileUrls)
        {
            arguments.Add("--allow-file-urls");
        }

        return arguments;
    }
}
