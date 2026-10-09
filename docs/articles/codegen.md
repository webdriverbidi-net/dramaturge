# Code Generation

Code generation writes C# while you use a browser: `dramaturge codegen` opens a browser, and each thing you do in it becomes a Dramaturge statement, with a locator chosen the way you would choose one by hand. A toolbar in the page picks locators and adds assertions. The result is a program or a test class to edit, rather than a finished test: name it, and replace typed values, such as passwords, with ones the test supplies.

## Recording with the Command-Line Tool

The [command-line tool](command-line-tool.md) records into the browser it starts, with a window:

```bash
dramaturge codegen https://example.com/login --target xunit -o LoginTests.cs
```

| Option | Meaning |
| --- | --- |
| *url* | The address to open first, recorded as `NavigateAsync` |
| `--target` | The kind of file: `program` (the default), `xunit`, `nunit`, `mstest`, or `tunit` |
| `--browser` | `chrome` (the default), `firefox`, or `edge` |
| `--channel` | The browser's release channel, named as `dramaturge install` names it: `stable` (the default), `beta`, `dev`, and `canary` for Chrome and Edge, or `nightly` and `esr` for Firefox |
| `--test-id-attribute` | The attribute test IDs are read from; the default is `data-testid` |
| `-o`, `--output` | A file kept up to date with the whole code as it is recorded |
| `--mask-passwords` | Writes a value typed into, or expected of, a password field as `Environment.GetEnvironmentVariable("DRAMATURGE_PASSWORD") ?? ""` instead of as typed |

A statement stays open while what you do next can still change it: typing more into the same field, the second click of a double click, or the navigation, popup, download, or dialog an action causes. In a terminal, the open statement is printed at once and rewritten in place as it changes; when standard output is redirected, each statement is printed once it is complete, since a printed line cannot be taken back. Each locator picked with the toolbar is printed to standard error. With `-o`, the file is rewritten whenever a statement starts, changes, or completes, so it always has the open statement too; without it, the whole file is printed when recording ends. Recording ends when you close the browser, or press Ctrl+C. The browser is downloaded, if it must be, as [Browser Setup](browser-setup.md) describes, and `--path` chooses the cache as for every command.

A statement is printed once it is complete, which is when you do the next thing: typing into a field is one `FillAsync` with everything you typed, and the clicks of a double click are one `DblClickAsync`.

## The Toolbar

The toolbar sits at the top of each page while it is recorded. Drag it by the grip at its left end to move it, and click the button at its right end to collapse it to a small **Recording** pill, which you can also drag; clicking the pill opens the toolbar again. Pages loaded later in the recording show it where you last left it, open or collapsed.

Its buttons:

| Button | Does |
| --- | --- |
| **Record** | Turns recording off and on |
| **Pick locator** | Highlights the element under the pointer; clicking it prints its locator without recording anything |
| **Assert visible** | Clicking an element adds `ToBeVisibleAsync` |
| **Assert text** | Clicking an element asks for the text to expect, starting from the element's; Enter adds `ToContainTextAsync`, and Escape cancels |
| **Assert value** | Clicking a field adds `ToHaveValueAsync`, or, for a checkbox or radio button, `ToBeCheckedAsync` or `Not.ToBeCheckedAsync` |
| **Assert snapshot** | Clicking an element adds `ToMatchAriaSnapshotAsync` with its [accessibility snapshot](accessibility-snapshots.md) |

A click made to pick or assert is not passed to the page, so a link is not followed and a checkbox is not toggled. Recording resumes after each pick or assertion. An element under the toolbar cannot be clicked there; move or collapse the toolbar first.

## What Is Recorded

| You | Statement |
| --- | --- |
| Click, with any mouse button and modifier keys | `ClickAsync`; a double click is `DblClickAsync`, and a triple click sets `ClickCount` |
| Check or uncheck a checkbox or radio button, with the mouse or the keyboard | `CheckAsync`, `UncheckAsync` |
| Type into a field or an editable element | `FillAsync` with the whole value |
| Press a key that does not type, such as Enter or Tab, or a key with Control, Alt, or Meta | `PressAsync`, with a member of `Keys` or the character |
| Choose options in a select | `SelectOptionAsync` with the options' values |
| Choose files for a file input | `SetInputFilesAsync` with the files' names, under a comment saying to replace each with its path, since the page knows only the names |
| Open an address yourself | `NavigateAsync` |
| Go back or forward with the browser | `GoBackAsync` or `GoForwardAsync`, where the page reports the step, which Chrome does not always do; otherwise `NavigateAsync` to the page arrived at |
| Reload with the browser | `NavigateAsync` to the same address |
| Close a page other than the first | `CloseAsync` |

What an action causes is recorded with it:

