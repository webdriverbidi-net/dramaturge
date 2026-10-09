// <copyright file="SessionReply.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Text.Json;

/// <summary>
/// A daemon's answer to a command: its result, or why it failed.
/// </summary>
/// <param name="Result">The result, or <see langword="null"/> when the command failed.</param>
/// <param name="Error">Why the command failed, or <see langword="null"/> when it succeeded.</param>
internal sealed record SessionReply(JsonElement? Result, SessionError? Error);
