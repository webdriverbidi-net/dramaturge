# Command-Line Tool

The `dramaturge` command-line tool, from the `Dramaturge.Tool` package, installs, lists, and removes the browsers and drivers that [Dramaturge.Browsers](browser-setup.md) keeps in its cache, records what you do in a browser as C#, and keeps browser sessions open for later commands to drive. Installing is most useful in continuous integration: install the browsers while building a machine image or before the tests run, so that no test waits for a download.

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

`codegen` opens a browser with a window and writes what you do in it as C#: a program, or, with `--target`, an xUnit, NUnit, MSTest, or TUnit test class. A toolbar in the page picks locators and adds assertions. [Code Generation](codegen.md) describes it.

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

## The Cache

`browsers`, `codegen`, and `session start` take `--path` to choose the cache directory. Otherwise, the tool uses the same directory as the library: `DRAMATURGE_BROWSERS_PATH` if it is set, or else the per-user cache directory. The library's other environment variables apply too, such as `DRAMATURGE_DOWNLOAD_MANIFEST` to download from a mirror. [Browser Setup](browser-setup.md#downloads-and-the-cache) describes them.

A test run can then use what the tool installed:

```bash
export DRAMATURGE_BROWSERS_PATH="$PWD/.browsers"
dramaturge browsers install chrome chromedriver
export DRAMATURGE_SKIP_DOWNLOAD=1
dotnet test
```

With `DRAMATURGE_SKIP_DOWNLOAD` set, a test that needs a browser the tool did not install fails at once rather than downloading it.
