// <copyright file="CommandOutcome.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// What a command for sessions produced: its result, and the lines of text that say it, or why it failed.
/// </summary>
/// <param name="Result">The result, or <see langword="null"/> when the command failed.</param>
/// <param name="Text">The lines that say the result.</param>
/// <param name="Error">Why the command failed, or <see langword="null"/> when it succeeded.</param>
internal sealed record CommandOutcome(IReadOnlyDictionary<string, object?>? Result, IReadOnlyList<string> Text, SessionError? Error)
{
    /// <summary>
    /// Gets a result that succeeded.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="text">The lines that say it.</param>
    /// <returns>The outcome.</returns>
    public static CommandOutcome Success(IReadOnlyDictionary<string, object?> result, params IReadOnlyList<string> text) => new(result, text, null);

    /// <summary>
    /// Gets a result that failed.
    /// </summary>
    /// <param name="error">Why.</param>
    /// <returns>The outcome.</returns>
    public static CommandOutcome Failure(SessionError error) => new(null, [], error);
}
