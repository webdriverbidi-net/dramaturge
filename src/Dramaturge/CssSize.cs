// <copyright file="CssSize.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

/// <summary>
/// A size in CSS pixels.
/// </summary>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
internal readonly record struct CssSize(double Width, double Height);
