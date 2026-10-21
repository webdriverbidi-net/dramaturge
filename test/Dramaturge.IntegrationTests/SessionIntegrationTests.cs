// <copyright file="SessionIntegrationTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.Text.RegularExpressions;
using Dramaturge.Browsers;
using Dramaturge.TestUtilities;
using Dramaturge.Tool;

// The command-line tool's session, driven one command at a time as an agent drives it, with its daemon in this
// process.
public partial class SessionIntegrationTests
{
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task SessionIsDrivenAndExportedAsATest(BrowserKind browserKind)
    {
        TestBrowsers.Locate(BrowserLauncher.Configure(browserKind), browserKind);
        await using TestPageServer server = await TestPageServer.StartAsync();
        string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dis-{Guid.NewGuid():N}"[..12])).FullName;
        List<Task<int>> daemons = [];
        SessionHost host = null!;
        host = new() { Directory = directory, StartDaemon = (serveArguments, error, cancellationToken) => StartDaemonAsync(serveArguments, error, browserKind, host, daemons) };
        try
        {
            await RunAsync(host, "session", "start", "--browser", browserKind.ToString().ToLowerInvariant());
            string opened = await RunAsync(host, "open", server.UrlFor("recording.html"));
            await RunAsync(host, "fill", Ref(opened, "textbox \"Name\""), "Ada");
            string checkedBox = await RunAsync(host, "check", Ref(opened, "checkbox \"Agree\""));
            await RunAsync(host, "click", Ref(opened, "button \"Done\""), "--no-snapshot");
            await RunAsync(host, "expect", Ref(opened, "textbox \"Name\""), "value", "Ada");
            await RunAsync(host, "expect", "text", "Hello");
            string code = await RunAsync(host, "code", "--target", "xunit");

            Assert.Contains("+ - checkbox \"Agree\" [checked]", checkedBox);
            string[] statements = [.. code.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("await ", StringComparison.Ordinal))];
            Assert.Equal(
                [
                    $"await page.NavigateAsync(\"{server.UrlFor("recording.html")}\");",
                    "await page.GetByRole(\"textbox\", \"Name\").FillAsync(\"Ada\");",
                    "await page.GetByRole(\"checkbox\", \"Agree\").CheckAsync();",
                    "await page.GetByRole(\"button\", \"Done\").ClickAsync();",
                    "await Expect(page.GetByRole(\"textbox\", \"Name\")).ToHaveValueAsync(\"Ada\");",
                    "await Expect(page.Locate(new CssLocator(\"html > body\"))).ToContainTextAsync(\"Hello\");",
                ],
                statements);
            Assert.Contains("public class RecordedTests : PageTest", code);
        }
        finally
        {
            await DramaturgeTool.RunAsync(["session", "stop"], TextWriter.Null, TextWriter.Null, sessionHost: host, cancellationToken: CancellationToken.None);
            await Task.WhenAll(daemons);
            Directory.Delete(directory, true);
        }
    }

    // The ref of the snapshot's line that starts with a role and name.
    internal static string Ref(string snapshot, string roleAndName)
    {
        Match match = new Regex($"- {Regex.Escape(roleAndName)}.*\\[ref=([a-z0-9]+)\\]").Match(snapshot);
        Assert.True(match.Success, $"No {roleAndName} in:\n{snapshot}");
        return match.Groups[1].Value;
    }

    private static async Task<string> RunAsync(SessionHost host, params string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await DramaturgeTool.RunAsync(args, output, error, sessionHost: host, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(exitCode == 0, $"'{string.Join(" ", args)}' exited with {exitCode}: {error}");
        return output.ToString();
    }

    // Serves the session in this process, with the test's browser, answering once it is ready or has failed.
    internal static async Task<string?> StartDaemonAsync(IReadOnlyList<string> serveArguments, TextWriter error, BrowserKind browserKind, SessionHost host, List<Task<int>> daemons)
    {
        ReadyWriter ready = new();
        daemons.Add(DramaturgeTool.RunAsync([.. serveArguments], ready, error, configureLauncher: builder => TestBrowsers.Locate(builder, browserKind), sessionHost: host, cancellationToken: CancellationToken.None));
        return await ready.Said;
    }

    // Completes when the daemon says it is ready, with null, or that it failed, with why.
    private sealed class ReadyWriter : StringWriter
    {
        private readonly TaskCompletionSource<string?> said = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> Said => this.said.Task;

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value == "ready")
            {
                this.said.TrySetResult(null);
            }
            else if (value?.StartsWith("failed ", StringComparison.Ordinal) == true)
            {
                this.said.TrySetResult(value["failed ".Length..]);
            }
        }
    }
}
