// <copyright file="OpenedPage.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.TestUtilities;

using WebDriverBiDi;

/// <summary>
/// A page opened on a driver connected to a fake session.
/// </summary>
/// <param name="Driver">The started driver.</param>
/// <param name="Session">The fake session answering the driver.</param>
/// <param name="Page">The page.</param>
public sealed record OpenedPage(BiDiDriver Driver, FakeSession Session, Page Page);
