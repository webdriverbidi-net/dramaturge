// <copyright file="SnapshotDiff.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// How a snapshot changed from the one before it, as lines: each line removed, marked <c>- </c>, and each added,
/// marked <c>+ </c>, with the unchanged lines they sit within, marked with two spaces, so that each change can be
/// placed in the page.
/// </summary>
internal static class SnapshotDiff
{
    /// <summary>
    /// Describes the changes from one snapshot to another.
    /// </summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <param name="after">The later snapshot.</param>
    /// <returns>The lines, none if nothing changed.</returns>
    public static IReadOnlyList<string> Describe(string before, string after)
    {
        string[] old = before.Split('\n');
        string[] current = after.Split('\n');

        // Lines the two share at either end need no comparing.
        int prefix = 0;
        while (prefix < old.Length && prefix < current.Length && old[prefix] == current[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < old.Length - prefix && suffix < current.Length - prefix && old[old.Length - 1 - suffix] == current[current.Length - 1 - suffix])
        {
            suffix++;
        }

        int oldCount = old.Length - prefix - suffix;
        int currentCount = current.Length - prefix - suffix;

        // The length of the longest common subsequence of each pair of suffixes of the middles, from which the
        // changes are read.
        int[,] common = new int[oldCount + 1, currentCount + 1];
        for (int i = oldCount - 1; i >= 0; i--)
        {
            for (int j = currentCount - 1; j >= 0; j--)
            {
                common[i, j] = old[prefix + i] == current[prefix + j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        List<DiffLine> merged = [.. current.Take(prefix).Select(line => new DiffLine(' ', line))];
        int oldIndex = 0;
        int currentIndex = 0;
        while (oldIndex < oldCount || currentIndex < currentCount)
        {
            if (oldIndex < oldCount && currentIndex < currentCount && old[prefix + oldIndex] == current[prefix + currentIndex])
            {
                merged.Add(new DiffLine(' ', old[prefix + oldIndex++]));
                currentIndex++;
            }
            else if (currentIndex == currentCount || (oldIndex < oldCount && common[oldIndex + 1, currentIndex] >= common[oldIndex, currentIndex + 1]))
            {
                merged.Add(new DiffLine('-', old[prefix + oldIndex++]));
            }
            else
            {
                merged.Add(new DiffLine('+', current[prefix + currentIndex++]));
            }
        }

        return WithAncestors(merged);
    }

    // Each change, after those lines that enclose it, by indentation, and are not shown yet.
    private static List<string> WithAncestors(List<DiffLine> merged)
    {
        List<string> lines = [];
        List<int> enclosing = [];
        HashSet<int> shown = [];
        for (int i = 0; i < merged.Count; i++)
        {
            int indent = Indent(merged[i].Text);
            enclosing.RemoveAll(index => Indent(merged[index].Text) >= indent);
            if (merged[i].Marker != ' ')
            {
                foreach (int index in enclosing.Where(shown.Add))
                {
                    lines.Add($"  {merged[index].Text}");
                }

                shown.Add(i);
                lines.Add($"{merged[i].Marker} {merged[i].Text}");
            }

            enclosing.Add(i);
        }

        return lines;
    }

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private sealed record DiffLine(char Marker, string Text);
}
