// <copyright file="TraceParameter.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

/// <summary>
/// A parameter of an action described for a trace.
/// </summary>
/// <param name="Name">The parameter's name.</param>
/// <param name="Value">The parameter's value: a string, or a list of strings.</param>
internal sealed record TraceParameter(string Name, object Value);
