// <copyright file="SessionPaths.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

/// <summary>
/// Where sessions' sockets are: one per session, named for it, in a directory only the current user can reach.
/// </summary>
internal static class SessionPaths
{
    /// <summary>
    /// The name of the session a command acts on when it names none.
    /// </summary>
    public const string DefaultName = "default";

    // Short enough that the socket's path stays within the 104 bytes macOS allows.
    private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks a session's name, which becomes part of a file name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>Why the name cannot be used, or <see langword="null"/> if it can.</returns>
    public static string? CheckName(string name)
    {
        return NamePattern.IsMatch(name) ? null : $"'{name}' is not a session name: use letters, digits, '-', and '_', starting with a letter or digit, up to 32 characters.";
    }

    /// <summary>
    /// Gets the socket of a session.
    /// </summary>
    /// <param name="directory">The sessions' directory.</param>
    /// <param name="name">The session's name.</param>
    /// <returns>The socket's path.</returns>
    public static string SocketPath(string directory, string name) => Path.Combine(directory, $"{name}.sock");

    /// <summary>
    /// Gets the sessions' directory, creating it, if need be, so that only the current user can reach it.
    /// </summary>
    /// <param name="directory">The directory to create, if it does not exist.</param>
    /// <returns>The same directory, ready for sockets.</returns>
    public static string Prepare(string directory)
    {
        CreateForUser(directory);
        return directory;
    }

    /// <summary>
    /// Makes a socket's file the current user's alone.
    /// </summary>
    /// <param name="path">The socket's path.</param>
    [ExcludeFromCodeCoverage] // Each system takes one branch.
    public static void RestrictToUser(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Gets the current user's own directory for sessions: on Linux, in the user's runtime directory, or else in
    /// /tmp; on macOS, in the user's own temporary directory; on Windows, in the user's local application data.
    /// </summary>
    /// <returns>The directory.</returns>
    [ExcludeFromCodeCoverage] // The tests give a directory of their own.
    public static string DefaultDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dramaturge", "sessions");
        }

        string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return OperatingSystem.IsMacOS() || string.IsNullOrEmpty(runtime)
            ? Path.Combine(OperatingSystem.IsMacOS() ? Path.GetTempPath() : "/tmp", OperatingSystem.IsMacOS() ? "dramaturge" : $"dramaturge-{Environment.UserName}")
            : Path.Combine(runtime, "dramaturge");
    }

    // A directory in the user's local application data is the user's alone already.
    [ExcludeFromCodeCoverage] // Each system takes one branch.
    private static void CreateForUser(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
