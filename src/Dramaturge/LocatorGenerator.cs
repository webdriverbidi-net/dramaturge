// <copyright file="LocatorGenerator.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.Text.Json;
using WebDriverBiDi.BrowsingContext;
using WebDriverBiDi.Script;

/// <summary>
/// Chooses the locator a recording writes for an element, from the facts the page gave about it and its nameable
/// ancestors: the first candidate that Dramaturge's own resolution finds the element alone with, in the order test
/// ID, role and name, label, placeholder, alt text, title, text, and role; then one of those within an ancestor
/// found alone by its test ID, role and name, or ID; then the first that finds the element among others, with its
/// index. When none finds the element, its CSS path, if that does, or else its first candidate. In a page behind
/// another, whose accessibility tree is not searched, its first role candidate is taken unconfirmed once one is reached.
/// </summary>
internal static class LocatorGenerator
{
    private const int MaxTextLength = 80;

    // Stands for the frame's variable inside a locator's C#, as in a filter's own locator; no literal can contain it.
    private const string FrameVariable = "\u001Fframe\u001F";

    private static readonly HashSet<string> UnnamedRoles = ["generic", "none", "presentation"];

    private static readonly AttributeQueryMethod[] AttributeQueryMethods = [new("placeholder", "GetByPlaceholder"), new("alt", "GetByAltText"), new("title", "GetByTitle")];

    /// <summary>
    /// Writes a generated locator's C# for the variable of the frame it starts from.
    /// </summary>
    /// <param name="code">The locator's C#, as <see cref="GeneratedLocator.Code"/> gives it.</param>
    /// <param name="frameVariable">The frame's variable, such as <c>page</c>.</param>
    /// <returns>The C#, the variable included, such as <c>page.GetByRole("button", "Save")</c>.</returns>
    public static string Write(string code, string frameVariable) => $"{frameVariable}.{code.Replace(FrameVariable, frameVariable)}";

    /// <summary>
    /// Chooses a locator for an element.
    /// </summary>
    /// <param name="frame">The frame the element is in.</param>
    /// <param name="target">The element's facts.</param>
    /// <param name="ancestors">The facts of the element's nameable ancestors, nearest first.</param>
    /// <param name="elements">The element, then its ancestors, as the page sent them.</param>
    /// <param name="budget">The time resolving the candidates may take.</param>
    /// <returns>The locator, and its C# after the frame's variable, such as <c>GetByRole("button", "Save")</c>.</returns>
    public static async Task<GeneratedLocator> GenerateAsync(Frame frame, JsonElement target, IReadOnlyList<JsonElement> ancestors, IReadOnlyList<NodeRemoteValue> elements, TimeBudget budget)
    {
        string? targetId = elements[0].SharedId;
        List<Candidate> candidates = [.. TargetCandidates(target)];
        Candidate? containing = null;
        int index = 0;
        foreach (Candidate candidate in candidates)
        {
            ElementLocator locator = candidate.OnFrame(frame);
            if (candidate.SearchesAccessibilityTree && frame.Page.Browser.Group.IsBehindAnother(frame.Page))
            {
                Candidate first = candidates.First(earlier => earlier.SearchesAccessibilityTree);
                return new GeneratedLocator(first.Code, first.OnFrame(frame)) { MatchedText = first.MatchedText };
            }

            List<string?> matches = await ResolveAsync(locator, budget).ConfigureAwait(false);
            if (matches.Count == 1 && matches[0] == targetId)
            {
                return new GeneratedLocator(candidate.Code, locator) { MatchedText = candidate.MatchedText };
            }

            if (containing is null && matches.Contains(targetId))
            {
                containing = candidate;
                index = matches.IndexOf(targetId);
            }
        }

        // No candidate finds the element when the lookups see another document, as after a click that navigated, or
        // when the browser names it otherwise. Its CSS path still finds it in the second case; in the first, its first
        // candidate is the likeliest. Scoping or an index only narrows matches that include it.
        if (containing is null)
        {
            Candidate path = Css(target.GetProperty("cssPath").GetString()!);
            List<string?> found = candidates.Count == 0 ? [] : await ResolveAsync(path.OnFrame(frame), budget).ConfigureAwait(false);
            Candidate chosen = candidates.Count == 0 || (found.Count == 1 && found[0] == targetId) ? path : candidates[0];
            return new GeneratedLocator(chosen.Code, chosen.OnFrame(frame));
        }

        for (int i = 0; i < ancestors.Count && i + 1 < elements.Count; i++)
        {
            if (await FindAloneAsync(frame, AncestorCandidates(ancestors[i]), elements[i + 1].SharedId, budget).ConfigureAwait(false) is not (string scopeCode, ElementLocator scope))
            {
                continue;
            }

            foreach (Candidate candidate in candidates)
            {
                ElementLocator locator = candidate.Within!(scope);
                List<string?> matches = await ResolveAsync(locator, budget).ConfigureAwait(false);
                if (matches.Count == 1 && matches[0] == targetId)
                {
                    return new GeneratedLocator($"{scopeCode}.{candidate.Code}", locator) { MatchedText = candidate.MatchedText };
                }
            }
        }

        return new GeneratedLocator($"{containing.Code}.Nth({index})", containing.OnFrame(frame).Nth(index)) { MatchedText = containing.MatchedText };
    }

