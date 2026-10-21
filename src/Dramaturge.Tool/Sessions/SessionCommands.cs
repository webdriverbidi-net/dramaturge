// <copyright file="SessionCommands.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// The catalogue of the commands that drive a session's browser. Every front end builds its commands from it, so a
/// new command is one entry here and its implementation in <see cref="SessionBrowser"/>.
/// </summary>
internal static class SessionCommands
{
    private static readonly SessionParameter Ref = new("ref", ParameterKind.Text, "The element's ref, such as e7, from the current tab's latest snapshot.", ParameterForm.Argument);
    private static readonly SessionParameter Snapshot = new("snapshot", ParameterKind.Flag, "Leave out the page's snapshot.", ParameterForm.InvertedOption, "--no-snapshot");

    /// <summary>
    /// Gets the commands, in the order they are offered.
    /// </summary>
    public static IReadOnlyList<SessionCommand> Page { get; } =
    [
        new("open", "Open an address in the session's current tab, and show its snapshot.", [new("url", ParameterKind.Text, "The address to open, such as https://example.com/.", ParameterForm.Argument), Snapshot]),
        new("back", "Go back in the current tab's history, and show its snapshot.", [Snapshot]),
        new("forward", "Go forward in the current tab's history, and show its snapshot.", [Snapshot]),
        new("reload", "Reload the current tab, and show its snapshot.", [Snapshot]),
        new("url", "Show the current tab's address and title.", [], new Dictionary<string, object?>() { ["snapshot"] = false }),
        new("snapshot", "Show the current tab's accessibility snapshot, whose refs, such as e7, name its elements.", [], new Dictionary<string, object?>() { ["snapshot"] = true }),
        new("tabs", "List the session's tabs.", []),
        new("tab", "Make a tab the current one.", [new("index", ParameterKind.Number, "The tab's number, as tabs lists it.", ParameterForm.Argument)]),
        new(
            "screenshot",
            "Save a screenshot of the current tab, or of an element in it.",
            [
                new("path", ParameterKind.Path, "The file to save the screenshot in, as PNG.", CliName: "--output", Alias: "-o", Required: true),
                new("fullPage", ParameterKind.Flag, "Capture the whole page, not only what the window shows.", CliName: "--full-page"),
                new("ref", ParameterKind.Text, "The ref of an element to capture alone, such as e7."),
            ]),
        new(
            "click",
            "Click an element, and show what changed.",
            [
                Ref,
                new("double", ParameterKind.Flag, "Click twice, as a double click."),
                new("button", ParameterKind.Text, "The mouse button: left (the default), right, or middle.", Choices: ["left", "right", "middle"], Default: "left"),
                Snapshot,
            ]),
        new("fill", "Fill a field with text, and show what changed.", [Ref, new("text", ParameterKind.Text, "The text to fill the field with, in place of what it has.", ParameterForm.Argument), Snapshot]),
        new("press", "Press a key in an element, and show what changed.", [Ref, new("key", ParameterKind.Text, "The key, such as Enter, a, or Control+a.", ParameterForm.Argument), Snapshot]),
        new(
            "select",
            "Select options in a select element, and show what changed.",
            [
                Ref,
                new("values", ParameterKind.Texts, "The options to select, by the labels the snapshot shows.", ParameterForm.Argument),
                new("byValue", ParameterKind.Flag, "Match options by their values, not their labels.", CliName: "--by-value"),
                Snapshot,
            ]),
        new("check", "Check a checkbox or radio button, and show what changed.", [Ref, Snapshot]),
        new("uncheck", "Uncheck a checkbox, and show what changed.", [Ref, Snapshot]),
        new("hover", "Move the mouse over an element, and show what changed.", [Ref, Snapshot]),
        new("upload", "Choose files for a file input, and show what changed.", [Ref, new("files", ParameterKind.Paths, "The files to upload.", ParameterForm.Argument), Snapshot]),
        new(
            "expect",
            "Wait until a condition is met, and assert it; the session's code records the assertion.",
            [
                new("condition", ParameterKind.Texts, "The condition: e5 visible, hidden, enabled, checked, or unchecked; e5 text <text> or value <value>; e5 snapshot [<template>]; or, of the tab, url <url>, title <title>, or text <text>.", ParameterForm.Argument),
                new("timeout", ParameterKind.Number, "The seconds to wait for the condition; the default is 5."),
            ]),
        new(
            "code",
            "Show the session's recording as C#: a program, or, with --target, a test class.",
            [
                new("target", ParameterKind.Text, "The kind of file: program (the default), xunit, nunit, mstest, or tunit.", Choices: ["program", "xunit", "nunit", "mstest", "tunit"], Default: "program"),
                new("path", ParameterKind.Path, "A file to write the code to, instead of showing it.", CliName: "--output", Alias: "-o"),
                new("reset", ParameterKind.Flag, "Then start the recording over: the code comes from what is done next. Open the page the test starts from next."),
            ]),
        new("console", "Show the console messages, and uncaught errors, of the session's tabs since the last console command.", [new("all", ParameterKind.Flag, "Show every message kept, not only those since the last console command.")]),
    ];
}
