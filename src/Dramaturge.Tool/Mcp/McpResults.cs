// <copyright file="McpResults.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Mcp;

using System.Text.Json;
using Dramaturge.Tool.Sessions;
using ModelContextProtocol.Protocol;

/// <summary>
/// A tool's result, written as the command line writes a command's: its text on success, or its error line and
/// details, marked as an error. A screenshot not saved to a file is the image itself.
/// </summary>
internal static class McpResults
{
    /// <summary>
    /// Gets the result of a command's outcome.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The result.</returns>
    public static CallToolResult Of(CommandOutcome outcome)
    {
        StringWriter output = new();
        StringWriter error = new();
        bool failed = CommandOutput.Write(outcome, false, output, error) != 0;
        return new()
        {
            Content = [new TextContentBlock() { Text = (failed ? error : output).ToString().TrimEnd('\r', '\n') }],
            IsError = failed,
        };
    }

    /// <summary>
    /// Gets the result of a session's answer to a command.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="reply">The answer.</param>
    /// <returns>The result.</returns>
    public static CallToolResult Of(string command, SessionReply reply)
    {
        if (reply.Error is SessionError failure)
        {
            return Of(CommandOutcome.Failure(failure));
        }

        JsonElement result = reply.Result!.Value;
        if (command == "screenshot" && result.TryGetProperty("image", out JsonElement image))
        {
            byte[] png = Convert.FromBase64String(image.GetString()!);
            return new() { Content = [new TextContentBlock() { Text = $"A screenshot of {png.Length} bytes, as PNG." }, ImageContentBlock.FromBytes(png, "image/png")] };
        }

        return Of(CommandOutcome.Success(result, SessionText.Render(command, result)));
    }
}
