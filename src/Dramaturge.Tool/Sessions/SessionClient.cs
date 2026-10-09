// <copyright file="SessionClient.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Net.Sockets;
using System.Text.Json;

/// <summary>
/// Sends one command to a session's daemon and reads its answer.
/// </summary>
internal static class SessionClient
{
    /// <summary>
    /// The code of the error for a session that is not running.
    /// </summary>
    public const string NoSession = "no-session";

    /// <summary>
    /// Sends a command to a session.
    /// </summary>
    /// <param name="socketPath">The session's socket.</param>
    /// <param name="name">The session's name, for the error when it is not running.</param>
    /// <param name="command">The command.</param>
    /// <param name="arguments">The command's arguments.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    /// <returns>The daemon's answer; a session with no socket, or none that answers, is not running.</returns>
    public static async Task<SessionReply> SendAsync(string socketPath, string name, string command, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return new SessionReply(null, new SessionError(NoSession, $"No session named '{name}' is running.", "Start one with 'dramaturge session start'."));
        }

        NetworkStream stream = new(socket, ownsSocket: false);
        JsonElement? response;
        await using (stream.ConfigureAwait(false))
        {
            await SessionWire.WriteRequestAsync(stream, command, arguments, cancellationToken).ConfigureAwait(false);
            response = await SessionWire.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        if (response is not JsonElement message)
        {
            return new SessionReply(null, new SessionError("no-answer", $"Session '{name}' closed the connection without answering.", "It may have stopped; 'dramaturge session list' shows the sessions running."));
        }

        if (message.GetProperty("ok").GetBoolean())
        {
            return new SessionReply(message.GetProperty("result"), null);
        }

        JsonElement error = message.GetProperty("error");
        string? hint = error.GetProperty("hint").ValueKind == JsonValueKind.String ? error.GetProperty("hint").GetString() : null;
        return new SessionReply(null, new SessionError(error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!, hint));
    }
}
