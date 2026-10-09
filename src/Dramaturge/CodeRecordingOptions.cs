// <copyright file="CodeRecordingOptions.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

/// <summary>
/// Settings of a code recording.
/// </summary>
public sealed class CodeRecordingOptions
{
    /// <summary>
    /// Gets the kind of file the recording writes. The default is <see cref="CodeTarget.Program"/>.
    /// </summary>
    public CodeTarget Target { get; init; } = CodeTarget.Program;

    /// <summary>
    /// Gets a value indicating whether the recording shows its toolbar at the top of each page. Without it, the
    /// user's actions and Dramaturge's own calls are still recorded, but no locator can be picked and no assertion
    /// added from the page. The default is <see langword="true"/>.
    /// </summary>
    public bool ShowToolbar { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a value typed into a password field, or expected of one, is written as a read
    /// of the <c>DRAMATURGE_PASSWORD</c> environment variable instead of as typed. The default is
    /// <see langword="false"/>: the value is written as typed.
    /// </summary>
    public bool MaskPasswords { get; init; }
}