    private static IEnumerable<Candidate> TargetCandidates(JsonElement facts)
    {
        if (Text(facts, "testId") is string testId)
        {
            yield return TestId(testId);
        }

        string? role = Text(facts, "role");
        bool namedRole = role is not null && !UnnamedRoles.Contains(role);
        string name = Text(facts, "name") ?? string.Empty;
        string shown = Text(facts, "text") ?? string.Empty;
        if (namedRole && name.Length > 0)
        {
            // A name that is the element's text is text the locator finds it by.
            yield return Role(role!, name) with { MatchedText = name == shown ? name : null };
        }

        foreach (string label in facts.GetProperty("labels").EnumerateArray().Select(label => label.GetString()!).Where(label => label.Length > 0))
        {
            yield return Partial("GetByLabel", label, (frame, text) => frame.GetByLabel(text), (scope, text) => scope.GetByLabel(text));
            yield return Exact("GetByLabel", label, (frame, text) => frame.GetByLabel(text, true), (scope, text) => scope.GetByLabel(text, true));
        }

        foreach ((string property, string method) in AttributeQueryMethods)
        {
            if (Text(facts, property) is string value)
            {
                yield return Partial(method, value, (frame, text) => AttributeQuery(frame, property, text, false), (scope, text) => AttributeQuery(scope, property, text, false));
                yield return Exact(method, value, (frame, text) => AttributeQuery(frame, property, text, true), (scope, text) => AttributeQuery(scope, property, text, true));
            }
        }

        if (shown.Length > 0)
        {
            string shortened = Shorten(shown);
            yield return Partial("GetByText", shortened, (frame, text) => frame.GetByText(text), (scope, text) => scope.GetByText(text)) with { MatchedText = shortened };
            if (shortened == shown)
            {
                yield return Exact("GetByText", shown, (frame, text) => frame.GetByText(text, true), (scope, text) => scope.GetByText(text, true)) with { MatchedText = shown };
            }
        }

        if (namedRole)
        {
            yield return Role(role!, null);
        }
    }

    private static IEnumerable<Candidate> AncestorCandidates(JsonElement facts)
    {
        if (Text(facts, "testId") is string testId)
        {
            yield return TestId(testId);
        }

        if (Text(facts, "role") is string role && !UnnamedRoles.Contains(role))
        {
            if (Text(facts, "name") is string name)
            {
                yield return Role(role, name);
            }

            // An unnamed ancestor, such as a card or a list item, is told from others like it by its heading.
            if (Text(facts, "headingName") is string heading)
            {
                yield return new($"GetByRole({CodeWriter.Literal(role)}).Filter(has: {FrameVariable}.GetByRole(\"heading\", {CodeWriter.Literal(heading)}))", frame => frame.GetByRole(role).Filter(has: frame.GetByRole("heading", heading)), null);
            }
        }

        // A CSS path that starts at the element is its unique ID.
        string cssPath = facts.GetProperty("cssPath").GetString()!;
        if (cssPath.StartsWith("#", StringComparison.Ordinal) && !cssPath.Contains(' '))
        {
            yield return Css(cssPath);
        }
    }

