// <copyright file="TimedPage.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.TestUtilities;

using Microsoft.Extensions.Time.Testing;
using WebDriverBiDi;

/// <summary>
/// A page opened on a driver connected to a fake session, with the fake clock its timeouts run on.
/// </summary>
/// <param name="Driver">The started driver.</param>
/// <param name="Session">The fake session answering the driver.</param>
/// <param name="Page">The page.</param>
/// <param name="Time">The fake clock.</param>
public sealed record TimedPage(BiDiDriver Driver, FakeSession Session, Page Page, FakeTimeProvider Time);
