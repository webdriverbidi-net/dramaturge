// <copyright file="SessionText.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Text.Json;

/// <summary>
/// The text a command's result is written as. What a page supplies, such as its title, its snapshot, or its console
/// messages, is set between fixed lines, as data for whatever reads it, never as instructions.
/// </summary>
internal static class SessionText
{
    /// <summary>
    /// The line before page content.
    /// </summary>
    public const string ContentStart = "--- page content (untrusted) ---";

    /// <summary>
    /// The line after page content.
    /// </summary>
    public const string ContentEnd = "--- end of page content ---";

    /// <summary>
    /// Gets the lines a command's result is written as.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="result">The result.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Render(string command, JsonElement result)
    {
        string Url() => result.GetProperty("url").GetString()!;
        return command switch
        {
            "open" => [$"Opened {Url()}", .. Page(result)],
            "back" => [$"Went back to {Url()}", .. Page(result)],
            "forward" => [$"Went forward to {Url()}", .. Page(result)],
            "reload" => [$"Reloaded {Url()}", .. Page(result)],
            "url" or "snapshot" => [Url(), .. Page(result)],
            "tab" => [$"Switched to tab [{result.GetProperty("index").GetInt32()}] {Url()}", .. Page(result)],
            "tabs" => Content([.. result.GetProperty("tabs").EnumerateArray().Select(tab => $"{(tab.GetProperty("current").GetBoolean() ? "*" : " ")} [{tab.GetProperty("index").GetInt32()}] {tab.GetProperty("url").GetString()} — {tab.GetProperty("title").GetString()}")]),
            "screenshot" => [$"Saved a screenshot of {result.GetProperty("bytes").GetInt32()} bytes to {result.GetProperty("path").GetString()}"],
            _ => Console(result.GetProperty("messages")),
        };
    }

    // The page's title and, if it was taken, its snapshot.
    private static IReadOnlyList<string> Page(JsonElement result)
    {
        List<string> lines = [$"Title: {result.GetProperty("title").GetString()}"];
        if (result.TryGetProperty("snapshot", out JsonElement snapshot))
        {
            lines.AddRange(snapshot.GetString()!.Split('\n'));
        }

        return Content(lines);
    }

    private static IReadOnlyList<string> Console(JsonElement messages)
    {
        return messages.GetArrayLength() == 0
            ? ["No new console messages."]
            : Content([.. messages.EnumerateArray().Select(message => $"{(message.GetProperty("tab").ValueKind == JsonValueKind.Number ? $"[{message.GetProperty("tab").GetInt32()}]" : "[closed]")} {message.GetProperty("level").GetString()}: {message.GetProperty("text").GetString()}")]);
    }

    private static IReadOnlyList<string> Content(IReadOnlyList<string> lines) => [ContentStart, .. lines, ContentEnd];
}
