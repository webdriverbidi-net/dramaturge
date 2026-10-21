// <copyright file="StandardStreams.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

/// <summary>
/// The streams the <c>mcp</c> command serves a host on, in place of the console's standard input and output.
/// </summary>
/// <param name="Input">The stream the host's requests are read from.</param>
/// <param name="Output">The stream the answers are written to.</param>
public sealed record StandardStreams(Stream Input, Stream Output);
