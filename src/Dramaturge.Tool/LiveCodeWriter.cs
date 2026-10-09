// <copyright file="LiveCodeWriter.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool;

/// <summary>
/// Writes recorded statements as they are recorded. On a terminal, the statement still open is shown as well, and
/// rewritten in place as it changes; otherwise only settled statements are written, since written lines cannot be
/// taken back.
/// </summary>
internal sealed class LiveCodeWriter
{
    // Moves the cursor to the start of the line above and clears that line.
    private const string EraseLineAbove = "\u001b[1A\u001b[2K\r";

    private readonly TextWriter output;
    private readonly bool isTerminal;
    private readonly object lockObject = new();
    private string? open;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveCodeWriter"/> class.
    /// </summary>
    /// <param name="output">The writer for statements.</param>
    /// <param name="isTerminal">A value indicating whether the output is a terminal.</param>
    public LiveCodeWriter(TextWriter output, bool isTerminal)
    {
        this.output = output;
        this.isTerminal = isTerminal;
    }

    /// <summary>
    /// Writes code with the console's line endings, not the "\n" it is generated with.
    /// </summary>
    /// <param name="output">The writer.</param>
    /// <param name="code">The code.</param>
    public static void WriteCode(TextWriter output, string code)
    {
        foreach (string line in code.TrimEnd('\n').Split('\n'))
        {
            output.WriteLine(line);
        }
    }

    /// <summary>
    /// Shows the statement still open, in place of the one shown before, on a terminal.
    /// </summary>
    /// <param name="statement">The statement.</param>
    public void WriteOpen(string statement)
    {
        if (this.isTerminal)
        {
            lock (this.lockObject)
            {
                this.EraseOpen();
                this.open = statement;
                WriteCode(this.output, statement);
            }
        }
    }

    /// <summary>
    /// Writes a settled statement, in place of the open one shown before it.
    /// </summary>
    /// <param name="statement">The statement.</param>
    public void WriteSettled(string statement)
    {
        lock (this.lockObject)
        {
            this.EraseOpen();
            this.open = null;
            WriteCode(this.output, statement);
        }
    }

    private void EraseOpen()
    {
        if (this.open is not null)
        {
            this.output.Write(string.Concat(Enumerable.Repeat(EraseLineAbove, this.open.TrimEnd('\n').Split('\n').Length)));
            this.output.Flush();
        }
    }
}
