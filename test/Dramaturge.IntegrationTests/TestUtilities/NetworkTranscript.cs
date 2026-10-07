// <copyright file="NetworkTranscript.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.TestUtilities;

using System.Collections.Concurrent;
using WebDriverBiDi;

/// <summary>
/// The protocol messages about the network that a group exchanges with its browser, kept to explain a failure that
/// only a CI machine shows.
/// </summary>
public sealed class NetworkTranscript
{
    private const int MaxLineLength = 800;

    private readonly ConcurrentQueue<string> lines = new();

    private NetworkTranscript()
    {
    }

    /// <summary>
    /// Starts recording a group's network messages.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <returns>The transcript.</returns>
    public static NetworkTranscript Record(BrowserGroup group)
    {
        NetworkTranscript transcript = new();
        group.Driver.TransportConfiguration.LogLevel = WebDriverBiDiLogLevel.Trace;
        group.Driver.OnLogMessage.AddObserver(e =>
        {
            if (e.Message.Contains("network.", StringComparison.Ordinal))
            {
                transcript.lines.Enqueue(e.Message.Length > MaxLineLength ? e.Message[..MaxLineLength] + "…" : e.Message);
            }
        });
        return transcript;
    }

    /// <summary>
    /// Runs a step of a test, adding the transcript to the message of any failure.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <returns>A task that completes when the step does.</returns>
    public async Task ExplainAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{ex.Message}{Environment.NewLine}Network messages:{Environment.NewLine}{string.Join(Environment.NewLine, this.lines)}", ex);
        }
    }
}
