// <copyright file="McpIntegrationTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.IO.Pipelines;
using Dramaturge.Browsers;
using Dramaturge.TestUtilities;
using Dramaturge.Tool;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// The command-line tool's session, driven through its MCP server one tool call at a time as a host drives it, with
// the server and the session's daemon in this process.
public class McpIntegrationTests
{
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task SessionIsDrivenThroughMcpAndExportedAsATest(BrowserKind browserKind)
    {
        TestBrowsers.Locate(BrowserLauncher.Configure(browserKind), browserKind);
        await using TestPageServer server = await TestPageServer.StartAsync();
        string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dim-{Guid.NewGuid():N}"[..12])).FullName;
        List<Task<int>> daemons = [];
        Pipe requests = new();
        Pipe answers = new();
        SessionHost host = null!;
        host = new()
        {
            Directory = directory,
            StartDaemon = (serveArguments, error, cancellationToken) => SessionIntegrationTests.StartDaemonAsync(serveArguments, error, browserKind, host, daemons),
            Streams = new StandardStreams(requests.Reader.AsStream(), answers.Writer.AsStream()),
        };
        StringWriter error = new();
        Task<int> serving = DramaturgeTool.RunAsync(["mcp"], TextWriter.Null, error, sessionHost: host, cancellationToken: CancellationToken.None);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(requests.Writer.AsStream(), answers.Reader.AsStream()), cancellationToken: TestContext.Current.CancellationToken);
            await CallAsync(client, "session_start", new Dictionary<string, object?>() { ["browser"] = browserKind.ToString().ToLowerInvariant() });
            string opened = await CallAsync(client, "open", new Dictionary<string, object?>() { ["url"] = server.UrlFor("recording.html") });
            string name = SessionIntegrationTests.Ref(opened, "textbox \"Name\"");
            await CallAsync(client, "fill", new Dictionary<string, object?>() { ["ref"] = name, ["text"] = "Ada" });
            string checkedBox = await CallAsync(client, "check", new Dictionary<string, object?>() { ["ref"] = SessionIntegrationTests.Ref(opened, "checkbox \"Agree\"") });
            await CallAsync(client, "click", new Dictionary<string, object?>() { ["ref"] = SessionIntegrationTests.Ref(opened, "button \"Done\""), ["snapshot"] = false });
            await CallAsync(client, "expect", new Dictionary<string, object?>() { ["condition"] = new[] { name, "value", "Ada" } });
            CallToolResult screenshot = await client.CallToolAsync("screenshot", cancellationToken: TestContext.Current.CancellationToken);
            string code = await CallAsync(client, "code", new Dictionary<string, object?>() { ["target"] = "xunit" });
            await CallAsync(client, "session_stop");

            Assert.Contains("+ - checkbox \"Agree\" [checked]", checkedBox);
            ImageContentBlock image = Assert.Single(screenshot.Content.OfType<ImageContentBlock>());
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], image.DecodedData.ToArray()[..4]);
            string[] statements = [.. code.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("await ", StringComparison.Ordinal))];
            Assert.Equal(
                [
                    $"await page.NavigateAsync(\"{server.UrlFor("recording.html")}\");",
                    "await page.GetByRole(\"textbox\", \"Name\").FillAsync(\"Ada\");",
                    "await page.GetByRole(\"checkbox\", \"Agree\").CheckAsync();",
                    "await page.GetByRole(\"button\", \"Done\").ClickAsync();",
                    "await Expect(page.GetByRole(\"textbox\", \"Name\")).ToHaveValueAsync(\"Ada\");",
                ],
                statements);
            Assert.Contains("public class RecordedTests : PageTest", code);
        }
        finally
        {
            await requests.Writer.CompleteAsync();
            int exitCode = await serving;
            await Task.WhenAll(daemons);
            Directory.Delete(directory, true);
            Assert.True(exitCode == 0, $"The server exited with {exitCode}: {error}");
        }
    }

    private static async Task<string> CallAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
        string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.True(result.IsError != true, $"'{tool}' failed: {text}");
        return text;
    }
}
