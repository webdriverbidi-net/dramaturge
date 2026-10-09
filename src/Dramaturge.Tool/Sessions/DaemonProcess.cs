// <copyright file="DaemonProcess.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Starts a session's daemon as a process of its own: the tool again, serving the session detached.
/// </summary>
[ExcludeFromCodeCoverage] // Only a real run starts the tool again; the tests serve sessions in their own process.
internal static class DaemonProcess
{
    /// <summary>
    /// Starts the daemon and waits until it is ready, passing on its errors, such as download progress, meanwhile.
    /// Its standard streams are its own, not the command's, so that whatever ran the command does not wait for the
    /// daemon to close them.
    /// </summary>
    /// <param name="serveArguments">The arguments of <c>session serve</c>.</param>
    /// <param name="error">The writer for the daemon's errors.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task whose result is 0 once the daemon is ready, or another exit code once it has failed.</returns>
    public static async Task<int> StartAsync(IReadOnlyList<string> serveArguments, TextWriter error, CancellationToken cancellationToken)
    {
        string host = Environment.ProcessPath!;
        ProcessStartInfo start = new(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Run as "dotnet dramaturge.dll", the host needs the tool's assembly too.
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(DaemonProcess).Assembly.Location);
        }

        foreach (string argument in serveArguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add("--detached");
        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        _ = RelayAsync(process.StandardError, error);
        while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            if (line == "ready")
            {
                return 0;
            }
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 ? 1 : process.ExitCode;
    }

    private static async Task RelayAsync(StreamReader reader, TextWriter error)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            error.WriteLine(line);
        }
    }
}
