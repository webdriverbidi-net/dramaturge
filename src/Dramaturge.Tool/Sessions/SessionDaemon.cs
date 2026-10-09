// <copyright file="SessionDaemon.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge.Tool.Sessions;

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dramaturge.Browsers;
using WebDriverBiDi;
using WebDriverBiDi.Session;

/// <summary>
/// A session's daemon: it owns the browser and its recording, and answers one command at a time on the session's
/// socket until it is stopped, its browser goes away, it is idle for too long, or, detached, it is signalled.
/// </summary>
internal sealed class SessionDaemon
{
    private static readonly Dictionary<string, object?> NoResult = [];

    private readonly SessionSettings settings;
    private readonly SessionBrowser browser;
    private readonly string socketPath;
    private readonly Socket listener;
    private readonly DateTimeOffset started;

    private SessionDaemon(SessionSettings settings, SessionBrowser browser, string socketPath, Socket listener, DateTimeOffset started)
    {
        this.settings = settings;
        this.browser = browser;
        this.socketPath = socketPath;
        this.listener = listener;
        this.started = started;
    }

    /// <summary>
    /// Starts the session's browser and serves the session until it ends.
    /// </summary>
    /// <param name="settings">The session's settings.</param>
    /// <param name="downloadOptions">The download options of the browser.</param>
    /// <param name="configureLauncher">Gives the launcher to use from the one the settings configure.</param>
    /// <param name="directory">The sessions' directory.</param>
    /// <param name="time">The clock of the idle timeout.</param>
    /// <param name="detached">A value indicating whether the daemon runs as a process of its own, which detaches from the one that started it and stops when signalled.</param>
    /// <param name="output">The writer the daemon says on, in one line, that it is <c>ready</c>, or that it <c>failed</c>, and why.</param>
    /// <param name="cancellationToken">A token that stops the daemon.</param>
    /// <returns>A task whose result is the exit code: 0 when the session ended, and 1 when it could not start.</returns>
    public static async Task<int> ServeAsync(SessionSettings settings, BrowserDownloadOptions downloadOptions, Func<BrowserLauncherBuilder, BrowserLauncherBuilder> configureLauncher, string directory, TimeProvider time, bool detached, TextWriter output, CancellationToken cancellationToken)
    {
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<IDisposable> signals = Detach(detached, stopping.Cancel);
        try
        {
            BrowserLauncherBuilder builder = BrowserLauncher.Configure(settings.Browser).WithHeadlessOption(!settings.Headed).WithDownloadOptions(downloadOptions);
            if (settings.Channel is not null)
            {
                builder = builder.WithReleaseChannel(Target.ParseChannel(settings.Browser, settings.Channel));
            }

            if (settings.UserDataDirectory is not null)
            {
                builder = builder.WithUserDataDirectory(settings.UserDataDirectory);
            }

            BrowserGroup group;
            try
            {
                BrowserLauncherBuilder launcher = configureLauncher(builder).WithSessionCapability("unhandledPromptBehavior", new UserPromptHandler() { Default = settings.Dialogs });
                group = await BrowserGroup.LaunchAsync(launcher, cancellationToken: stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebDriverBiDiException or OperationCanceledException)
            {
                output.WriteLine($"failed The browser could not be started: {ex.Message}");
                output.Flush();
                return 1;
            }

            await using (group.ConfigureAwait(false))
            {
                using IDisposable lost = group.Driver.OnConnectionLost.AddObserver(_ => stopping.Cancel());
                Browser browser = group.DefaultBrowser;
                if (browser.Pages.Count == 0)
                {
                    await browser.NewPageAsync(cancellationToken: stopping.Token).ConfigureAwait(false);
                }

                CodeRecording recording = await browser.RecordCodeAsync(new CodeRecordingOptions() { ShowToolbar = false, MaskPasswords = settings.MaskPasswords }, stopping.Token).ConfigureAwait(false);
                await using (recording.ConfigureAwait(false))
                {
                    using SessionBrowser session = await SessionBrowser.StartAsync(browser, settings, stopping.Token).ConfigureAwait(false);
                    string socketPath = SessionPaths.SocketPath(directory, settings.Name);
                    File.Delete(socketPath);
                    using Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    listener.Bind(new UnixDomainSocketEndPoint(socketPath));
                    SessionPaths.RestrictToUser(socketPath);
                    listener.Listen();
                    SessionDaemon daemon = new(settings, session, socketPath, listener, time.GetUtcNow());
                    using ITimer idle = time.CreateTimer(_ => stopping.Cancel(), null, settings.IdleTimeout, Timeout.InfiniteTimeSpan);
                    try
                    {
                        output.WriteLine("ready");
                        output.Flush();
                        await daemon.ServeAsync(idle, stopping).ConfigureAwait(false);
                    }
                    finally
                    {
                        File.Delete(socketPath);
                    }
                }
            }

            return 0;
        }
        finally
        {
            signals.ForEach(signal => signal.Dispose());
        }
    }

    // A daemon of its own leaves the process group of the command that started it, so that ending that command does
    // not end the session, and stops cleanly when signalled.
    [ExcludeFromCodeCoverage] // Only a daemon the tool started as a process detaches; the tests serve in their own process.
    private static List<IDisposable> Detach(bool detached, Action stop)
    {
        if (!detached)
        {
            return [];
        }

        if (!OperatingSystem.IsWindows())
        {
            _ = CreateSession();
        }

        void OnSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            stop();
        }

        return [PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal), PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal), PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSignal)];
    }

    [DllImport("libc", EntryPoint = "setsid")]
    private static extern int CreateSession();

    // The idle timeout pauses while a command is handled, and starts again before its answer is written, so that it
    // runs from the moment the command's client has its answer.
    private async Task ServeAsync(ITimer idle, CancellationTokenSource stopping)
    {
        void Resume() => idle.Change(this.settings.IdleTimeout, Timeout.InfiniteTimeSpan);
        try
        {
            while (true)
            {
                using Socket client = await this.listener.AcceptAsync(stopping.Token).ConfigureAwait(false);
                idle.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                if (await this.HandleAsync(client, Resume, stopping.Token).ConfigureAwait(false))
                {
                    return;
                }

                Resume();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Stopped by the browser going away, the idle timeout, or a signal.
        }
    }

    // A connection that fails is the client's loss alone. Returns whether the session is to stop.
    private async Task<bool> HandleAsync(Socket client, Action resume, CancellationToken cancellationToken)
    {
        NetworkStream stream = new(client, ownsSocket: false);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                JsonElement? request = await SessionWire.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                string? command = request is JsonElement message && message.TryGetProperty("command", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                if (command is "status" or null)
                {
                    resume();
                }

                switch (command)
                {
                    case "status":
                        await SessionWire.WriteResultAsync(stream, this.Status(), cancellationToken).ConfigureAwait(false);
                        return false;
                    case "stop":
                        // The socket goes first, so that a session started under the name at once does not find it.
                        this.listener.Dispose();
                        File.Delete(this.socketPath);
                        await SessionWire.WriteResultAsync(stream, NoResult, cancellationToken).ConfigureAwait(false);
                        return true;
                    case null:
                        await SessionWire.WriteErrorAsync(stream, new SessionError("bad-request", "The request was not a command."), cancellationToken).ConfigureAwait(false);
                        return false;
                    default:
                        DaemonAnswer answer = await this.browser.RunAsync(command, request!.Value.TryGetProperty("arguments", out JsonElement arguments) ? arguments : default, cancellationToken).ConfigureAwait(false)
                            ?? DaemonAnswer.Failed("unknown-command", $"The session does not know the command '{command}'.", "The tool and the session may be of different versions; stop the session and start it again.");
                        resume();
                        if (answer.Error is SessionError failure)
                        {
                            await SessionWire.WriteErrorAsync(stream, failure, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await SessionWire.WriteResultAsync(stream, answer.Result!, cancellationToken).ConfigureAwait(false);
                        }

                        return false;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    private Dictionary<string, object?> Status()
    {
        return new Dictionary<string, object?>()
        {
            ["name"] = this.settings.Name,
            ["browser"] = this.settings.Browser.ToString().ToLowerInvariant(),
            ["started"] = this.started,
        };
    }
}
