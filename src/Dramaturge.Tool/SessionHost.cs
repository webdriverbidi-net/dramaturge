// <copyright file="SessionHost.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

using Dramaturge.Tool.Sessions;

/// <summary>
/// Where the tool keeps its browser sessions, how it starts a session's daemon, and the clock the daemon's idle
/// timeout runs on, each of which a test can replace.
/// </summary>
public sealed class SessionHost
{
    /// <summary>
    /// Gets the directory of the sessions' sockets. The default is the current user's own directory.
    /// </summary>
    public string Directory { get; init; } = SessionPaths.DefaultDirectory();

    /// <summary>
    /// Gets the starter of a session's daemon. Given the arguments of <c>session serve</c> and the writer for the
    /// daemon's progress, such as a browser download's, it starts the daemon and returns <see langword="null"/> once
    /// the daemon is ready, or why it failed. The default starts the tool again as a detached process.
    /// </summary>
    public Func<IReadOnlyList<string>, TextWriter, CancellationToken, Task<string?>> StartDaemon { get; init; } = DaemonProcess.StartAsync;

    /// <summary>
    /// Gets the clock of a daemon's idle timeout. The default is the system's.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Gets the streams the <c>mcp</c> command serves its host on, or <see langword="null"/> for the console's
    /// standard input and output.
    /// </summary>
    public StandardStreams? Streams { get; init; }
}
