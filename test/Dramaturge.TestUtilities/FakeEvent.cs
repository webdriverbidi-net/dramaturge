// <copyright file="FakeEvent.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.TestUtilities;

using System.Text.Json.Nodes;

/// <summary>
/// An event a fake remote end delivers.
/// </summary>
/// <param name="Method">The event's method, such as "browsingContext.load".</param>
/// <param name="Parameters">The event's parameters.</param>
public sealed record FakeEvent(string Method, JsonObject Parameters);
