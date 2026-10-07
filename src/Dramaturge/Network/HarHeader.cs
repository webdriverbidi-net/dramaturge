// <copyright file="HarHeader.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Network;

/// <summary>
/// A header recorded in an HTTP Archive.
/// </summary>
/// <param name="Name">The header's name.</param>
/// <param name="Value">The header's value.</param>
internal sealed record HarHeader(string Name, string Value);
