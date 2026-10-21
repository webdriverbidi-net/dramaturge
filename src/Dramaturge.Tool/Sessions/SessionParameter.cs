// <copyright file="SessionParameter.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

/// <summary>
/// A parameter of a session command: the name it is sent to the daemon under, and how a front end presents it.
/// </summary>
/// <param name="Name">The name the value is sent under.</param>
/// <param name="Kind">The kind of value.</param>
/// <param name="Description">What the value is, for help.</param>
/// <param name="Form">How the command line gives it.</param>
/// <param name="CliName">The option's name on the command line, when it is not --<see cref="Name"/>.</param>
/// <param name="Alias">A short alias of the option, such as -o.</param>
/// <param name="Choices">The values a text parameter accepts, when it accepts only those.</param>
/// <param name="Default">The value when the option is not given, as text; a parameter with <see cref="Choices"/> has one.</param>
/// <param name="Required">Whether an option must be given.</param>
internal sealed record SessionParameter(string Name, ParameterKind Kind, string Description, ParameterForm Form = ParameterForm.Option, string? CliName = null, string? Alias = null, IReadOnlyList<string>? Choices = null, string? Default = null, bool Required = false);
