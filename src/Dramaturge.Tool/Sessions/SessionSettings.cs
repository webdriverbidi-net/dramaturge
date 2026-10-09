// <copyright file="SessionSettings.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using Dramaturge.Browsers;

/// <summary>
/// The settings a session is started with, fixed for its life.
/// </summary>
/// <param name="Name">The session's name.</param>
/// <param name="Browser">The browser.</param>
/// <param name="Channel">The browser's release channel, as a target names it, or <see langword="null"/> for the default.</param>
/// <param name="Headed">A value indicating whether the browser has a window.</param>
/// <param name="UserDataDirectory">The browser's profile directory, or <see langword="null"/> for a temporary one.</param>
/// <param name="IdleTimeout">How long the daemon waits for a command before it stops.</param>
/// <param name="MaskPasswords">A value indicating whether the session's code masks passwords.</param>
internal sealed record SessionSettings(string Name, BrowserKind Browser, string? Channel, bool Headed, string? UserDataDirectory, TimeSpan IdleTimeout, bool MaskPasswords);
