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
            "click" or "fill" or "press" or "select" or "check" or "uncheck" or "hover" or "upload" => Action(command, result),
            "expect" => result.TryGetProperty("template", out JsonElement template)
                ? [$"Passed: {result.GetProperty("condition").GetString()}.", .. Content(template.GetString()!.Split('\n'))]
                : [$"Passed: {result.GetProperty("condition").GetString()}."],
            "code" => result.TryGetProperty("path", out JsonElement path)
                ? [$"Wrote the code for {result.GetProperty("target").GetString()} to {path.GetString()}."]
                : result.GetProperty("code").GetString()!.TrimEnd('\n').Split('\n'),
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

    // What the action did and led to, and the snapshot after it: in full after a navigation, or when its changes would
    // be longer, and otherwise its changes.
    private static IReadOnlyList<string> Action(string command, JsonElement result)
    {
        string reference = result.GetProperty("ref").GetString()!;
        string done = command switch
        {
            "click" => $"{(result.GetProperty("double").GetBoolean() ? "Double-clicked" : "Clicked")} {reference}",
            "fill" => $"Filled {reference}",
            "press" => $"Pressed {result.GetProperty("key").GetString()} on {reference}",
            "select" => $"Selected {Count(result.GetProperty("selected").GetArrayLength(), "option")} in {reference}",
            "check" => $"Checked {reference}",
            "uncheck" => $"Unchecked {reference}",
            "hover" => $"Hovered over {reference}",
            _ => $"Uploaded {Count(result.GetProperty("files").GetInt32(), "file")} to {reference}",
        };
        bool navigated = result.GetProperty("navigated").GetBoolean();
        List<string> lines = [navigated ? $"{done}, and the page navigated to {result.GetProperty("url").GetString()}" : done];
        if (result.TryGetProperty("snapshotError", out _))
        {
            lines.Add("No snapshot was taken, as the page was changing. Run 'dramaturge snapshot' to see it.");
        }

        List<string> content = [.. result.GetProperty("effects").EnumerateArray().Select(Effect)];
        if (navigated)
        {
            content.Add($"Title: {result.GetProperty("title").GetString()}");
        }
        else if (result.TryGetProperty("snapshot", out _))
        {
            content.Add("Snapshot:");
        }

        if (result.TryGetProperty("snapshot", out JsonElement snapshot) && snapshot.ValueKind == JsonValueKind.String)
        {
            content.AddRange(snapshot.GetString()!.Split('\n'));
        }
        else if (result.TryGetProperty("changes", out JsonElement changes))
        {
            if (changes.GetArrayLength() == 0)
            {
                lines.Add("The snapshot did not change.");
            }
            else
            {
                content.Add("Changes:");
                content.AddRange(changes.EnumerateArray().Select(line => line.GetString()!));
            }
        }

        return content.Count == 0 ? lines : [.. lines, .. Content(content)];
    }

    private static string Effect(JsonElement effect)
    {
        string Text(string name) => effect.GetProperty(name).GetString()!;
        return Text("kind") switch
        {
            "dialog" => $"Dialog ({Text("type")}): \"{Text("message")}\" — {Text("answer")}",
            "download" => $"Download: {Text("fileName")} from {Text("url")}{(effect.GetProperty("saved").GetBoolean() ? string.Empty : " — refused; start the session with --downloads <dir> to save downloads")}",
            "tab" => $"Opened tab [{effect.GetProperty("index").GetInt32()}] {Text("url")}",
            _ => $"Console error: {Text("text")}",
        };
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static IReadOnlyList<string> Console(JsonElement messages)
    {
        return messages.GetArrayLength() == 0
            ? ["No new console messages."]
            : Content([.. messages.EnumerateArray().Select(message => $"{(message.GetProperty("tab").ValueKind == JsonValueKind.Number ? $"[{message.GetProperty("tab").GetInt32()}]" : "[closed]")} {message.GetProperty("level").GetString()}: {message.GetProperty("text").GetString()}")]);
    }

    private static IReadOnlyList<string> Content(IReadOnlyList<string> lines) => [ContentStart, .. lines, ContentEnd];
}
