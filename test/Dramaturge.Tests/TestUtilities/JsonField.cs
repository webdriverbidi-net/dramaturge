// <copyright file="JsonField.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.TestUtilities;

using System.Text.Json.Nodes;

/// <summary>
/// A named JSON value, for building protocol messages in tests.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Value">The value.</param>
public sealed record JsonField(string Name, JsonNode Value);