    private static async Task<GeneratedLocator?> FindAloneAsync(Frame frame, IEnumerable<Candidate> candidates, string? elementId, TimeBudget budget)
    {
        foreach (Candidate candidate in candidates)
        {
            ElementLocator locator = candidate.OnFrame(frame);
            List<string?> matches = await ResolveAsync(locator, budget).ConfigureAwait(false);
            if (matches.Count == 1 && matches[0] == elementId)
            {
                return new GeneratedLocator(candidate.Code, locator);
            }
        }

        return null;
    }

    // The shared IDs of the elements a locator finds now, or none when it cannot be resolved, as for a text locator
    // in a browser without one.
    private static async Task<List<string?>> ResolveAsync(ElementLocator locator, TimeBudget budget)
    {
        try
        {
            return [.. (await locator.FindAllAsync(budget).ConfigureAwait(false)).Select(node => node.SharedId)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string? Text(JsonElement facts, string property)
    {
        return facts.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0 ? value.GetString() : null;
    }

    // Long text is cut at a word, which a partial match still finds.
    private static string Shorten(string text)
    {
        if (text.Length <= MaxTextLength)
        {
            return text;
        }

        int space = text.LastIndexOf(' ', MaxTextLength);
        return text.Substring(0, space > 0 ? space : MaxTextLength);
    }

    private static ElementLocator AttributeQuery(Frame frame, string property, string text, bool exact) => property switch
    {
        "placeholder" => frame.GetByPlaceholder(text, exact),
        "alt" => frame.GetByAltText(text, exact),
        _ => frame.GetByTitle(text, exact),
    };

    private static ElementLocator AttributeQuery(ElementLocator scope, string property, string text, bool exact) => property switch
    {
        "placeholder" => scope.GetByPlaceholder(text, exact),
        "alt" => scope.GetByAltText(text, exact),
        _ => scope.GetByTitle(text, exact),
    };

    private static Candidate TestId(string testId) => new($"GetByTestId({CodeWriter.Literal(testId)})", frame => frame.GetByTestId(testId), scope => scope.GetByTestId(testId));

    private static Candidate Role(string role, string? name) => name is null
        ? new($"GetByRole({CodeWriter.Literal(role)})", frame => frame.GetByRole(role), scope => scope.GetByRole(role)) { SearchesAccessibilityTree = true }
        : new($"GetByRole({CodeWriter.Literal(role)}, {CodeWriter.Literal(name)})", frame => frame.GetByRole(role, name), scope => scope.GetByRole(role, name)) { SearchesAccessibilityTree = true };

    private static Candidate Css(string selector) => new($"Locate(new CssLocator({CodeWriter.Literal(selector)}))", frame => frame.Locate(new CssLocator(selector)), null);

    private static Candidate Partial(string method, string text, Func<Frame, string, ElementLocator> onFrame, Func<ElementLocator, string, ElementLocator> within) => new($"{method}({CodeWriter.Literal(text)})", frame => onFrame(frame, text), scope => within(scope, text));

    private static Candidate Exact(string method, string text, Func<Frame, string, ElementLocator> onFrame, Func<ElementLocator, string, ElementLocator> within) => new($"{method}({CodeWriter.Literal(text)}, exact: true)", frame => onFrame(frame, text), scope => within(scope, text));

    // A way to find the element: its C#, its locator in a frame or, for the element's own candidates, within another
    // locator, the element's text it finds the element by, if any, and whether it searches the accessibility tree.
    private sealed record Candidate(string Code, Func<Frame, ElementLocator> OnFrame, Func<ElementLocator, ElementLocator>? Within)
    {
        public string? MatchedText { get; init; }

        public bool SearchesAccessibilityTree { get; init; }
    }

    private sealed record AttributeQueryMethod(string Attribute, string Method);
}
