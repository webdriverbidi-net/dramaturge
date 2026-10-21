// <copyright file="ParameterKind.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// The kind of value a session command's parameter takes. A path is made absolute by the front end before it is
/// sent, since the daemon reads and writes files as named.
/// </summary>
internal enum ParameterKind
{
    /// <summary>
    /// A string.
    /// </summary>
    Text,

    /// <summary>
    /// A whole number.
    /// </summary>
    Number,

    /// <summary>
    /// A boolean, false unless given.
    /// </summary>
    Flag,

    /// <summary>
    /// One or more strings.
    /// </summary>
    Texts,

    /// <summary>
    /// A file path.
    /// </summary>
    Path,

    /// <summary>
    /// One or more file paths.
    /// </summary>
    Paths,
}
