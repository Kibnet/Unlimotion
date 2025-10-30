# Git Integration Configuration

<cite>
**Referenced Files in This Document**
- [GitSettings.cs](file://src/Unlimotion.ViewModel/TaskStorageSettings.cs)
- [GitPullJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPullJob.cs)
- [GitPushJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPushJob.cs)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs)
- [GitService.cs](file://src/Unlimotion.TelegramBot/GitService.cs)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [SettingsViewModel.cs](file://src/Unlimotion.ViewModel/SettingsViewModel.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)
- [TestSettings.json](file://src/Unlimotion.Test/TestSettings.json)
- [NotificationManagerWrapper.cs](file://src/Unlimotion/NotificationManagerWrapper.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [GitSettings Configuration Class](#gitsettings-configuration-class)
3. [Quartz.NET Scheduled Jobs](#quartznet-scheduled-jobs)
4. [Git Operations Implementation](#git-operations-implementation)
5. [Configuration Patterns](#configuration-patterns)
6. [Authentication Methods](#authentication-methods)
7. [Repository Management](#repository-management)
8. [User Feedback System](#user-feedback-system)
9. [Troubleshooting Guide](#troubleshooting-guide)
10. [Best Practices](#best-practices)
11. [Security Considerations](#security-considerations)

## Introduction

Unlimotion provides a comprehensive Git backup and synchronization system that enables automated version control for task data through LibGit2Sharp library integration. The system supports both desktop and Telegram bot implementations, offering flexible configuration options for different repository types and authentication methods.

The Git integration consists of several key components:
- **Configuration Management**: Centralized GitSettings class with comprehensive property definitions
- **Scheduled Operations**: Quartz.NET-based job scheduling for automatic pull and push operations
- **Service Layer**: Robust backup service with conflict resolution and error handling
- **User Interface**: Real-time notifications and status feedback
- **Multi-platform Support**: Desktop application and Telegram bot implementations

## GitSettings Configuration Class

The GitSettings class serves as the central configuration hub for all Git-related operations in Unlimotion. It defines all necessary properties for establishing and maintaining Git repository connections.

### Core Properties

```mermaid
classDiagram
class GitSettings {
+bool BackupEnabled
+bool ShowStatusToasts
+string RemoteUrl
+string Branch
+string UserName
+string Password
+int PullIntervalSeconds
+int PushIntervalSeconds
+string RemoteName
+string PushRefSpec
+string CommitterName
+string CommitterEmail
}
class TaskStorageSettings {
+string Path
+string URL
+string Login
+string Password
+bool IsServerMode
}
GitSettings --> TaskStorageSettings : "configured alongside"
```

**Diagram sources**
- [GitSettings.cs](file://src/Unlimotion.ViewModel/TaskStorageSettings.cs#L15-L34)
- [TaskStorageSettings.cs](file://src/Unlimotion.ViewModel/TaskStorageSettings.cs#L1-L13)

#### Essential Configuration Properties

| Property | Type | Default Value | Description |
|----------|------|---------------|-------------|
| `BackupEnabled` | bool | false | Enables/disables automatic Git operations |
| `RemoteUrl` | string | null | Full URL to the remote Git repository |
| `Branch` | string | "master" | Target branch for operations |
| `UserName` | string | "YourEmail" | Authentication username or email |
| `Password` | string | "YourToken" | Authentication password or personal access token |

#### Interval Settings

| Property | Type | Default Value | Description |
|----------|------|---------------|-------------|
| `PullIntervalSeconds` | int | 30 | Frequency of automatic pull operations (seconds) |
| `PushIntervalSeconds` | int | 60 | Frequency of automatic push operations (seconds) |

#### Git Metadata Configuration

| Property | Type | Default Value | Description |
|----------|------|---------------|-------------|
| `RemoteName` | string | "origin" | Name of the remote repository |
| `PushRefSpec` | string | "refs/heads/master" | Reference specification for push operations |
| `CommitterName` | string | "Backuper" | Name used for Git commits |
| `CommitterEmail` | string | "Backuper@unlimotion.ru" | Email used for Git commits |

**Section sources**
- [GitSettings.cs](file://src/Unlimotion.ViewModel/TaskStorageSettings.cs#L15-L34)
- [TestSettings.json](file://src/Unlimotion.Test/TestSettings.json#L8-L22)

## Quartz.NET Scheduled Jobs

Unlimotion utilizes Quartz.NET for reliable scheduling of Git operations, ensuring automatic backup and synchronization without manual intervention.

### Job Architecture

```mermaid
sequenceDiagram
participant Scheduler as "Quartz Scheduler"
participant PullJob as "GitPullJob"
participant PushJob as "GitPushJob"
participant BackupService as "BackupViaGitService"
participant Repository as "Git Repository"
Scheduler->>PullJob : Execute() every PullIntervalSeconds
PullJob->>PullJob : Check BackupEnabled
PullJob->>BackupService : Pull()
BackupService->>Repository : Fetch + Merge
Repository-->>BackupService : Merge results
BackupService-->>PullJob : Operation complete
Scheduler->>PushJob : Execute() every PushIntervalSeconds
PushJob->>PushJob : Check BackupEnabled
PushJob->>BackupService : Push("Backup created")
BackupService->>Repository : Stage + Commit + Push
Repository-->>BackupService : Push successful
BackupService-->>PushJob : Operation complete
```

**Diagram sources**
- [GitPullJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPullJob.cs#L10-L19)
- [GitPushJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPushJob.cs#L10-L19)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L129-L203)

### Job Configuration

The scheduling system dynamically adjusts job frequencies based on user configuration changes:

```mermaid
flowchart TD
ConfigChange["Configuration Change"] --> CheckType{"Job Type"}
CheckType --> |PullInterval| UpdatePull["Reschedule Pull Job"]
CheckType --> |PushInterval| UpdatePush["Reschedule Push Job"]
CheckType --> |BackupEnabled| ToggleScheduler["Toggle Scheduler State"]
UpdatePull --> GenerateTrigger["Generate New Trigger"]
UpdatePush --> GenerateTrigger
ToggleScheduler --> StartStopScheduler["Start/Pause Scheduler"]
GenerateTrigger --> ApplyChanges["Apply Changes"]
StartStopScheduler --> ApplyChanges
ApplyChanges --> Complete["Operation Complete"]
```

**Diagram sources**
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs#L31-L81)

**Section sources**
- [GitPullJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPullJob.cs#L1-L20)
- [GitPushJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPushJob.cs#L1-L21)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs#L31-L81)

## Git Operations Implementation

The BackupViaGitService provides comprehensive Git operations with robust error handling, conflict resolution, and user feedback mechanisms.

### Service Architecture

```mermaid
classDiagram
class IRemoteBackupService {
<<interface>>
+string[] Remotes()
+string[] Refs()
+void Push(string msg)
+void Pull()
+void CloneOrUpdateRepo()
}
class BackupViaGitService {
-const string TasksFolderName
-static object LockObject
+string[] Remotes()
+string[] Refs()
+CredentialsHandler GetCredentials(GitSettings)
+void CloneOrUpdateRepo()
+void Push(string msg)
+void Pull()
-string GetRepositoryPath(string)
-void CheckGitSettings(string, string)
-void ShowUiError(string)
-void ShowUiMessage(string)
}
class GitService {
-GitSettings settings
-CredentialsHandler credentialsProvider
+void CloneOrUpdateRepo()
+void PullLatestChanges()
+void CommitAndPushChanges(string)
}
IRemoteBackupService <|.. BackupViaGitService
BackupViaGitService --> GitService : "used by"
```

**Diagram sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L15-L356)
- [GitService.cs](file://src/Unlimotion.TelegramBot/GitService.cs#L10-L109)

### Core Git Operations

#### Repository Initialization and Cloning

The system automatically initializes repositories or clones from remote sources when needed:

```mermaid
flowchart TD
Start["Repository Operation"] --> CheckExists{"Repository Exists?"}
CheckExists --> |No| CloneRepo["Clone from RemoteUrl"]
CheckExists --> |Yes| PullChanges["Pull Latest Changes"]
CloneRepo --> SetBranch["Set Branch Configuration"]
SetBranch --> ConfigureAuth["Configure Authentication"]
ConfigureAuth --> Complete["Clone Complete"]
PullChanges --> FetchRemote["Fetch Remote Changes"]
FetchRemote --> CompareBranches["Compare Local/Remote"]
CompareBranches --> HasChanges{"Changes Detected?"}
HasChanges --> |Yes| MergeChanges["Merge Remote Changes"]
HasChanges --> |No| Complete
MergeChanges --> Complete
```

**Diagram sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L83-L108)

#### Push Operation Workflow

The push operation includes staging, committing, and pushing with conflict detection:

```mermaid
sequenceDiagram
participant Service as "BackupViaGitService"
participant Repo as "Git Repository"
participant Remote as "Remote Repository"
participant DBWatcher as "Database Watcher"
Service->>Repo : Checkout PushRefSpec
Service->>DBWatcher : SetEnable(false)
Service->>Repo : Stage("*")
Repo-->>Service : Staging complete
alt Repository has changes
Service->>Repo : Create commit with message
Repo-->>Service : Commit created
Service->>Remote : Push with credentials
Remote-->>Service : Push successful
Service->>DBWatcher : SetEnable(true)
else No changes detected
Service->>DBWatcher : SetEnable(true)
end
```

**Diagram sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L129-L203)

#### Pull Operation with Conflict Resolution

The pull operation handles merge conflicts gracefully with stashing:

```mermaid
flowchart TD
StartPull["Start Pull Operation"] --> FetchChanges["Fetch Remote Changes"]
FetchChanges --> CompareCommits["Compare Local/Remote Commits"]
CompareCommits --> HasConflict{"SHA Mismatch?"}
HasConflict --> |No| Complete["Pull Complete"]
HasConflict --> |Yes| CreateStash["Create Stash"]
CreateStash --> CheckoutBranch["Checkout Target Branch"]
CheckoutBranch --> MergeRemote["Merge Remote Changes"]
MergeRemote --> MergeSuccess{"Merge Successful?"}
MergeSuccess --> |Yes| ApplyStash["Apply Stash"]
MergeSuccess --> |No| ShowConflicts["Show Conflict Warning"]
ApplyStash --> HasConflicts{"Conflicts Present?"}
HasConflicts --> |Yes| ShowConflicts
HasConflicts --> |No| RemoveStash["Remove Stash"]
ShowConflicts --> Complete
RemoveStash --> Complete
```

**Diagram sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L205-L316)

**Section sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L15-L356)
- [GitService.cs](file://src/Unlimotion.TelegramBot/GitService.cs#L10-L109)

## Configuration Patterns

### GitHub Repository Configuration

For GitHub repositories, configure with HTTPS authentication:

```json
{
  "Git": {
    "BackupEnabled": true,
    "RemoteUrl": "https://github.com/username/repository.git",
    "Branch": "main",
    "UserName": "your-github-email@example.com",
    "Password": "ghp_your-personal-access-token",
    "PullIntervalSeconds": 60,
    "PushIntervalSeconds": 120
  }
}
```

### GitLab Repository Configuration

For GitLab instances, use either HTTPS or SSH:

```json
{
  "Git": {
    "BackupEnabled": true,
    "RemoteUrl": "https://gitlab.com/username/repository.git",
    "Branch": "master",
    "UserName": "gitlab-username",
    "Password": "glpat-your-personal-access-token",
    "PullIntervalSeconds": 30,
    "PushIntervalSeconds": 60
  }
}
```

### Self-Hosted Repository Configuration

For self-hosted Git servers:

```json
{
  "Git": {
    "BackupEnabled": true,
    "RemoteUrl": "https://selfhosted-git.company.com/git/repo.git",
    "Branch": "develop",
    "UserName": "domain\\username",
    "Password": "your-domain-password",
    "PullIntervalSeconds": 45,
    "PushIntervalSeconds": 90
  }
}
```

### Custom Branch Configuration

Configure custom branches for different environments:

```json
{
  "Git": {
    "Branch": "feature/new-backup-system",
    "PushRefSpec": "refs/heads/feature/new-backup-system",
    "CommitterName": "CI/CD Pipeline",
    "CommitterEmail": "ci-cd@company.com"
  }
}
```

**Section sources**
- [TestSettings.json](file://src/Unlimotion.Test/TestSettings.json#L8-L22)
- [SettingsViewModel.cs](file://src/Unlimotion.ViewModel/SettingsViewModel.cs#L40-L75)

## Authentication Methods

### HTTPS Authentication

The primary authentication method uses username/password combinations:

```mermaid
classDiagram
class CredentialsHandler {
<<delegate>>
+UsernamePasswordCredentials Invoke(url, user, cred)
}
class UsernamePasswordCredentials {
+string Username
+string Password
}
CredentialsHandler --> UsernamePasswordCredentials : "returns"
```

**Diagram sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L68-L75)

#### Personal Access Tokens (PAT)

Recommended for enhanced security:

- **GitHub**: Generate PAT with repository access permissions
- **GitLab**: Create Personal Access Token with API scope
- **Azure DevOps**: Generate Personal Access Token with Code.ReadWrite scope

#### Username/Password Authentication

For domain-joined environments:

- Use domain\\username format
- Password field accepts domain credentials
- Supports Windows Integrated Authentication scenarios

### SSH Authentication

While not directly supported in the current implementation, SSH keys can be configured through system-level Git configuration:

```bash
# Add SSH key to system Git
ssh-add ~/.ssh/id_rsa

# Configure Git to use SSH transport
git config --global url."git@github.com:".insteadOf "https://github.com/"
```

**Section sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L68-L75)
- [GitService.cs](file://src/Unlimotion.TelegramBot/GitService.cs#L15-L22)

## Repository Management

### Repository Path Management

The system supports both relative and absolute repository paths:

```mermaid
flowchart TD
PathInput["Repository Path"] --> CheckAbsolute{"Is Absolute Path?"}
CheckAbsolute --> |Yes| ValidatePath["Validate Path Exists"]
CheckAbsolute --> |No| ResolveRelative["Resolve Relative Path"]
ResolveRelative --> GetAbsolutePath["GetAbsolutePath Function"]
GetAbsolutePath --> ValidatePath
ValidatePath --> PathValid{"Path Valid?"}
PathValid --> |Yes| UsePath["Use Repository Path"]
PathValid --> |No| ThrowError["Throw Exception"]
UsePath --> Complete["Path Ready"]
ThrowError --> Complete
```

**Diagram sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L317-L332)

### Remote Repository Discovery

The system provides methods to discover available remotes and references:

```mermaid
sequenceDiagram
participant UI as "User Interface"
participant Service as "BackupViaGitService"
participant Repo as "Git Repository"
UI->>Service : Remotes()
Service->>Repo : Network.Remotes
Repo-->>Service : Remote collection
Service->>Service : Filter remote names
Service-->>UI : List<string> remoteNames
UI->>Service : Refs()
Service->>Repo : Repository.Refs
Repo-->>Service : Reference collection
Service->>Service : Filter refs/heads/*
Service-->>UI : List<string> branchRefs
```

**Diagram sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L25-L60)

**Section sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L25-L60)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L317-L332)

## User Feedback System

The system provides comprehensive user feedback through toast notifications and logging mechanisms.

### Notification Architecture

```mermaid
classDiagram
class INotificationManagerWrapper {
<<interface>>
+void Ask(string header, string message, Action yesAction, Action noAction)
+void ErrorToast(string message)
+void SuccessToast(string message)
}
class NotificationManagerWrapper {
+void Ask(string header, string message, Action yesAction, Action noAction)
+void ErrorToast(string message)
+void SuccessToast(string message)
}
class NotificationMessageManager {
+INotificationMessage CreateMessage()
}
INotificationManagerWrapper <|.. NotificationManagerWrapper
NotificationManagerWrapper --> NotificationMessageManager : "uses"
```

**Diagram sources**
- [NotificationManagerWrapper.cs](file://src/Unlimotion/NotificationManagerWrapper.cs#L8-L73)

### Status Feedback Configuration

The `ShowStatusToasts` property controls whether user feedback is displayed:

| Setting | Behavior | Use Case |
|---------|----------|----------|
| `true` | Display success/error notifications | Development, testing environments |
| `false` | Silent operation | Production, headless deployments |

### Notification Types

#### Success Notifications
- "Start Git Push"
- "Commit Created"
- "Push Successful"
- "Merge Successful"
- "Stash Applied"

#### Error Notifications
- Authentication failures
- Merge conflicts requiring manual resolution
- Network connectivity issues
- Repository corruption detection

#### Information Notifications
- Repository cloning progress
- Branch checkout operations
- Stash creation/deletion

**Section sources**
- [NotificationManagerWrapper.cs](file://src/Unlimotion/NotificationManagerWrapper.cs#L8-L73)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L334-L356)

## Troubleshooting Guide

### Common Authentication Issues

#### Problem: "Can't push to the remote repository, because username or password is empty"

**Cause**: Missing or invalid authentication credentials

**Solution**:
1. Verify credentials in configuration
2. Ensure `UserName` and `Password` are properly set
3. For PATs, verify token validity and permissions

#### Problem: "Authentication failed"

**Cause**: Incorrect credentials or expired tokens

**Solution**:
1. Regenerate personal access token
2. Verify account permissions
3. Check for two-factor authentication requirements

### Merge Conflict Resolution

#### Problem: "Fix conflicts and then commit the result"

**Cause**: Automatic merge failed due to conflicting changes

**Solution**:
1. Manually resolve conflicts in the repository
2. Commit the resolved changes
3. The system will automatically detect and apply subsequent pushes

#### Problem: "Can't merge remote branch to local branch"

**Cause**: Complex merge conflicts or repository corruption

**Solution**:
1. Check repository integrity
2. Reset to known good state if necessary
3. Reconfigure repository settings

### Network Connectivity Issues

#### Problem: Repository cloning fails

**Symptoms**:
- Timeout errors during clone
- SSL certificate verification failures
- DNS resolution problems

**Solutions**:
1. Verify network connectivity
2. Configure proxy settings if behind corporate firewall
3. Update SSL certificates if using self-signed certificates

#### Problem: Push operations timeout

**Symptoms**:
- Long-running push operations
- Connection reset during push
- Large repository size causing timeouts

**Solutions**:
1. Optimize repository size
2. Increase timeout settings
3. Split large commits into smaller batches

### Configuration Validation

#### Problem: Invalid RemoteUrl format

**Cause**: Malformed repository URL

**Solution**:
- Verify URL format matches repository type
- Ensure protocol specification (https:// or git@)
- Check for trailing slashes or missing extensions (.git)

#### Problem: Branch not found

**Cause**: Specified branch doesn't exist in remote repository

**Solution**:
1. Verify branch name exists in remote repository
2. Create branch if necessary
3. Update configuration with correct branch name

**Section sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L324-L332)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L205-L316)

## Best Practices

### Repository Organization

#### Recommended Repository Structure
```
repository-root/
├── tasks/
│   ├── task-data.json
│   └── metadata.json
├── logs/
│   └── git-operations.log
└── backups/
    ├── daily/
    └── weekly/
```

#### Branch Strategy
- **main**: Production-ready task data
- **develop**: Active development branch
- **feature/***: Feature-specific branches
- **backup/***: Automated backup branches

### Security Best Practices

#### Credential Management
1. **Never hardcode credentials** in source code
2. **Use environment variables** for sensitive data
3. **Rotate access tokens regularly**
4. **Implement least-privilege access** for automation accounts

#### Repository Security
1. **Enable two-factor authentication** on hosting platforms
2. **Restrict repository access** to authorized personnel only
3. **Monitor repository activity** for unauthorized changes
4. **Use protected branches** to prevent accidental deletions

### Performance Optimization

#### Interval Configuration
```json
{
  "Git": {
    "PullIntervalSeconds": 300,  // 5 minutes for production
    "PushIntervalSeconds": 600,  // 10 minutes for production
    "BackupEnabled": true
  }
}
```

#### Repository Maintenance
1. **Regular garbage collection** for large repositories
2. **Pack optimization** for frequently changing data
3. **Branch cleanup** for obsolete feature branches
4. **Log rotation** for operational logs

### Monitoring and Logging

#### Operational Metrics
- Push/pull success rates
- Merge conflict frequency
- Authentication failure rates
- Repository size growth trends

#### Alert Configuration
- Notify on consecutive authentication failures
- Alert on merge conflict escalation
- Monitor repository health indicators
- Track backup completion status

## Security Considerations

### Credential Protection

#### Current Implementation Limitations
The current system stores credentials in plain text within configuration files. This poses security risks for production deployments.

#### Recommended Security Enhancements
1. **Encrypt configuration files** using DPAPI or similar encryption
2. **Implement credential vault integration** for secure storage
3. **Use OAuth flows** instead of storing passwords
4. **Implement credential refresh mechanisms** for long-running sessions

### Access Control

#### Repository Permissions
- **Read access**: Required for pull operations
- **Write access**: Required for push operations
- **Admin access**: Required for repository configuration changes

#### Network Security
- **HTTPS enforcement**: Always use encrypted connections
- **Certificate validation**: Verify SSL certificates
- **Firewall configuration**: Restrict outbound Git traffic
- **VPN requirements**: Enforce secure network access

### Audit and Compliance

#### Operational Auditing
1. **Log all Git operations** with timestamps and user context
2. **Track credential usage** for security monitoring
3. **Monitor repository access patterns** for anomalies
4. **Maintain audit trails** for compliance requirements

#### Data Integrity
1. **Verify commit signatures** when available
2. **Monitor repository corruption** proactively
3. **Implement backup verification** procedures
4. **Validate repository health** regularly

**Section sources**
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L324-L332)
- [TestSettings.json](file://src/Unlimotion.Test/TestSettings.json#L8-L22)