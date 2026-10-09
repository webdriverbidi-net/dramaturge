// <copyright file="DaemonAnswer.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// What a daemon answers a command with: its result, or why it failed.
/// </summary>
/// <param name="Result">The result, or <see langword="null"/> when the command failed.</param>
/// <param name="Error">Why the command failed, or <see langword="null"/> when it succeeded.</param>
internal sealed record DaemonAnswer(IReadOnlyDictionary<string, object?>? Result, SessionError? Error)
{
    /// <summary>
    /// Gets an answer that succeeded.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The answer.</returns>
    public static DaemonAnswer Of(IReadOnlyDictionary<string, object?> result) => new(result, null);

    /// <summary>
    /// Gets an answer that failed.
    /// </summary>
    /// <param name="code">The error's code.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="hint">What to do about it, or <see langword="null"/>.</param>
    /// <returns>The answer.</returns>
    public static DaemonAnswer Failed(string code, string message, string? hint = null) => new(null, new SessionError(code, message, hint));
}
