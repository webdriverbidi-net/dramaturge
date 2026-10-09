// <copyright file="Observation.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using WebDriverBiDi.Script;

/// <summary>
/// What one check of an expectation saw.
/// </summary>
/// <param name="Holds">A value indicating whether the expected condition held, before any negation.</param>
/// <param name="Actual">What was seen, such as <c>hidden</c>, or <see langword="null"/> when no element matched.</param>
/// <param name="Summary">What was seen, in words for a failure message, or <see langword="null"/> to quote <paramref name="Actual"/>.</param>
/// <param name="Element">The one element checked, or <see langword="null"/> when none was.</param>
internal sealed record Observation(bool Holds, string? Actual, string? Summary = null, NodeRemoteValue? Element = null);
