// <copyright file="GeneratedLocator.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

/// <summary>
/// A locator chosen for a recorded element, with its C#.
/// </summary>
/// <param name="Code">The locator's C# after its frame's variable, such as <c>GetByRole("button", "Save")</c>.</param>
/// <param name="Locator">The locator.</param>
internal sealed record GeneratedLocator(string Code, ElementLocator Locator);
