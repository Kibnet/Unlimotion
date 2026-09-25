# Unlimotion

<p align="center">
  <img src="assets/branding/readme-logo-512.png" alt="Unlimotion logo" width="256" height="256">
</p>

[Русский](README.RU.md)

Unlimotion is a task planner built around a graph: a task can belong to several projects, break down into smaller steps, and depend on other tasks being completed first.

## Features

- Unlimited nesting and multiple parents for a single task.
- Dependencies and an Unlocked view to help choose the next step.
- Separate task spaces for work, personal projects and other areas.
- Completion criteria, planned dates and recurring task trees.
- Local JSON files and optional Git backup and synchronization.
- A CLI for automation and agents that follows the same task rules.

![Unlimotion tab tour](media/readme/en/tab-tour.gif)

> Images show a demo build of the current branch with fictional tasks. It may differ from the latest stable release: for example, the current icon and settings recovery were added after 1.31.1. The version shown in the test build is not a published release number.

## Download and install

Ready-to-run self-contained published builds are available on the [latest GitHub release](https://github.com/Kibnet/Unlimotion/releases/latest) page. They do not require the .NET SDK. An artifact being published does not guarantee compatibility with every OS version; a complete platform smoke-test matrix is still being established.

| Available build | File to choose | Current validation status |
| --- | --- | --- |
| Windows x64 | `Unlimotion-win-Setup.exe` or `Unlimotion-win-Portable.zip` | Published. The project does not currently publish verified Authenticode evidence. Microsoft Defender SmartScreen may show a warning. |
| Linux x64 (AppImage) | `Unlimotion.AppImage` | Published generic Linux option; distribution compatibility has not yet been smoke-tested as a complete matrix. |
| Linux x64 (.deb) | `Unlimotion-v<version>.deb` | Preview. Compatibility with current Debian releases has not yet been verified. |
| macOS x64 | `Unlimotion-osx-Setup.pkg` or `Unlimotion-osx-Portable.zip` | Intel build. The project does not currently publish verified Developer ID signing and notarization evidence. |
| macOS arm64 | `Unlimotion-osx-arm64-Setup.pkg` or `Unlimotion-osx-arm64-Portable.zip` | Apple Silicon build. The project does not currently publish verified Developer ID signing and notarization evidence. |
| Android arm64 | `Unlimotion-v<version>-android-arm64.apk` | Sideloaded APK. The project declares Android 6.0 / API 23 as its minimum; this is not a universal device-compatibility guarantee. |
| Android x64 | `Unlimotion-v<version>-android-x64.apk` | Sideloaded APK, primarily for x86_64 devices and emulators; the same Android minimum-version caveat applies. |

- For a Windows or macOS portable ZIP, extract the archive before starting the included application.
- For the AppImage, download it and run:

```bash
chmod +x Unlimotion.AppImage
./Unlimotion.AppImage
```

- On macOS, the project does not currently publish verified signing and notarization evidence for these packages. Gatekeeper may block them. If you trust the downloaded artifact, follow Apple's official [Open Anyway guidance](https://support.apple.com/en-us/102445). Changing file permissions does not establish trust or notarization.
- Android installation is performed outside an app store. The OS may ask you to allow installation from the selected source; the exact permission flow depends on the Android version and device. When updating in the app, Android downloads the matching APK and asks for system installation confirmation.
- The desktop in-app updater is available only when Velopack recognizes the current installation as managed. Portable and source runs must not rely on that updater; use the Releases page instead.

## Quick start

1. Open the app and create a task with **➕**. Give it a title, such as “Plan a trip”.
2. In the task card on the right, add a description and completion criteria: what must be done before the task is complete.
3. Use **Ctrl+Tab** to add steps, such as “Choose dates” and “Book tickets”. Add a blocking relation when one step depends on another.
4. Open **Unlocked** and choose suitable status and time filters. This view helps find tasks without unfinished children or blockers.
5. Move a prepared task to **In progress**, then to **Completed** after satisfying its criteria. If a transition is unavailable, the status picker explains why.

Press **F1** for the keyboard shortcut reference. Create separate task spaces in Settings to keep work and personal tasks apart.

## How tasks work

### Task spaces

One application instance can keep several named task spaces. Use the selector in the main header to switch the active space, and the first Settings section to add, rename, switch, or remove configured spaces.

Only one space is active at a time. Its task source and complete Git synchronization profile are isolated from every other space; tasks from multiple spaces are never shown together and cross-space task relations are rejected. To configure storage or Git for another space, switch to it first. Removing a space removes only its configuration and stored credentials from this application—it does not delete or move the task files or remote repository.

### Task states

Any task can be in only one of five statuses:

1. Not ready - an empty square
2. Prepared - a square with `!`
3. In progress - a square with a play sign
4. Completed - a square with a check mark
5. Archived - a square inside a square

**Prepared** means a task has enough context to be started or delegated. Starting requires its active children and blockers to be finished and its planned begin date to have arrived. Completion also requires every completion criterion to be satisfied. A future begin date alone does not prevent completion.

When archiving a task, the app can offer to archive its active children as well, taking the process out of active work while retaining its history.

<details>
<summary>Detailed status rules and exchange format</summary>

Lifecycle status, graph availability and transition guards are separate concepts:

- `Prepared` means that the task has enough context to be started or delegated.
- Graph availability is calculated from active contained tasks, direct blockers and blockers inherited from parent tasks. The status control of a graph-unavailable task is shown with opacity `0.4`; completed and archived blockers do not block it.
- A future planned begin date is only a guard for entering `In progress`. It does not change graph availability, the `Unlocked` projection or opacity.

The canonical transition matrix is:

| Current \ requested | Not ready | Prepared | In progress | Completed | Archived |
| --- | --- | --- | --- | --- | --- |
| Not ready | No-op | Allowed | Start guards | Completion guards | Allowed |
| Prepared | Allowed | No-op | Start guards | Completion guards | Allowed |
| In progress | Allowed | Allowed | No-op | Completion guards | Allowed |
| Completed | Allowed | Allowed | Denied | No-op | Denied |
| Archived | Allowed | Allowed | Denied | Denied | No-op |

`Start guards` require graph availability and a planned begin date that is not in the future. `Completion guards` require graph availability and every completion criterion to be satisfied. A same-status request is a no-op and does not add a history entry.

If an `In progress` task loses graph availability or receives a future planned begin date, it is moved once to `Prepared` with a system history entry. Unarchiving restores `Not ready` as `Not ready`, and restores both `Prepared` and `In progress` as `Prepared`. Invalid history entries are ignored, so an older valid non-archived entry can still determine the result; a previous `Completed` status or the absence of any valid non-archived entry falls back to `Not ready`.

Markdown outline import/export uses these markers:

| Marker | Status |
| --- | --- |
| `[ ]` | Not ready |
| `[!]` | Prepared |
| `[>]` | In progress |
| `[x]` | Completed |
| `[#]` | Archived |

The same status picker is available in task lists, the roadmap and the current task card. It hides the current status and shows the other four targets; denied targets remain visible but disabled with a localized reason. The Telegram bot shows only enabled non-current targets and applies the same storage-backed transition contract.

</details>

### Task relations

Each task can have links to other tasks of 4 types:

1. **Parents Tasks** - parent tasks that contain this task within themselves as an integral part necessary for execution.
2. **Containing Tasks** - child tasks that are part or steps of this task and arise during the decomposition process.
3. **Blocking By Tasks** - blocking tasks that must be completed to unlock the current one.
4. **Blocked Tasks** - blocked tasks that cannot be unblocked while this task is not completed.

### Hierarchy of tasks

All tasks have their place in the general tree hierarchy. The very first level is called the root level, it contains tasks that have no links to parent tasks.
Any task can become a child of another task, visually it looks like a nesting in the task tree.
Unlimotion allows one task to be a child of several other tasks at once.
This is convenient in cases where the task has an inter project value and cannot be assigned to only one parent task.
Also, this feature can be used as a replacement for tags, you can create a parent task with a certain meaning and add to it all the child tasks that are related to this meaning.
This will allow you to observe the same task in different slices right at the hierarchy level.

### Blocking

A graph-unavailable task cannot be started or completed until it becomes available. Its status control is shown with opacity `0.4`.

A task is graph-unavailable if it has:

1. Active incomplete contained tasks.
2. Active incomplete direct blockers.
3. Active incomplete blockers inherited from any parent task.

Archived and completed related tasks are not incomplete blockers. Graph availability does not prevent moving a task to `Not ready`, `Prepared` or `Archived` when the lifecycle matrix allows that target.

### Planning and recurring tasks

The task card lets you set a planned start, duration and end, mark a task as wanted, and add verifiable completion criteria.

To configure recurrence, first choose a begin date, then a pattern such as daily, weekdays or selected days of the week. Clearing the begin date also clears recurrence.

The next occurrence receives **its own subtree**: new tasks, criteria and internal relations, including nested levels. Dates shift for the new occurrence; external relations of the original subtree are not copied. This is useful for a recurring project review with the same set of steps.

## Interface description

The header contains the task space selector and the path to the selected task. Tabs on the left provide different task views and Settings; the card on the right contains the description, criteria, planning fields, relations and status history.

The images below use a synthetic demo dataset.

### All Tasks

It's a hierarchical representation of all tasks.
At the root are those tasks that don't have parents.
![All Tasks](media/readme/en/all-tasks.png)

### Last Created

Shows all tasks by creation date in descending order.
![Last Created](media/readme/en/last-created.png)

### Last Updated

Shows the tasks with the most recent edits first.
![Last Updated](media/readme/en/last-updated.png)

### Unlocked

The `Unlocked` projection contains graph-available, non-archived tasks. Status and time filters are applied separately, so it can include completed or future-planned tasks; a future task remains fully opaque but cannot enter `In progress` before its planned begin date.
![Unlocked](media/readme/en/unlocked.png)

### In Progress

A flat list of tasks with the `In progress` status. The tab helps track current work and how long each task has already been in that status.
![In Progress](media/readme/en/in-progress.png)

### Completed

The list of completed tasks in the reverse order of execution - the last ones from the top.
![Completed](media/readme/en/completed.png)

### Archived

The list of archived tasks in the reverse order of archiving - the last ones from the top. This includes tasks that no longer need to be performed, but you don't want to delete them either.
![Archived](media/readme/en/archived.png)

### Last Opened

Shows the tasks that were opened most recently, which is useful for quickly returning to the working context you just left.
![Last Opened](media/readme/en/last-opened.png)

### Roadmap

Displaying tasks in the form of a roadmap. Inspired by the development tree from games. In this view, tasks are displayed in the form of a directed graph, which allows you to visualize the tracks of tasks that need to be completed in order to reach the goal.
Green arrows - child-parent relationship
Red arrows - the ratio of the blocking task to the blocked one
![Roadmap](media/readme/en/roadmap.png)

### Settings

The Settings tab includes:

- Task spaces: create, switch, rename and remove a space from the app.
- The shortcut reference, language, theme, text size, smart search and remembering expanded nodes.
- Markdown task-outline copying and including descriptions in copied outlines.
- Checking for and installing updates, depending on the build type.
- The task source: a local JSON directory or a server connection.
- Git backup: repository, authentication, SSH keys, manual and automatic synchronization, and conflict resolution.

Source and Git settings belong to the active space. After changing a connection, use the corresponding connect button and check its status.

![Settings](media/readme/en/settings.png)

## Shortcuts and task operations

### Tasks creation

New tasks are always created relative to the selected task. To do this, you can use the buttons on the top right panel with the sign "➕" or use hotkeys:

- **➕Sibling (Ctrl+Enter)** - Create a task at the same level as the selected one
- **➕🔒Sibling (Shift+Enter)** - Create a task at the same level as the selected one and block it with the selected one
- **➕Inner (Ctrl+Tab)** - Create a nested task inside the selected
- **Complete current task (Ctrl+D)** - Mark the selected/current task as completed

The Settings button named **Show keyboard shortcuts** and the **F1** key open the full in-app shortcut reference.

After creating a task, you need to fill in the name of the current task, because if a task without a name is selected, the creation buttons will be disabled.

### Tree expansion shortcuts

In all task list tabs and relation trees on the task card, you can control expansion with these hotkeys:

- **Expand nested for current (Ctrl+Shift+Right)** - Expand the selected node and all nested nodes under it
- **Collapse nested for current (Ctrl+Shift+Left)** - Collapse nested nodes under the selected node
- **Expand all nodes (Ctrl+Alt+Right)** - Expand all nodes in the active tree
- **Collapse all nodes (Ctrl+Alt+Left)** - Collapse all nodes in the active tree

### Tasks deleting

Tasks are permanently deleted when you click the "❌" button in the task list.
Also, when you press **Shift+Delete**, the selected task is deleted.

### Dragging with the mouse

In all tabs, you can perform drag-and-drop actions with the left mouse button. The task that you pulled is called draggable.
The task on which you release the mouse button is called the target.
Depending on which buttons on the keyboard are clamped when you release the left mouse button, different commands are executed:

- **Without keys** - Attach a draggable task to a target task
- **Shift** - Move the dragged task to the target task
- **Ctrl** - The dragged task blocks the target task
- **Alt** - The target task blocks the dragged task
- **Ctrl+Shift** - Clone the dragged task to the target as a subtask

### Search and emoji filters

Emoji in task titles help group tasks. Filters show their hierarchy with expandable branches: search by title or emoji and select several include/exclude values without closing the list after each choice.

Flat lists and the task card show parent emoji, making it easier to see which projects a task belongs to.

Use the system emoji picker: **Win+.** on Windows or **Cmd+Ctrl+Space** on macOS. On Linux, emoji input depends on the desktop environment and input settings.

## Storage and backup

Local use does not require a server: tasks are individual JSON files in the active space's directory. Check its path in the storage settings. Desktop accepts absolute and relative paths; its default is `Tasks` relative to the launch working directory. An explicit absolute path is easier to manage for your own storage.

A regular installed desktop build keeps settings in `Documents/Unlimotion/Settings.json` in your user profile; `--config=<path>` can override this location. Debug runs from source use `Settings.json` in the working directory. Before moving data, check the actual task path in Settings: it is a separate directory and is not necessarily next to the settings file.

Configure Git separately for each space. In **Backup**, enter the repository and authentication details, connect it and verify manual synchronization. Automatic backup must be enabled in Settings. For SSH, select or generate a key; **SSH Key Storage Path** specifies the directory for keys and a dedicated `known_hosts` file. The app provides a conflict-resolution interface for synchronization conflicts.

Removing a space from Settings does not delete its JSON files or remote repository. Deleting a task itself is a separate, permanent operation.

### Exchanging task outlines

**Copy task outline** (**Ctrl+Shift+C**) and **Paste task outline** (**Ctrl+Shift+V**) transfer a hierarchy through the clipboard. Settings can enable Markdown output and copying descriptions. Pasting shows a confirmation with the number of tasks and the destination. Status markers are listed in the expandable status reference above.

## CLI and automation

The CLI works with a local task directory without launching the UI. Scripts and agents can read tasks and relations, create tasks, update criteria and statuses, claim work, and record questions, answers and results.

Installing through `dotnet tool` requires the **.NET 10 SDK**; the CLI itself runs on the .NET 10 runtime. The self-contained desktop builds do not need a separate .NET installation.

```powershell
dotnet tool install --global Unlimotion.Cli
unlimotion-cli status --format json
```

To update:

```powershell
dotnet tool update --global Unlimotion.Cli
```

Without `--tasks`, the CLI uses the active local space from the installed app's standard settings file. Explicit `--tasks <directory>` takes precedence and is also needed when using a custom configuration location. The CLI resolves relative task paths from the directory containing `Settings.json`.

The agent lifecycle is `candidates` → `task` → `claim` → questions/answers and results through `execution` → `execution complete` or `release`. Execution commands check the agent and lease identifiers; completion also checks task availability and criteria.

Multiline descriptions and Markdown are preserved as source text. This does not imply a formatted Markdown viewer in desktop.

### Batch changes

`unlimotion-cli unlocked --root <task-id> --format json` returns the startable tasks in the selected task and its contained subtree. Each expanded `task` response includes an `etag`; use it as a precondition when an approved proposal changes an existing task.

Store the application JSON outside the task directory, inspect it first, then preview and apply it:

```text
unlimotion-cli apply --tasks <task-directory> --request <approved-request.json> --dry-run --format json
unlimotion-cli apply --tasks <task-directory> --request <approved-request.json> --format json
```

An application identifies its proposal revision, author, reason, optimistic `etag` preconditions and declarative operations. The CLI validates the whole task graph before writing and returns JSON with `preview`, `applied`, or a structured refusal. A successful application records a local receipt under `.unlimotion.applies/v1`; retrying the identical request returns `alreadyApplied`. If a crash occurs after the task transaction but before the receipt, the same deterministic operations are reconciled against the authoritative graph rather than written twice.

For `outcomeUnknown`, read back the state before retrying a change. See the [CLI documentation](src/Unlimotion.Cli/README.md) and `unlimotion-cli --help` for request formats and detailed rules.

## Recent releases

| Release | Main changes |
| --- | --- |
| [1.31.1](https://github.com/Kibnet/Unlimotion/releases/tag/v1.31.1) | Batch graph changes through the CLI, multiline Markdown context preservation, refreshed icon and loading animation. |
| [1.31.0](https://github.com/Kibnet/Unlimotion/releases/tag/v1.31.0) | Faster loading and space switching, the full agent lifecycle in the CLI, hierarchical emoji filters and parent emoji, archiving child tasks. |
| [1.30.1](https://github.com/Kibnet/Unlimotion/releases/tag/v1.30.1) | CLI installation from NuGet and selecting the active local space from desktop settings. |
| [1.30.0](https://github.com/Kibnet/Unlimotion/releases/tag/v1.30.0) | Reliable reflection of file changes in the UI and preserving current edits when storage updates. |
| [1.29.0](https://github.com/Kibnet/Unlimotion/releases/tag/v1.29.0) | Task spaces, independent recurring subtrees and saving edited fields together with status changes. |

In the 1.31.0 measurements on **2,879 tasks**, loading to readiness became **7.20× faster** at startup and **12.52× / 10.24× faster** when switching A → B / B → A. These are specific comparative measurements, not a guarantee for every device and dataset; the release notes describe the conditions and additional measurements.

[All releases and upgrade notes](https://github.com/Kibnet/Unlimotion/releases).

## Build and run from source

Cloning `main` gives you the current development snapshot. For a stable build or matching source archive, use the Releases page above.

Prerequisites:

- Git
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), compatible with `global.json`
- Network access to NuGet for the first restore

Run the commands from the repository root.

Windows PowerShell:

```powershell
git clone https://github.com/Kibnet/Unlimotion.git
Set-Location Unlimotion
.\run.windows.cmd
```

Linux or macOS:

```bash
git clone https://github.com/Kibnet/Unlimotion.git
cd Unlimotion

# Linux
bash ./run.linux.sh

# macOS
bash ./run.macos.sh
```
