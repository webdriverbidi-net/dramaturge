// <copyright file="PageBehindException.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

/// <summary>
/// The exception thrown when a lookup that leaves pages where they are needs a page's accessibility tree while
/// another page is in front of it.
/// </summary>
internal sealed class PageBehindException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PageBehindException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public PageBehindException(string message)
        : base(message)
    {
    }
}
