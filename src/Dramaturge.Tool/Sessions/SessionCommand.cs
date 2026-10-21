// <copyright file="SessionCommand.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// A command that drives a session's browser, as the daemon takes it, and as each front end offers it.
/// </summary>
/// <param name="Name">The command's name, which is also the request's.</param>
/// <param name="Description">What the command does, for help.</param>
/// <param name="Parameters">The parameters, in the order a front end presents them.</param>
/// <param name="Fixed">The arguments the command always sends, or <see langword="null"/> for none.</param>
internal sealed record SessionCommand(string Name, string Description, IReadOnlyList<SessionParameter> Parameters, IReadOnlyDictionary<string, object?>? Fixed = null);
