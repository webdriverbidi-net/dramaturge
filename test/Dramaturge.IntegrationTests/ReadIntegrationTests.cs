// <copyright file="ReadIntegrationTests.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

using System.IO.Compression;
using System.Text;
using Dramaturge.Browsers;
using Dramaturge.TestUtilities;
using WebDriverBiDi.BrowsingContext;
using WebDriverBiDi.Script;

public class ReadIntegrationTests
{
    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task TextHtmlValuesAndAttributesAreRead(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await OpenAsync(group, server);
        CancellationToken token = TestContext.Current.CancellationToken;

        Assert.Equal("Visible hidden text", await page.Locate(new CssLocator("#text")).TextContentAsync(cancellationToken: token));
        Assert.Equal("Visible text", await page.Locate(new CssLocator("#text")).InnerTextAsync(cancellationToken: token));
        Assert.Equal("<b>bold</b> text", await page.Locate(new CssLocator("#html")).InnerHtmlAsync(cancellationToken: token));
        Assert.Equal("Ada", await page.Locate(new CssLocator("#name")).InputValueAsync(cancellationToken: token));
        Assert.Equal("Some notes", await page.Locate(new CssLocator("#notes")).InputValueAsync(cancellationToken: token));
        Assert.Equal("green", await page.Locate(new CssLocator("#color")).InputValueAsync(cancellationToken: token));
        Assert.Equal("person", await page.Locate(new CssLocator("#name")).GetAttributeAsync("data-role", cancellationToken: token));
        Assert.Equal("person", await page.Locate(new CssLocator("#name")).GetAttributeAsync("DATA-ROLE", cancellationToken: token));
        Assert.Null(await page.Locate(new CssLocator("#name")).GetAttributeAsync("title", cancellationToken: token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.Locate(new CssLocator("#plain")).InputValueAsync(cancellationToken: token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.Locate(new CssLocator("#svg-text")).InnerTextAsync(cancellationToken: token));
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task StatesAreRead(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await OpenAsync(group, server);
        CancellationToken token = TestContext.Current.CancellationToken;

        Assert.True(await page.Locate(new CssLocator("#invisible")).IsHiddenAsync(token));
        Assert.True(await page.Locate(new CssLocator("#missing")).IsHiddenAsync(token));
        Assert.False(await page.Locate(new CssLocator("#enabled")).IsHiddenAsync(token));
        Assert.True(await page.Locate(new CssLocator("#enabled")).IsEnabledAsync(cancellationToken: token));
        Assert.True(await page.Locate(new CssLocator("#disabled")).IsDisabledAsync(cancellationToken: token));
        Assert.True(await page.Locate(new CssLocator("#name")).IsEditableAsync(cancellationToken: token));
        Assert.False(await page.Locate(new CssLocator("#readonly")).IsEditableAsync(cancellationToken: token));
        Assert.True(await page.Locate(new CssLocator("#checked")).IsCheckedAsync(cancellationToken: token));
        Assert.False(await page.Locate(new CssLocator("#mixed")).IsCheckedAsync(cancellationToken: token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.Locate(new CssLocator("#plain")).IsEditableAsync(cancellationToken: token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.Locate(new CssLocator("#plain")).IsCheckedAsync(cancellationToken: token));
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task BoundingBoxIsInTheViewportAndNullWhenNotVisible(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await OpenAsync(group, server);

        Assert.Equal(new BoundingBox(page.MainFrame, 20, 30, 100, 50), await page.Locate(new CssLocator("#box")).BoundingBoxAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(await page.Locate(new CssLocator("#invisible")).BoundingBoxAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task ScreenshotCapturesTheElementInOrOutOfView(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await OpenAsync(group, server);

        ImageSize box = PngSize(await page.Locate(new CssLocator("#box")).ScreenshotAsync(cancellationToken: TestContext.Current.CancellationToken));
        ImageSize far = PngSize(await page.Locate(new CssLocator("#far")).ScreenshotAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(box.Width, box.Height * 2);
        Assert.Equal(far.Width * 2, far.Height * 3);
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task ScreenshotCapturesAnElementOutOfViewInAFrameBelowTheFold(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await OpenAsync(group, server, "frame-screenshot.html");
        Frame frame = await page.Locate(new CssLocator("#frame")).ContentFrameAsync(cancellationToken: TestContext.Current.CancellationToken);

        DecodedImage image = DecodePng(await frame.Locate(new CssLocator("#target")).ScreenshotAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(await ScaledSizeAsync(page, 120, 60), image.Size);
        Assert.Equal(0, image.CountUnlike(new Rgb(255, 0, 0)));
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task ScreenshotCapturesAnElementOutOfViewInANestedCrossOriginFrame(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await OpenAsync(group, server, "frame-screenshot-nested.html");
        Frame outer = await page.Locate(new CssLocator("#outer")).ContentFrameAsync(cancellationToken: TestContext.Current.CancellationToken);
        Frame inner = await outer.Locate(new CssLocator("#inner")).ContentFrameAsync(cancellationToken: TestContext.Current.CancellationToken);

        DecodedImage image = DecodePng(await inner.Locate(new CssLocator("#target")).ScreenshotAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(await ScaledSizeAsync(page, 120, 60), image.Size);
        Assert.Equal(0, image.CountUnlike(new Rgb(255, 0, 0)));
    }

    [Theory]
    [MemberData(nameof(TestBrowsers.All), MemberType = typeof(TestBrowsers))]
    public async Task ScreenshotOfAnElementLargerThanItsFrameHasNothingFromAroundTheFrame(BrowserKind browserKind)
    {
        await using TestPageServer server = await TestPageServer.StartAsync();
        await using BrowserGroup group = await TestBrowsers.LaunchAsync(browserKind);
        Page page = await OpenAsync(group, server, "frame-screenshot.html");
        Frame frame = await page.Locate(new CssLocator("#frame")).ContentFrameAsync(cancellationToken: TestContext.Current.CancellationToken);

        DecodedImage image = DecodePng(await frame.Locate(new CssLocator("#big")).ScreenshotAsync(cancellationToken: TestContext.Current.CancellationToken));
        RemoteValueList shown = (await frame.EvaluateAsync("() => [document.documentElement.clientWidth, document.documentElement.clientHeight]", cancellationToken: TestContext.Current.CancellationToken)).As<CollectionRemoteValue>().Value!;

        // Chrome captures frames from the page, so only the part of the element its 300 by 200 frame shows: within
        // the frame's scroll bars where they take up room, as on Linux and Windows, and under them where they are
        // drawn over content, as on macOS.
        Assert.Equal(browserKind == BrowserKind.Chrome ? await ScaledSizeAsync(page, (int)shown[0].As<NumberRemoteValue>().Value, (int)shown[1].As<NumberRemoteValue>().Value) : await ScaledSizeAsync(page, 500, 400), image.Size);
        Assert.Equal(0, image.CountLike(new Rgb(255, 255, 0)) + image.CountLike(new Rgb(0, 0, 0)) + image.CountLike(new Rgb(0, 255, 0)));
    }

    private static async Task<Page> OpenAsync(BrowserGroup group, TestPageServer server, string pageName)
    {
        Page page = await group.DefaultBrowser.NewPageAsync(cancellationToken: TestContext.Current.CancellationToken);
        await page.NavigateAsync(server.UrlFor(pageName), cancellationToken: TestContext.Current.CancellationToken);
        return page;
    }

    private static async Task<ImageSize> ScaledSizeAsync(Page page, int width, int height)
    {
        double ratio = (await page.MainFrame.EvaluateAsync("() => window.devicePixelRatio", cancellationToken: TestContext.Current.CancellationToken)).As<NumberRemoteValue>().Value;
        return new ImageSize((int)Math.Round(width * ratio), (int)Math.Round(height * ratio));
    }

    private static Task<Page> OpenAsync(BrowserGroup group, TestPageServer server)
    {
        return OpenAsync(group, server, "reads.html");
    }

    // A PNG's width and height are big-endian integers at bytes 16 and 20 of its header.
    private static ImageSize PngSize(byte[] image)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], image.Take(4));
        return new ImageSize((image[16] << 24) | (image[17] << 16) | (image[18] << 8) | image[19], (image[20] << 24) | (image[21] << 16) | (image[22] << 8) | image[23]);
    }

    // Decodes an 8-bit, non-interlaced RGB or RGBA PNG, as browsers capture.
    private static DecodedImage DecodePng(byte[] image)
    {
        ImageSize size = PngSize(image);
        int bytesPerPixel = image[25] == 6 ? 4 : 3;
        using MemoryStream compressed = new();
        for (int offset = 8; offset < image.Length;)
        {
            int length = (image[offset] << 24) | (image[offset + 1] << 16) | (image[offset + 2] << 8) | image[offset + 3];
            if (Encoding.ASCII.GetString(image, offset + 4, 4) == "IDAT")
            {
                compressed.Write(image, offset + 8, length);
            }

            offset += length + 12;
        }

        compressed.Position = 0;
        using ZLibStream inflater = new(compressed, CompressionMode.Decompress);
        using MemoryStream filtered = new();
        inflater.CopyTo(filtered);
        byte[] data = filtered.ToArray();
        int stride = size.Width * bytesPerPixel;
        byte[] previous = new byte[stride];
        List<Rgb> pixels = new(size.Width * size.Height);
        for (int y = 0; y < size.Height; y++)
        {
            int filter = data[y * (stride + 1)];
            byte[] row = new byte[stride];
            for (int x = 0; x < stride; x++)
            {
                int left = x >= bytesPerPixel ? row[x - bytesPerPixel] : 0;
                int up = previous[x];
                int upLeft = x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;
                int estimate = left + up - upLeft;
                int nearest = Math.Abs(estimate - left) <= Math.Abs(estimate - up) && Math.Abs(estimate - left) <= Math.Abs(estimate - upLeft) ? left : Math.Abs(estimate - up) <= Math.Abs(estimate - upLeft) ? up : upLeft;
                int predictor = filter switch { 1 => left, 2 => up, 3 => (left + up) / 2, 4 => nearest, _ => 0 };
                row[x] = (byte)(data[(y * (stride + 1)) + 1 + x] + predictor);
            }

            for (int x = 0; x < stride; x += bytesPerPixel)
            {
                pixels.Add(new Rgb(row[x], row[x + 1], row[x + 2]));
            }

            previous = row;
        }

        return new DecodedImage(size, pixels);
    }

    private readonly record struct ImageSize(int Width, int Height);

    private readonly record struct Rgb(int Red, int Green, int Blue);

    private sealed record DecodedImage(ImageSize Size, IReadOnlyList<Rgb> Pixels)
    {
        public int CountLike(Rgb color) => this.Pixels.Count(pixel => IsLike(pixel, color));

        public int CountUnlike(Rgb color) => this.Pixels.Count(pixel => !IsLike(pixel, color));

        private static bool IsLike(Rgb pixel, Rgb color) => Math.Abs(pixel.Red - color.Red) <= 40 && Math.Abs(pixel.Green - color.Green) <= 40 && Math.Abs(pixel.Blue - color.Blue) <= 40;
    }
}
