// <copyright file="ToolAnswer.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.TestUtilities;

/// <summary>
/// The text of an MCP tool's result.
/// </summary>
/// <param name="IsError">Whether the result is an error.</param>
/// <param name="Lines">The lines of the result's text content.</param>
public sealed record ToolAnswer(bool IsError, IReadOnlyList<string> Lines);
