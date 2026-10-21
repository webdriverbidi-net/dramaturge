// <copyright file="ParameterForm.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// How a session command's parameter is given on the command line.
/// </summary>
internal enum ParameterForm
{
    /// <summary>
    /// A positional argument, which must be given.
    /// </summary>
    Argument,

    /// <summary>
    /// A named option.
    /// </summary>
    Option,

    /// <summary>
    /// A named flag whose presence sets the parameter to false, such as --no-snapshot.
    /// </summary>
    InvertedOption,
}
