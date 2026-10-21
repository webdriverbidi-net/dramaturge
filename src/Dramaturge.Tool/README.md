# Dramaturge.Tool

The `dramaturge` command-line tool, which installs, lists, and removes the browsers and drivers that [Dramaturge.Browsers](https://www.nuget.org/packages/Dramaturge.Browsers) keeps in its cache, records what you do in a browser as C#, keeps browser sessions open for later commands to drive, and serves those commands to MCP hosts as tools. Installing is most useful in continuous integration: install the browsers while building a machine image or before the tests run, so that no test waits for a download.

It uses the same cache, download sources, and verification as the library, because it is the library: each release of the tool bundles the Dramaturge.Browsers of the same version.

## Installation

```bash
dotnet tool install --global Dramaturge.Tool
```

Or, with .NET 10 or later, run it once without installing it:

```bash
dnx Dramaturge.Tool browsers install chrome
```

The tool runs on .NET 8 and later.

## Installing Browsers and Drivers

```bash
dramaturge browsers install chrome chromedriver firefox@beta geckodriver
```

Each target is a name, optionally followed by `@` and a channel, a milestone, or a version:

| Name | Channels | Also accepts |
| --- | --- | --- |
| `chrome`, `chrome-headless-shell` | `stable` (the default), `beta`, `dev`, `canary` | A milestone (`chrome@131`, the newest release of Chrome 131) or a version (`chrome@131.0.6778.204`) |
| `chromedriver` | `stable`, `beta`, `dev`, `canary` | A milestone or version, of the Chrome it drives |
| `firefox` | `stable`, `beta`, `dev` (Developer Edition), `nightly`, `esr` | A version (`firefox@134.0`) |
| `geckodriver` | None | Always its latest release |
| `msedgedriver` | `stable`, `beta`, `dev`, `canary` | Always the version of the installed Edge of the channel |

Microsoft Edge and Safari are never downloaded: install Edge from Microsoft, and Safari comes with macOS. `msedgedriver` downloads the driver that matches the installed Edge.

The tool prints where each target is installed, and reports download progress on standard error. A target that is already cached is not downloaded again. Every target is checked before anything is downloaded, and a target that fails does not stop the others; the exit code is 1 if any failed.

`--dry-run` shows the version and URL each target resolves to, and whether it is already cached, without downloading anything:

```bash
dramaturge browsers install --dry-run chrome@beta geckodriver
```

## Listing and Removing

```bash
dramaturge browsers list
dramaturge browsers uninstall chrome@canary chromedriver@131
dramaturge browsers clear
```

`list` shows each cached browser and driver, as `name@version`, with its channel, size, the date a channel or driver lookup last used it, and its directory. `uninstall` removes what its targets select (a channel, a milestone, a version, or everything of a name), and `clear` removes everything; both take `--dry-run` to show what they would remove. Drivers are shared by every channel, so a driver is selected by version, not channel.

## Recording Code

```bash
dramaturge codegen https://example.com/ -o Recorded.cs
```

`codegen` opens a browser with a window and writes what you do in it as C#: a program, or, with `--target`, an xUnit, NUnit, MSTest, or TUnit test class. A toolbar in the page picks locators and adds assertions. [Code Generation](https://webdriverbidi-net.github.io/dramaturge/articles/codegen.html) describes it.

## Browser Sessions

A session is a browser that stays open between commands, so that a program, or a coding agent, can drive it one command at a time:

```bash
dramaturge session start --browser firefox
dramaturge session list
dramaturge session stop
```

`session start` starts a background process that owns the browser, waits until it is ready, and returns; the browser is headless, with a temporary profile, unless `--headed` or `--user-data-dir` asks otherwise. Sessions are named with `--session` (the default name is `default`), so several can run at once, one per name. A session stops with `session stop`, when its browser closes, or after 30 minutes without a command (`--idle-timeout` sets the minutes). It records everything done in it as code from the start; `--mask-passwords` masks passwords in that code, as for `codegen`.

| Option of `session start` | Meaning |
| --- | --- |
| `--session` | The session's name: letters, digits, `-`, and `_`, up to 32 characters |
| `--browser`, `--channel` | The browser, and its release channel, as for `codegen` |
| `--headed` | Shows the browser's window |
| `--user-data-dir` | A profile directory for the browser to keep |
| `--idle-timeout` | The minutes the session waits for a command before it stops; the default is 30 |
| `--mask-passwords` | Masks passwords in the session's code |
| `--allow-origin` | An origin, such as `https://example.com`, the session's pages may load; give it once for each. Without it, every origin is allowed |
| `--allow-file-urls` | Lets `open` open `file:` URLs, which it otherwise refuses |
| `--dialogs` | How the browser answers the dialogs pages open, such as `confirm`: `dismiss` (the default) or `accept` |
| `--allow-upload-from` | A directory files may be uploaded from, besides the one the session is started in; give it once for each |
| `--downloads` | A directory to save downloads in; without it, the session refuses downloads |
| `--path` | The browser cache, as for `browsers` |
| `--json` | Writes the result, or the error, as one line of JSON (on `list` and `stop` too) |

Each session listens on a socket that only the current user can reach, in a directory of the user's own: under `$XDG_RUNTIME_DIR` (or `/tmp`) on Linux, the user's temporary directory on macOS, and the user's local application data on Windows. A session stopped by a signal (SIGTERM, SIGINT, SIGHUP) closes its browser first; one killed outright (SIGKILL), or that crashes, cannot, and leaves its browser running, while the next command that looks for it removes its socket.

Session commands write their result as text, and an error to standard error as `error[code]: message — hint`. With `--json`, each writes one line of JSON to standard output instead: `{"ok":true,"result":{…}}`, or `{"ok":false,"error":{"code":…,"message":…,"hint":…}}`. The exit code is the same either way, and the same for every command of the tool:

| Exit code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | The command failed, as when a session of the name is running already, or its browser could not start |
| 2 | The arguments cannot be used: an unknown command or option, or a value that is not valid |
| 3 | The session named is not running |

## Driving a Session

These commands drive a session's browser, one command at a time; each takes `--session` to name the session, and `--json`. [Driving a Browser from an Agent](https://webdriverbidi-net.github.io/dramaturge/articles/driving-a-browser.html) describes how they fit together.

```bash
dramaturge session start
dramaturge open https://example.com/login
dramaturge fill e3 ada
dramaturge click e5
dramaturge screenshot -o example.png
dramaturge session stop
```

| Command | Does |
| --- | --- |
| `open` *url* | Opens the address in the current tab, or in a new tab when none is open |
| `back`, `forward`, `reload` | Goes back or forward in the current tab's history, or reloads it |
| `url` | Shows the current tab's address and title |
| `snapshot` | Shows the current tab's [accessibility snapshot](https://webdriverbidi-net.github.io/dramaturge/articles/accessibility-snapshots.html), whose refs, such as `e7`, name its elements |
| `tabs` | Lists the session's tabs, numbered from 1, with `*` before the current one |
| `tab` *index* | Makes the tab of that number the current one, and brings it to the front |
| `screenshot -o` *file* | Saves a PNG of the current tab; `--full-page` captures the whole page, not only what the window shows |
| `console` | Shows the console messages and uncaught errors of every tab since the last `console`, each with its tab's number; `--all` shows every one kept, up to the latest 500 |
| `click` *ref* | Clicks the element; `--double` double-clicks it, and `--button` chooses `left` (the default), `right`, or `middle` |
| `fill` *ref* *text* | Replaces the text of a field with the text given |
| `press` *ref* *key* | Presses a key in the element: a key named as `Keys` names it, such as `Enter` or `ArrowDown`, or a character, after any modifier keys, as in `Control+a` |
| `select` *ref* *option*... | Selects the options of a select element with the labels given, as the snapshot shows them; `--by-value` matches their values instead |
| `check`, `uncheck` *ref* | Checks or unchecks a checkbox or radio button |
| `hover` *ref* | Moves the mouse over the element |
| `upload` *ref* *file*... | Chooses files for a file input |
| `screenshot -o` *file* `--ref` *ref* | Saves a PNG of the element alone |
| `expect` *condition* | Waits until the condition is met, and asserts it, as [Expectations](#expectations) describes |
| `code` | Shows the session as C#, as [Exporting the Session as Code](#exporting-the-session-as-code) describes |

`open`, `back`, `forward`, and `reload` show the address, title, and snapshot of the page they arrive at, taken as soon as the browser reports the navigation complete; `--no-snapshot` leaves the snapshot out. A tab that a page opens becomes one of the session's tabs, and when the current tab closes, the last tab left becomes the current one.

The actions take a ref from the current tab's latest snapshot, which `snapshot` takes, as do `open`, `back`, `forward`, and `reload`, and each action unless `--no-snapshot` is given. A ref that snapshot does not have is an `unknown-ref` error, and one from a document its frame has since replaced, as by a navigation, is a `stale-ref` error; either way, `snapshot` gives the refs to use. An action brings its tab to the front first, since Chrome does not draw a tab behind another, and actions wait for the page to be drawn.

Each action says what it did, and whether the tab navigated, and then shows what happened since the previous action: dialogs, with how the browser answered them, downloads, tabs opened, and errors in the console. Then it shows the snapshot taken right after it: in full after a navigation, and otherwise only what changed, each line removed marked `-` and each added `+`, with the lines that enclose them, unless that would be longer than the snapshot itself. The snapshot is taken as soon as the action is done, without waiting for what the action starts, so a navigation or a download that begins later is not reflected in it; the next action reports the download, and refuses its refs if the document changed.

Files are uploaded only from the directory the session was started in, those given with `--allow-upload-from`, and the directories below them, with every link along a file's path followed, so that no link leads out of them. Hovering is not recorded in the session's code, as for `codegen`.

What a page supplies, such as its title, snapshot, console messages, and dialogs, is written between the lines `--- page content (untrusted) ---` and `--- end of page content ---`, so that a program, or an agent, can tell it from the tool's own words and treat it as data rather than instructions.

With `--allow-origin`, `open` refuses an address of another origin, and the browser does not load a page or frame of another origin however it is reached, as by a link or a redirect. `open` refuses `file:` URLs unless the session was started with `--allow-file-urls`.

### Expectations

`expect` waits until a condition is met, for up to 5 seconds or the seconds `--timeout` gives, and fails with `expectation-failed` if it is not. It is how a session waits for something, such as the page an action leads to, as well as how it asserts it; the session's code records each expectation met that `codegen` records too, so the test it gives waits and asserts in the same places.

| Condition | Is met when |
| --- | --- |
| *ref* `visible`, `hidden`, `enabled` | The element is visible, hidden or gone, or enabled |
| *ref* `checked`, `unchecked` | The checkbox or radio button is checked, or not |
| *ref* `text` *text* | The element's text contains the text |
| *ref* `value` *value* | The field's value is the value |
| *ref* `snapshot` [*template*] | The element's [accessibility snapshot](https://webdriverbidi-net.github.io/dramaturge/articles/accessibility-snapshots.html) matches the template, or, without one, its snapshot as it is now, which is shown |
| `url` *url*, `title` *title* | The tab's address, or title, is the one given |
| `text` *text* | The text of the tab's page, hidden text included, contains the text |

```bash
dramaturge click e5
dramaturge expect url https://example.com/welcome --timeout 30
dramaturge expect text "Welcome, Ada"
```

Every condition but page-level `text` is recorded with a locator for its element; `hidden` chooses the locator when it starts waiting, while the element is still there, since by the time it is met the element may be gone. When an expectation fails, what the page had instead follows the error between the page content lines, or, with `--json`, is the error's `details`. There is no `wait` command; `expect` waits.

### Exporting the Session as Code

`code` shows everything done in the session since it started, or since `code --reset`, as C#, as `codegen` would have written it: a program, or, with `--target`, an xUnit, NUnit, MSTest, or TUnit test class. `-o` writes it to a file instead. The actions and expectations of the session become statements, with locators chosen as [Code Generation](https://webdriverbidi-net.github.io/dramaturge/articles/codegen.html#how-locators-are-chosen) describes, so that refs, which name elements only in the session, never appear in the code; the session's own navigations become `NavigateAsync`, and an action that navigated is wrapped in `RunAndWaitForNavigationAsync`.

```bash
dramaturge code --target xunit -o LoginTests.cs
```

The code is written without the page content lines, so that it can be redirected to a file, though it holds text from the pages, such as accessible names, in its string literals; review it as any generated code. The code of a session started with `--dialogs accept` sets its browser to accept dialogs too. `--reset` starts the recording over once the code is written, so that the code comes from what is done next, without the exploring before it; `open` the page the test starts from right after it.

## MCP Server

`mcp` serves the session commands to a Model Context Protocol host, such as Claude Desktop or Claude Code, over its standard input and output, as tools: `session_start`, `session_list`, and `session_stop`, then each command of [Driving a Session](#driving-a-session) under its own name, with the arguments the command takes and an optional `session` to name the session.

```bash
dramaturge mcp --allow-origin https://staging.example.com --downloads ./downloads
```

| Option of `mcp` | Meaning |
| --- | --- |
| Every option of `session start` but `--session`, `--browser`, and `--channel` | Applies to every session the host starts; `session_start` takes the name, browser, and channel |
| `--path` | The browser cache, as for `browsers` |

The host adds the server to its configuration; in Claude Code, `claude mcp add dramaturge -- dramaturge mcp`. A tool's result is the command's answer, as text with the page content lines, or its error line and details; `screenshot` returns the image unless it is given a `path`. The sessions the host started stop when it closes the connection. [Driving a Browser from an Agent](https://webdriverbidi-net.github.io/dramaturge/articles/driving-a-browser.html#through-mcp) describes the tools from the host's side.

## The Cache

`browsers`, `codegen`, `session start`, and `mcp` take `--path` to choose the cache directory. Otherwise, the tool uses the same directory as the library: `DRAMATURGE_BROWSERS_PATH` if it is set, or else the per-user cache directory. The library's other environment variables apply too, such as `DRAMATURGE_DOWNLOAD_MANIFEST` to download from a mirror. The [Browser Setup guide](https://webdriverbidi-net.github.io/dramaturge/articles/browser-setup.html) describes them.

A test run can then use what the tool installed:

```bash
export DRAMATURGE_BROWSERS_PATH="$PWD/.browsers"
dramaturge browsers install chrome chromedriver
export DRAMATURGE_SKIP_DOWNLOAD=1
dotnet test
```

With `DRAMATURGE_SKIP_DOWNLOAD` set, a test that needs a browser the tool did not install fails at once rather than downloading it.
