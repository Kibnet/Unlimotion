# Windows Deployment

<cite>
**Referenced Files in This Document**   
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [Program.cs](file://src/Unlimotion.Desktop/Program.cs)
- [app.manifest](file://src/Unlimotion.Desktop/app.manifest)
- [Unlimotion.Desktop.ForMacBuild.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj)
- [generate-deb-pkg.sh](file://src/Unlimotion.Desktop/ci/deb/generate-deb-pkg.sh)
- [generate-osx-publish.sh](file://src/Unlimotion.Desktop/ci/osx/generate-osx-publish.sh)
- [publish.bat](file://src/Unlimotion.Server/publish.bat)
- [run.windows.cmd](file://run.windows.cmd)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
4. [Architecture Overview](#architecture-overview)
5. [Detailed Component Analysis](#detailed-component-analysis)
6. [Dependency Analysis](#dependency-analysis)
7. [Performance Considerations](#performance-considerations)
8. [Troubleshooting Guide](#troubleshooting-guide)
9. [Conclusion](#conclusion)

## Introduction
This document provides comprehensive guidance on deploying the Unlimotion desktop application on Windows systems. It covers the configuration and build process controlled by the Unlimotion.Desktop.csproj file, including target framework specifications, runtime identifiers, and publishing settings. The documentation details both framework-dependent and self-contained deployment models, executable generation, distribution methods, and system integration features.

## Project Structure
The Unlimotion project follows a multi-platform architecture with dedicated project files for different operating systems. The Windows desktop application is primarily configured through the Unlimotion.Desktop.csproj file, while platform-specific build configurations are managed through supplementary project files and scripts.

```mermaid
graph TD
A[Unlimotion.Desktop] --> B[Unlimotion.Desktop.csproj]
A --> C[Unlimotion.Desktop.ForMacBuild.csproj]
A --> D[Unlimotion.Desktop.ForDebianBuild.csproj]
A --> E[ci/deb]
A --> F[ci/osx]
B --> G[TargetFramework: net9.0]
B --> H[OutputType: WinExe]
B --> I[ApplicationIcon]
B --> J[app.manifest]
E --> K[generate-deb-pkg.sh]
F --> L[generate-osx-publish.sh]
```

**Diagram sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [Unlimotion.Desktop.ForMacBuild.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj)
- [generate-deb-pkg.sh](file://src/Unlimotion.Desktop/ci/deb/generate-deb-pkg.sh)
- [generate-osx-publish.sh](file://src/Unlimotion.Desktop/ci/osx/generate-osx-publish.sh)

**Section sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [Unlimotion.Desktop.ForMacBuild.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj)

## Core Components
The Windows deployment of Unlimotion is controlled by several key components that work together to produce a functional desktop application. The primary configuration is managed through the Unlimotion.Desktop.csproj file, which specifies the target framework, output type, and application resources.

The deployment process is further customized through platform-specific build scripts and manifest files that ensure proper application behavior on Windows systems. The application's entry point in Program.cs contains logic for configuration file handling and storage path initialization based on the execution environment.

**Section sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [Program.cs](file://src/Unlimotion.Desktop/Program.cs)

## Architecture Overview
The Unlimotion desktop application follows a cross-platform architecture built on the .NET framework with Avalonia UI for the user interface. The Windows deployment configuration is designed to produce a native Windows executable that can run in both framework-dependent and self-contained modes.

```mermaid
graph TB
subgraph "Build Configuration"
A[Unlimotion.Desktop.csproj]
B[TargetFramework: net9.0]
C[RuntimeIdentifier: win-x64]
D[PublishSingleFile: true]
E[PublishTrimmed: true]
end
subgraph "Deployment Output"
F[Single Executable]
G[Application Icon]
H[Manifest File]
I[Configuration Files]
end
subgraph "Distribution"
J[ZIP Archive]
K[Installer Package]
end
A --> B
A --> C
A --> D
A --> E
B --> F
C --> F
D --> F
E --> F
F --> J
F --> K
G --> F
H --> F
I --> F
```

**Diagram sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [app.manifest](file://src/Unlimotion.Desktop/app.manifest)

## Detailed Component Analysis

### Build and Publish Configuration
The Unlimotion.Desktop.csproj file controls the build and publish behavior for Windows deployments through several key properties. The TargetFramework is set to net9.0, indicating the application targets .NET 9.0, providing access to the latest framework features and performance improvements.

The project configuration supports both framework-dependent and self-contained deployment models. Framework-dependent deployments require the target system to have the appropriate .NET runtime installed, resulting in smaller application size but additional installation requirements. Self-contained deployments include the entire runtime, creating a larger package but ensuring the application can run on any Windows system without prerequisite installations.

```mermaid
flowchart TD
A[Build Process] --> B{Deployment Model}
B --> C[Framework-Dependent]
B --> D[Self-Contained]
C --> E[Smaller Size]
C --> F[Requires .NET Runtime]
C --> G[Faster Build]
D --> H[Larger Size]
D --> I[No Runtime Dependency]
D --> J[Slower Build]
E --> K[Recommended for Enterprise]
F --> K
G --> K
H --> L[Recommended for General Distribution]
I --> L
J --> L
```

**Diagram sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)

**Section sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)

### Single File Deployment and IL Trimming
The project is configured to generate a single executable file through the PublishSingleFile setting, which bundles all application dependencies into a single .exe file. This simplifies distribution and installation for end users. Additionally, IL trimming is employed to reduce the application size by removing unused code from the final build.

The app.manifest file included in the project ensures proper Windows compatibility by specifying supported operating systems and enabling visual styles. This manifest helps prevent compatibility issues and ensures the application integrates correctly with the Windows user interface.

```mermaid
flowchart TD
A[Source Code] --> B[Compilation]
B --> C{Publish Settings}
C --> D[PublishSingleFile=true]
C --> E[PublishTrimmed=true]
D --> F[Single Executable Output]
E --> G[IL Trimming Process]
G --> H[Removed Unused Code]
G --> I[Reduced Application Size]
F --> J[Unlimotion.exe]
H --> J
I --> J
J --> K[Final Deployment Package]
```

**Diagram sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [app.manifest](file://src/Unlimotion.Desktop/app.manifest)

### Distribution Methods
The Unlimotion application can be distributed through multiple channels. The primary method is through a single executable file that can be packaged in a ZIP archive for simple distribution. For more sophisticated installation requirements, installer packages can be created using tools like WiX or InnoSetup, though specific configuration for these tools is not present in the current codebase.

The project includes CI/CD scripts for other platforms (Debian and macOS) that demonstrate the approach for creating platform-specific packages, which could be adapted for Windows installer creation. The generate-deb-pkg.sh script shows how the dotnet-deb tool can be used to create Debian packages, suggesting a similar approach could be implemented for Windows installers.

```mermaid
flowchart TD
A[Single Executable] --> B{Distribution Method}
B --> C[ZIP Archive]
B --> D[Installer Package]
C --> E[Simple Extraction]
C --> F[No Installation Required]
D --> G[Windows Installer]
D --> H[Registry Entries]
D --> I[Start Menu Integration]
E --> J[Portable Usage]
F --> J
G --> K[Traditional Installation]
H --> K
I --> K
```

**Diagram sources**
- [generate-deb-pkg.sh](file://src/Unlimotion.Desktop/ci/deb/generate-deb-pkg.sh)
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)

### System Integration Features
The application includes configuration for system integration features such as file associations, Start Menu shortcuts, and auto-updater integration. While specific implementation details for these features are not fully visible in the provided code, the presence of the app.manifest file and platform-specific build configurations suggests these capabilities are planned or partially implemented.

The Program.cs file contains logic for determining the application's configuration path and storage location, with different behavior for debug and release builds. In release mode, the application creates a dedicated folder in the user's personal directory for storing settings and task data, following Windows conventions for application data storage.

```mermaid
sequenceDiagram
participant User as "User"
participant App as "Unlimotion"
participant System as "Windows OS"
User->>App : Launch Application
App->>System : Check Configuration Path
alt Debug Mode
System-->>App : Use Local Settings.json
else Release Mode
App->>System : Get Personal Folder Path
System-->>App : Return Documents Directory
App->>System : Create Unlimotion Folder
System-->>App : Confirmation
end
App->>System : Load Configuration
App->>System : Initialize Storage Path
App-->>User : Display Main Window
```

**Diagram sources**
- [Program.cs](file://src/Unlimotion.Desktop/Program.cs)
- [app.manifest](file://src/Unlimotion.Desktop/app.manifest)

## Dependency Analysis
The Unlimotion desktop application has a well-defined dependency structure with clear separation between platform-specific and shared components. The primary dependencies are managed through NuGet packages specified in the project file, with Avalonia providing the cross-platform UI framework.

```mermaid
graph TD
A[Unlimotion.Desktop] --> B[Avalonia.Desktop]
A --> C[Avalonia.Diagnostics]
A --> D[Unlimotion Project]
B --> E[.NET Runtime]
C --> E
D --> E
A --> F[Windows OS]
F --> G[Visual Styles]
F --> H[File System]
F --> I[Registry]
G --> A
H --> A
I --> A
```

**Diagram sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [Program.cs](file://src/Unlimotion.Desktop/Program.cs)

**Section sources**
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)

## Performance Considerations
The deployment configuration includes several performance optimizations. IL trimming reduces the application size by removing unused code, which also improves startup time by reducing the amount of code that needs to be loaded. The single-file deployment model simplifies distribution but may impact startup performance slightly due to the need to extract dependencies on first run.

The application's storage strategy follows Windows best practices by using the user's personal directory for data storage, ensuring proper backup and synchronization through Windows features like OneDrive. The configuration also supports command-line arguments for specifying alternative configuration paths, providing flexibility for enterprise deployment scenarios.

## Troubleshooting Guide
When deploying the Unlimotion application on Windows systems, several common issues may arise. These include SmartScreen blocking unsigned executables, missing runtime dependencies for framework-dependent deployments, and permission issues with application data storage.

For self-contained deployments, ensure sufficient disk space is available as the single executable file includes the entire .NET runtime. For framework-dependent deployments, verify the appropriate .NET runtime is installed on the target system. If the application is blocked by Windows SmartScreen, users may need to right-click the executable and select "Properties" to unblock it, or obtain a code-signed version of the application.

```mermaid
flowchart TD
A[Application Issues] --> B{Error Type}
B --> C[SmartScreen Blocking]
B --> D[Missing Runtime]
B --> E[Permission Errors]
B --> F[Configuration Issues]
C --> G[Right-click .exe]
C --> H[Properties > Unblock]
C --> I[Code Signing]
D --> J[Install .NET Runtime]
D --> K[Use Self-Contained Build]
E --> L[Run as Administrator]
E --> M[Check Folder Permissions]
F --> N[Verify Config Path]
F --> O[Check Command Line Args]
```

**Diagram sources**
- [Program.cs](file://src/Unlimotion.Desktop/Program.cs)
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)

**Section sources**
- [Program.cs](file://src/Unlimotion.Desktop/Program.cs)

## Conclusion
The Unlimotion desktop application is configured for effective Windows deployment through its project file settings and supporting infrastructure. The use of .NET 9.0 with single-file publishing and IL trimming creates a balance between application size and deployment simplicity. While the current configuration focuses on the core deployment mechanics, opportunities exist to enhance the Windows user experience through more sophisticated installer packages and deeper system integration features.

For production deployment, code signing is recommended to avoid SmartScreen warnings and establish trust with end users. The existing build infrastructure can be extended to support Windows installer creation and automated code signing as part of a comprehensive release pipeline.