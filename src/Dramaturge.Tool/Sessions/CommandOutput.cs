// <copyright file="CommandOutput.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Text.Json;

/// <summary>
/// Writes what a command produced, as text or, with --json, as one line of JSON, and gives its exit code: 0 on
/// success, 2 for arguments that cannot be used, 3 when the session named is not running, and 1 otherwise.
/// </summary>
internal static class CommandOutput
{
    /// <summary>
    /// The code of the error for arguments that cannot be used.
    /// </summary>
    public const string Usage = "usage";

    /// <summary>
    /// Writes a command's result. As text, a result goes to the output and an error to the error writer, as
    /// <c>error[code]: message — hint</c>, followed by any details between the page content lines; as JSON, either
    /// goes to the output.
    /// </summary>
    /// <param name="result">What the command produced.</param>
    /// <param name="json">A value indicating whether to write JSON.</param>
    /// <param name="output">The writer for results.</param>
    /// <param name="error">The writer for errors.</param>
    /// <returns>The exit code.</returns>
    public static int Write(CommandOutcome result, bool json, TextWriter output, TextWriter error)
    {
        if (json)
        {
            output.WriteLine(result.Error is SessionError failure ? SessionWire.Error(failure) : SessionWire.Result(result.Result!));
        }
        else if (result.Error is SessionError failure)
        {
            error.WriteLine(failure.Hint is null ? $"error[{failure.Code}]: {failure.Message}" : $"error[{failure.Code}]: {failure.Message} — {failure.Hint}");
            if (failure.Details is not null)
            {
                foreach (string line in (string[])[SessionText.ContentStart, .. failure.Details.Split('\n'), SessionText.ContentEnd])
                {
                    error.WriteLine(line);
                }
            }
        }
        else
        {
            foreach (string line in result.Text)
            {
                output.WriteLine(line);
            }

            // Notes go apart from the result, which can be a file's content.
            if (result.Result is JsonElement answer && answer.TryGetProperty("notes", out JsonElement notes))
            {
                foreach (JsonElement note in notes.EnumerateArray())
                {
                    error.WriteLine($"note: {note.GetString()}");
                }
            }
        }

        return result.Error?.Code switch
        {
            null => 0,
            Usage => 2,
            SessionClient.NoSession => 3,
            _ => 1,
        };
    }
}
