# Unlimotion

[Русская версия](README.RU.md)

![Desktop tab tour](media/readme/en/tab-tour.gif)

Unlimotion is a task planner for work that needs more than a simple list: a task can belong to several projects, depend on other tasks, and be planned through a shared task graph.

## Highlights

- Build a multi-level task graph; a task can have more than one parent.
- Model prerequisites with blockers and see which tasks are ready to work on.
- Track task status, planned start dates, and completion criteria.
- Keep tasks in local storage by default, with server storage and Git backup available in Settings.
- Inspect and automate task directories through the included command-line client.

## Download and install

Published packages are available on the [latest GitHub release](https://github.com/Kibnet/Unlimotion/releases/latest) page. Choose an artifact for your platform:

| Platform | Published artifact types |
| --- | --- |
| Windows x64 | Setup program or portable ZIP archive |
| Linux x64 | AppImage or Debian package |
| macOS Intel | Installer package or portable ZIP archive |
| macOS Apple Silicon | Installer package or portable ZIP archive |
| Android arm64 | APK for manual installation |
| Android x64 | APK primarily intended for x86_64 devices and emulators |

Artifact availability is not a compatibility promise for every OS release or device. Check the release notes and select the package that matches your platform.

- Extract a portable ZIP archive before starting the application inside it.
- To run the Linux AppImage:

~~~bash
chmod +x Unlimotion.AppImage
./Unlimotion.AppImage
~~~

- Android packages are installed outside an app store. Android may ask you to allow installation from the selected source and to confirm the install.
- If macOS prevents you from opening an artifact you trust, follow Apple’s official [Open Anyway guidance](https://support.apple.com/en-us/102445).

## Build and run from source

Cloning main gives you the current development snapshot. For a stable package or its matching source archive, use the Releases page above.

Prerequisites:

- Git
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) compatible with global.json
- Network access to NuGet for the first restore

Run the commands from the repository root.

Windows PowerShell:

~~~powershell
git clone https://github.com/Kibnet/Unlimotion.git
Set-Location Unlimotion
.\run.windows.cmd
~~~

Linux or macOS:

~~~bash
git clone https://github.com/Kibnet/Unlimotion.git
cd Unlimotion

# Linux
bash ./run.linux.sh

# macOS
bash ./run.macos.sh
~~~

## How tasks work

### Task spaces

One application instance can keep several named task spaces. Use the selector in the main header to switch the active space, and the first Settings section to add, rename, switch, or remove configured spaces.

Only one space is active at a time. Its task source and complete Git synchronization profile are isolated from every other space; tasks from multiple spaces are never shown together and cross-space task relations are rejected. To configure storage or Git for another space, switch to it first. Removing a space removes only its configuration and stored credentials from this application—it does not delete or move the task files or remote repository.

### Status and availability

Every task has one lifecycle status:

| Status | Markdown-outline marker |
| --- | --- |
| Not ready | [ ] |
| Prepared | [!] |
| In progress | [>] |
| Completed | [x] |
| Archived | [#] |

Status and graph availability are separate. A task is graph-unavailable while it has active incomplete contained tasks, direct blockers, or blockers inherited through a parent task. An unavailable task cannot be started or completed. A future planned start date also prevents moving a task to In progress, and all completion criteria must be satisfied before completing it.

### Relations and hierarchy

A task can be connected to other tasks in four ways:

1. **Parent tasks** contain the current task.
2. **Contained tasks** are steps or parts of the current task.
3. **Blockers** must be completed before the current task is available.
4. **Blocked tasks** wait for the current task to be completed.

Tasks without parent tasks appear at the root. A task may be contained by several parents, so the same work can be visible in more than one project or meaningful grouping without being duplicated.

## Interface

The workspace combines breadcrumbs for the selected task, a left navigation panel with task projections, and a right-hand details panel where the selected task can be viewed and edited.

### All Tasks

Shows the complete task hierarchy. Root tasks have no parents.

![All Tasks](media/readme/en/all-tasks.png)

### Last Created

Shows tasks in descending creation-date order.

![Last Created](media/readme/en/last-created.png)

### Last Updated

Shows tasks with the most recent edits first.

![Last Updated](media/readme/en/last-updated.png)

### Unlocked

Shows graph-available, non-archived tasks. Status and time filters are applied independently.

![Unlocked](media/readme/en/unlocked.png)

### Completed

Shows completed tasks, with the most recently completed first.

![Completed](media/readme/en/completed.png)

### In Progress

Shows tasks whose status is In progress.

![In Progress](media/readme/en/in-progress.png)

### Archived

Shows archived tasks, with the most recently archived first.

![Archived](media/readme/en/archived.png)

### Last Opened

Shows recently opened tasks so you can return to the context you were working in.

![Last Opened](media/readme/en/last-opened.png)

### Roadmap

Visualizes tasks as a directed graph. Green arrows represent parent–contained-task relations; red arrows represent blockers.

![Roadmap](media/readme/en/roadmap.png)

### Settings

Settings provide:

- language, theme, font size, fuzzy search, and saved tree-expansion state;
- clipboard options for copying task outlines;
- update-check settings and, when supported by the installation, update actions;
- local-folder or server task storage;
- Git backup, including remote and SSH configuration.

For local storage, leaving the data-folder field empty uses the platform local-application-data directory under Unlimotion/Tasks. You can choose another local folder in Settings.

![Settings](media/readme/en/settings.png)

## CLI and automation

[Unlimotion CLI](src/Unlimotion.Cli/README.md) lets scripts and agents inspect, validate, and perform controlled updates on a specified task directory without starting the UI. Its guide is the authoritative reference for installation, commands, output formats, and write behavior.

## Getting started

### Create tasks

New tasks are created relative to the currently selected task:

- **Sibling (Ctrl+Enter)** — create a task at the same level.
- **Blocked sibling (Shift+Enter)** — create a sibling blocked by the selected task.
- **Inner (Ctrl+Tab)** — create a task inside the selected task.
- **Complete current task (Ctrl+D)** — complete the selected task when its completion rules are met.

The **Show keyboard shortcuts** button in Settings and the **F1** key open the in-app shortcut reference.

### Expand task trees

In task lists and relation trees, use:

- **Ctrl+Shift+Right** — expand the selected node and its nested tasks.
- **Ctrl+Shift+Left** — collapse nested tasks below the selected node.
- **Ctrl+Alt+Right** — expand all nodes in the active tree.
- **Ctrl+Alt+Left** — collapse all nodes in the active tree.

### Delete tasks

Use the delete button in a task list or press **Shift+Delete** to permanently delete the selected task.

### Drag and drop

Drag a task onto another task to change their relation:

- no modifier — attach the dragged task to the target;
- **Shift** — move the dragged task to the target;
- **Ctrl** — make the dragged task block the target;
- **Alt** — make the target block the dragged task;
- **Ctrl+Shift** — clone the dragged task into the target as a subtask.

### Group by emoji

Emoji in task names can be used as filters. The emoji-filter menu supports search and multi-selection, including include and exclude filters. In flat task projections, inherited emoji from parent tasks are displayed next to the task title to help show its context.
