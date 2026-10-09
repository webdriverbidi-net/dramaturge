// <copyright file="SessionError.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// Why a session command failed.
/// </summary>
/// <param name="Code">A stable code, such as <c>no-session</c>.</param>
/// <param name="Message">What went wrong.</param>
/// <param name="Hint">What to do about it, or <see langword="null"/>.</param>
internal sealed record SessionError(string Code, string Message, string? Hint = null);
