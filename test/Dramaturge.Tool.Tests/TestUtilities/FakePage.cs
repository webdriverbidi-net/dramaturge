// <copyright file="FakePage.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.TestUtilities;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dramaturge.TestUtilities;

/// <summary>
/// A fake browser's page, as its scripts describe it: a snapshot whose refs are all connected, visible, and ready
/// for any action, a title, and texts to read.
/// </summary>
public sealed partial class FakePage
{
    /// <summary>
    /// Gets or sets the page's accessibility snapshot.
    /// </summary>
    public string Snapshot { get; set; } = "- heading \"Welcome\" [level=1] [ref=e1]\n- button \"Add\" [ref=e2]";

    /// <summary>
    /// Makes a session's browser answer for this page: its scripts, and a body for any role lookup.
    /// </summary>
    /// <param name="session">The session.</param>
    public void Attach(FakeSession session)
    {
        session.RemoteEnd.AnswerWith("script.callFunction", this.Answer);
        session.RemoteEnd.AnswerWith("browsingContext.locateNodes", _ => new JsonObject() { ["nodes"] = new JsonArray(Element("body-1")) });
    }

    /// <summary>
    /// Answers a script call as the page.
    /// </summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The answer.</returns>
    public FakeResponse Answer(JsonObject parameters)
    {
        string function = (string)parameters["functionDeclaration"]!;
        JsonObject value = function.Contains("snapshots.snapshot") ? SnapshotValue(this.Snapshot)
            : function.Contains("document.title") ? Text("Fake title")
            : function.Contains("document.readyState") ? Text("complete")
            : function.Contains("isConnected") ? Element((string)parameters["arguments"]![0]!["sharedId"]!)
            : function.Contains("isInteractionReady") ? Readiness()
            : function.Contains("queryElementStates") ? new JsonObject() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("status", Text("success"))) }
            : function.Contains("isElementVisible") ? new JsonObject() { ["type"] = "boolean", ["value"] = true }
            : function.Contains("readTexts") ? new JsonObject() { ["type"] = "array", ["value"] = new JsonArray(Text("Welcome")) }
            : new JsonObject() { ["type"] = "undefined" };
        return new FakeResponse(new JsonObject() { ["type"] = "success", ["result"] = value, ["realm"] = "realm-1" });
    }

    private static JsonObject Text(string value) => new() { ["type"] = "string", ["value"] = value };

    private static JsonObject Element(string sharedId) => new() { ["type"] = "node", ["sharedId"] = sharedId, ["value"] = new JsonObject() { ["nodeType"] = 1, ["childNodeCount"] = 0 } };

    private static JsonObject SnapshotValue(string text)
    {
        static JsonArray Property(string name, JsonNode value) => [name, value];
        string[] refs = [.. RefPattern().Matches(text).Select(match => match.Groups[1].Value)];
        return new JsonObject()
        {
            ["type"] = "object",
            ["value"] = new JsonArray(
                Property("tree", Text("{\"role\":\"fragment\",\"name\":\"\",\"children\":[]}")),
                Property("text", Text(text)),
                Property("refs", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray([.. refs.Select(reference => Text(reference))]) }),
                Property("elements", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray([.. refs.Select(reference => Element($"node-{reference}"))]) }),
                Property("frames", new JsonObject() { ["type"] = "array", ["value"] = new JsonArray() })),
        };
    }

    private static JsonObject Readiness()
    {
        static JsonObject Number(double value) => new() { ["type"] = "number", ["value"] = value };
        JsonObject offset = new() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("x", Number(5)), new JsonArray("y", Number(5))) };
        return new JsonObject() { ["type"] = "object", ["value"] = new JsonArray(new JsonArray("status", Text("ready")), new JsonArray("interactionOffset", offset)) };
    }

    [GeneratedRegex(@"\[ref=([a-z0-9]+)\]")]
    private static partial Regex RefPattern();
}