| The action | Becomes |
| --- | --- |
| Navigates its page | `await page.RunAndWaitForNavigationAsync(() => …);` |
| Opens a popup | `Page page1 = await page.RunAndWaitForPopupAsync(() => …);`, and the popup's actions use `page1` |
| Starts a download | `Download download = await page.RunAndWaitForDownloadAsync(() => …);` |
| Opens a dialog | A comment above it naming the dialog and its message; the code does not answer the dialog, so the browser handles it as it did while recording, by default dismissing it (see [Dialogs](pages-and-frames.md#dialogs)) |

An action in a frame first declares the frame from its element, once, as `Frame frame = await page.GetByTitle("Payment").ContentFrameAsync();`, and frames within it from that. Hovering, dragging, scrolling, and the mouse's back and forward buttons are not recorded.

<!-- inline-csharp: an example of generated code, shown as the tool writes it -->
```csharp
using Dramaturge;
using Dramaturge.Browsers;
using static Dramaturge.Assertions;

await using BrowserGroup group = await BrowserGroup.LaunchAsync(BrowserLauncher.Configure(BrowserKind.Firefox).WithHeadlessOption(false));
Page page = await group.DefaultBrowser.NewPageAsync();
await page.NavigateAsync("https://example.com/login");
await page.GetByLabel("User name").FillAsync("ada");
await page.RunAndWaitForNavigationAsync(() => page.GetByRole("button", "Sign in").ClickAsync());
await Expect(page.GetByRole("heading", "Welcome, Ada")).ToBeVisibleAsync();
```

## How Locators Are Chosen

Each locator is the first of these that finds the element and nothing else, as Dramaturge itself finds elements when the code runs:

1. `GetByTestId`
2. `GetByRole` with the accessible name
3. `GetByLabel`, `GetByPlaceholder`, `GetByAltText`, and `GetByTitle`, matching part of the text, and then the whole text with `exact: true`
4. `GetByText`, with text longer than 80 characters cut at a word
5. `GetByRole` alone

When none finds the element alone, a locator is looked for within the element's nearest ancestor that its test ID, its role and name, or its own ID finds alone, as in `page.GetByTestId("cart").GetByRole("button", "Remove")`; then the first one that finds the element among others is used with `Nth`; and last, the element's CSS path. An element that has already left its page when its locator is chosen, as a link does once its click navigates, gets the first locator of the list, unconfirmed.

`--test-id-attribute`, or `DramaturgeOptions.TestIdAttribute`, names the attribute `GetByTestId` reads, and the generated code sets the same attribute.

## Targets

`--target program` writes top-level statements that launch the browser the recording used, with a window, and open a page. The test targets write a class, `RecordedTests`, derived from the framework package's `PageTest`, with one test, `Recorded`, that uses `this.Page`; the browser it runs in is chosen as [Test Frameworks](test-frameworks.md) describes, and the project needs that article's one-time setup. A test ID attribute other than the default is set by overriding `GroupOptions`.

## Recording from Code

`RecordCodeAsync` records a browser's actions as code, as the tool does. With the toolbar, the browser should have a window, since the toolbar needs a user; `CodeRecordingOptions.ShowToolbar = false` records without it, as a program driving the browser through Dramaturge would, though then no locator can be picked and no assertion added from the page. `OnStatement` reports each statement as it is complete, `OnPendingStatement` the open statement each time it starts or changes, `OnLocatorPicked` each locator picked with the toolbar, and `Code` gives the whole file at any time, the open statement included as it is then. `StopAsync` completes the last statement, removes the toolbar, and returns the code.

[!code-csharp[Record Code](../code/CodegenSamples.cs#RecordCode)]

Dramaturge's own calls on a recording browser's pages are recorded too: `NavigateAsync`, `ReloadAsync`, `GoBackAsync`, and `GoForwardAsync` as themselves, never as the effect of an earlier action; `SelectOptionAsync`, whose events the page does not take for a user's; and `SetInputFilesAsync` with the paths it was given rather than the files' names. Its other actions are recorded from the events they cause, as a user's are. Expectations that pass are recorded as well, where the toolbar could add them: `ToBeVisibleAsync`, `ToContainTextAsync` and `ToHaveValueAsync` with a string, `ToBeCheckedAsync` and `Not.ToBeCheckedAsync`, and `ToMatchAriaSnapshotAsync`, each with a locator chosen for the element it checked, and a page's `ToHaveUrlAsync` and `ToHaveTitleAsync` with a string. Other expectations are not recorded, nor are ones that ignore case, read the rendered text, or are otherwise negated.

A browser records code once at a time; starting another recording while one is running throws `InvalidOperationException`. Recording starts in the pages already open before `RecordCodeAsync` returns, and in each page or frame loaded later as its document loads.

## Limitations

- **Navigations:** a navigation is recorded as the effect of the action whose statement is still open when it starts, unless that action has navigated already. An address you open right after an action that did not navigate is therefore recorded as that action's navigation; another action, or a toolbar button, completes the action first. On Chrome, which sometimes loses the page's report of a navigation it starts itself, such a navigation, as a redirect by script, can be recorded as `NavigateAsync` as well. A page that goes back or forward in its history by itself, with no action of yours before it, is recorded as if you had pressed the browser's button.
- **Chrome popups:** Chrome does not answer script commands in a page that a script opened ([issue 571161446](https://issues.chromium.org/issues/571161446)), so the popup itself is recorded but nothing done in it is, and stopping waits for the group's `NavigationTimeout` for the popup.
- **Chrome frames:** Chrome's role lookup does not find elements in a same-origin child frame ([issue 571486947](https://issues.chromium.org/issues/571486947)), so an element there is found by its text or attributes instead of its role.
- **Firefox text:** Firefox cannot look elements up by their rendered text, so `GetByText` is never chosen there.
- **Shadow roots:** an element in a shadow root gets a role or attribute locator where one finds it; its CSS path starts inside the shadow root, so it works only with `DramaturgeOptions.PierceShadowRoots`. An action in a frame whose element is in a shadow root is not recorded.
- **Typed values:** what you type is written into the code as it is, passwords included, unless passwords are masked (`--mask-passwords`, or `CodeRecordingOptions.MaskPasswords`). Masking covers `input type="password"` fields only, and every one reads the same `DRAMATURGE_PASSWORD` variable, so a form with two different passwords needs its code edited.
