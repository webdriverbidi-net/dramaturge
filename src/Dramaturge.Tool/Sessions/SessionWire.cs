// <copyright file="SessionWire.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>
/// The messages between a command and a session's daemon: one line of JSON each way on a connection. A request is
/// <c>{"command": …, "arguments": {…}}</c>; a response is <c>{"ok": true, "result": {…}}</c> or
/// <c>{"ok": false, "error": {"code": …, "message": …, "hint": …}}</c>.
/// </summary>
internal static class SessionWire
{
    // The messages are read by programs, never placed in HTML, so only what JSON requires is escaped.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Writes a request.
    /// </summary>
    /// <param name="stream">The connection.</param>
    /// <param name="command">The command.</param>
    /// <param name="arguments">The command's arguments.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the request is written.</returns>
    public static Task WriteRequestAsync(Stream stream, string command, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        return WriteLineAsync(stream, new Dictionary<string, object?>() { ["command"] = command, ["arguments"] = arguments }, cancellationToken);
    }

    /// <summary>
    /// Writes a response that succeeded.
    /// </summary>
    /// <param name="stream">The connection.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static Task WriteResultAsync(Stream stream, IReadOnlyDictionary<string, object?> result, CancellationToken cancellationToken)
    {
        return WriteLineAsync(stream, ResultMessage(result), cancellationToken);
    }

    /// <summary>
    /// Writes a response that failed.
    /// </summary>
    /// <param name="stream">The connection.</param>
    /// <param name="error">The error.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static Task WriteErrorAsync(Stream stream, SessionError error, CancellationToken cancellationToken)
    {
        return WriteLineAsync(stream, ErrorMessage(error), cancellationToken);
    }

    /// <summary>
    /// Gets a response that succeeded, as JSON.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The JSON.</returns>
    public static string Result(IReadOnlyDictionary<string, object?> result) => Encoding.UTF8.GetString(Serialize(ResultMessage(result)));

    /// <summary>
    /// Gets a response that failed, as JSON.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The JSON.</returns>
    public static string Error(SessionError error) => Encoding.UTF8.GetString(Serialize(ErrorMessage(error)));

    /// <summary>
    /// Reads one line of JSON.
    /// </summary>
    /// <param name="stream">The connection.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The message, or <see langword="null"/> when the connection closed before a whole line came, or the line is not a JSON object.</returns>
    public static async Task<JsonElement?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?> ResultMessage(IReadOnlyDictionary<string, object?> result) => new() { ["ok"] = true, ["result"] = result };

    private static Dictionary<string, object?> ErrorMessage(SessionError error)
    {
        Dictionary<string, object?> details = new() { ["code"] = error.Code, ["message"] = error.Message, ["hint"] = error.Hint };
        return new Dictionary<string, object?>() { ["ok"] = false, ["error"] = details };
    }

    private static async Task WriteLineAsync(Stream stream, IReadOnlyDictionary<string, object?> message, CancellationToken cancellationToken)
    {
        byte[] line = [.. Serialize(message), (byte)'\n'];
        await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte[] Serialize(IReadOnlyDictionary<string, object?> message)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, WriterOptions))
        {
            WriteValue(writer, message);
        }

        return buffer.ToArray();
    }

    // The values a message holds: strings, booleans, times, nulls, objects, and lists.
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case DateTimeOffset time:
                writer.WriteStringValue(time.ToString("O", CultureInfo.InvariantCulture));
                break;
            case IReadOnlyDictionary<string, object?> properties:
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    writer.WritePropertyName(property.Key);
                    WriteValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                writer.WriteStartArray();
                foreach (object? item in (IEnumerable)value)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
        }
    }
}
