# Web Platform Errors

<cite>
**Referenced Files in This Document**   
- [index.html](file://landing/index.html)
- [index.html](file://src/Unlimotion.Browser/AppBundle/index.html)
- [index.html](file://src/Unlimotion.Browser/wwwroot/index.html)
- [runtimeconfig.template.json](file://src/Unlimotion.Browser/runtimeconfig.template.json)
- [Program.cs](file://src/Unlimotion.Browser/Program.cs)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js)
- [main.js](file://src/Unlimotion.Browser/wwwroot/main.js)
- [Unlimotion.Browser.csproj](file://src/Unlimotion.Browser/Unlimotion.Browser.csproj)
- [AssemblyInfo.cs](file://src/Unlimotion.Browser/Properties/AssemblyInfo.cs)
- [launchSettings.json](file://src/Unlimotion.Browser/Properties/launchSettings.json)
- [app.css](file://src/Unlimotion.Browser/AppBundle/app.css)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [WebAssembly and Browser Compatibility](#webassembly-and-browser-compatibility)
3. [Content Security Policy Configuration](#content-security-policy-configuration)
4. [Service Worker and Offline Functionality](#service-worker-and-offline-functionality)
5. [DOM Element Mounting and CSS Isolation](#dom-element-mounting-and-css-isolation)
6. [WASM Payload Loading and MIME Type Issues](#wasm-payload-loading-and-mime-type-issues)
7. [CORS Policy and API Communication](#cors-policy-and-api-communication)
8. [JavaScript Interoperability](#javascript-interoperability)
9. [Font Loading and Asset Management](#font-loading-and-asset-management)
10. [Browser Developer Tools and Diagnostics](#browser-developer-tools-and-diagnostics)
11. [Performance Profiling and Reactive Updates](#performance-profiling-and-reactive-updates)
12. [Storage Quota Management](#storage-quota-management)
13. [Conclusion](#conclusion)

## Introduction
Unlimotion is a cross-platform application that leverages Avalonia UI to provide a consistent user experience across desktop and web platforms. The web implementation uses WebAssembly (WASM) to run the .NET-based application directly in the browser. This document addresses common web-specific errors and issues that may arise when running Unlimotion in a browser environment, including browser compatibility problems, security policy configurations, service worker registration, and various rendering issues. The analysis focuses on the browser-specific implementation details found in the Unlimotion.Browser project and related configuration files.

## WebAssembly and Browser Compatibility
The Unlimotion web application is built on WebAssembly technology, allowing the .NET codebase to run natively in the browser. The project is configured as a WebAssembly application with the target framework set to net9.0-browser in the Unlimotion.Browser.csproj file. This configuration enables the application to compile to WebAssembly and run in modern browsers that support this technology.

Browser compatibility is explicitly declared in the AssemblyInfo.cs file with the SupportedOSPlatform attribute set to "browser", indicating that the application is designed to run in browser environments. The runtime configuration is managed through the runtimeconfig.template.json file, which specifies browser-specific host properties. This configuration ensures that the WebAssembly runtime is properly initialized when the application loads in the browser.

Potential compatibility issues may arise in browsers that lack full WebAssembly support or have disabled this feature. Users may encounter errors if their browser does not support the required WebAssembly features or if JavaScript execution is restricted. The application checks for browser environment at startup in the main.js files, throwing an error if the window object is not available, which helps identify non-browser execution attempts early in the loading process.

**Section sources**
- [Unlimotion.Browser.csproj](file://src/Unlimotion.Browser/Unlimotion.Browser.csproj#L1-L15)
- [AssemblyInfo.cs](file://src/Unlimotion.Browser/Properties/AssemblyInfo.cs#L0-L0)
- [runtimeconfig.template.json](file://src/Unlimotion.Browser/runtimeconfig.template.json#L1-L9)
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js#L1-L3)
- [main.js](file://src/Unlimotion.Browser/wwwroot/main.js#L1-L3)

## Content Security Policy Configuration
Content Security Policy (CSP) configuration is critical for the proper functioning of the Unlimotion web application. The application requires specific CSP directives to allow the loading and execution of WebAssembly modules, JavaScript files, and external resources. The index.html files in both the AppBundle and wwwroot directories include modulepreload links for essential JavaScript files (dotnet.js, avalonia.js, and main.js), which must be permitted by the CSP.

The application loads external resources such as the Avalonia UI logo and potentially other assets, which require appropriate CSP directives for fetch operations. The main.js files import the dotnet module from relative paths, requiring script-src directives that allow these imports. Additionally, the application may need connect-src directives to allow WebSocket connections for SignalR communication with the server backend.

Improper CSP configuration can result in blocked script execution, preventing the application from loading. The splash screen elements with inline styles may also be affected by strict CSP policies that disallow inline styling. Administrators should ensure that the CSP allows script loading from the application's origin and any required external domains, while maintaining appropriate security boundaries.

**Section sources**
- [index.html](file://src/Unlimotion.Browser/AppBundle/index.html#L7-L11)
- [index.html](file://src/Unlimotion.Browser/wwwroot/index.html#L7-L9)
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js#L1-L2)
- [main.js](file://src/Unlimotion.Browser/wwwroot/main.js#L1-L2)
- [app.css](file://src/Unlimotion.Browser/AppBundle/app.css#L23-L27)

## Service Worker and Offline Functionality
The Unlimotion web application does not appear to implement service workers for offline functionality based on the available codebase. The application relies on direct WebAssembly execution and server communication through SignalR for real-time updates. Without service worker registration, the application cannot function offline or provide progressive web app capabilities.

The absence of service workers means that the application must establish a connection to the server backend each time it is loaded, and users will not be able to access previously loaded content when offline. This design choice prioritizes real-time synchronization over offline capabilities, which may be appropriate for the application's use case but limits its functionality in environments with unreliable network connectivity.

To implement offline functionality, the application would need to register a service worker that caches essential assets and implements a strategy for storing and retrieving application data. This would require significant changes to the current architecture, including local data storage mechanisms and synchronization logic for when the connection is restored.

**Section sources**
- [Program.cs](file://src/Unlimotion.Browser/Program.cs#L1-L52)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs#L1-L233)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L1-L41)

## DOM Element Mounting and CSS Isolation
The Unlimotion web application uses a specific DOM structure for mounting the Avalonia UI framework. The application is mounted within a div element with the id "out" in both index.html files, which serves as the container for the entire UI. The splash screen is initially displayed within this container and is expected to be removed once the application is fully loaded.

CSS isolation is implemented through specific class names and styling in the app.css file. The application uses classes like "avalonia-splash" and "splash-close" to manage the splash screen appearance and transition. The CSS includes animations for fading out the splash screen, which should be triggered by the application once initialization is complete.

Potential issues may arise if the DOM structure is modified or if conflicting CSS rules are applied by the hosting environment. The application relies on specific element IDs and classes for proper rendering, and changes to these could result in display problems or functionality issues. The CSS uses environment variables for safe area insets, which helps with mobile browser compatibility but requires proper browser support for these features.

**Section sources**
- [index.html](file://src/Unlimotion.Browser/AppBundle/index.html#L15-L25)
- [index.html](file://src/Unlimotion.Browser/wwwroot/index.html#L13-L21)
- [app.css](file://src/Unlimotion.Browser/AppBundle/app.css#L23-L74)
- [Program.cs](file://src/Unlimotion.Browser/Program.cs#L10-L11)

## WASM Payload Loading and MIME Type Issues
The WebAssembly payload loading process is managed through the main.js files in the AppBundle and wwwroot directories. These files import the dotnet module and initialize the WebAssembly runtime with diagnostic tracing disabled and application arguments obtained from the query string. The runtime configuration is retrieved and used to start the main application assembly.

MIME type issues may occur if the server hosting the application does not properly configure the MIME types for WebAssembly and related files. The application requires correct MIME types for .wasm files (application/wasm), JavaScript files (text/javascript), and CSS files (text/css). Incorrect MIME type configuration can prevent the browser from properly interpreting and executing these resources.

The application uses modulepreload links in the index.html files to optimize the loading of critical JavaScript files. This can improve startup performance but requires proper server configuration to support HTTP/2 and efficient resource loading. Issues with payload loading may manifest as stalled application startup or runtime errors during the initialization phase.

**Section sources**
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js#L1-L12)
- [main.js](file://src/Unlimotion.Browser/wwwroot/main.js#L1-L13)
- [index.html](file://src/Unlimotion.Browser/AppBundle/index.html#L8-L11)
- [index.html](file://src/Unlimotion.Browser/wwwroot/index.html#L7-L9)

## CORS Policy and API Communication
The Unlimotion web application communicates with a server backend using SignalR for real-time updates and ServiceStack for API requests. The server-side AppHost.cs file includes a CorsFeature that allows specific headers (Content-Type, Authorization, x-client-version) in cross-origin requests. This configuration enables the browser application to communicate with the server API.

CORS policy violations may occur if the server is hosted on a different domain or port than the web application, or if additional headers are required that are not included in the allowed headers list. The application may also encounter issues if the server requires secure connections (HTTPS) but is accessed over HTTP, as indicated by the RequireSecureConnection setting in the server configuration.

The ServerTaskStorage class handles the connection to the server backend and includes error handling for connection issues. However, CORS-related errors typically occur at the browser level before reaching the application code, making them difficult to handle programmatically. Proper server configuration and consistent protocol usage (HTTP vs HTTPS) are essential for avoiding CORS issues.

**Section sources**
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs#L69-L78)
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs#L80-L102)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L170-L208)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L357-L392)

## JavaScript Interoperability
JavaScript interoperation is a critical aspect of the Unlimotion web application, enabling communication between the WebAssembly runtime and the browser environment. The main.js files serve as the bridge between JavaScript and .NET code, initializing the WebAssembly runtime and starting the application. The dotnet module is imported from dotnet.js, which provides the interop layer between JavaScript and .NET.

The application uses JavaScript interop to access browser features such as the location object for obtaining URL parameters and navigation. The main.js files pass the current URL to the .NET application, allowing it to process query parameters and route accordingly. This interop is essential for the application's functionality but can fail if JavaScript execution is restricted or if the expected objects are not available.

Potential interop failures may occur due to ad blockers, strict content security policies, or browser extensions that interfere with JavaScript execution. The application includes a check for the window object to verify it is running in a browser environment, which helps identify execution context issues early in the startup process.

**Section sources**
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js#L1-L12)
- [main.js](file://src/Unlimotion.Browser/wwwroot/main.js#L1-L13)
- [Program.cs](file://src/Unlimotion.Browser/Program.cs#L1-L52)

## Font Loading and Asset Management
The Unlimotion web application references external fonts and assets in its implementation. The wwwroot/index.html file includes an SVG logo for Avalonia UI, which is loaded as an inline SVG element. The application also references external images in the landing page's index.html file, such as the Unlimotion favicon.

Font loading issues may occur if the browser blocks external resource loading or if the required fonts are not available. The application relies on system fonts and Nunito (referenced in app.css) for its typography. Missing font resources could result in fallback fonts being used, potentially affecting the application's appearance and layout.

Asset management is handled through relative paths in the index.html and CSS files. The application expects certain assets (like Logo.svg) to be available in specific locations relative to the HTML files. If these assets are missing or incorrectly deployed, the application may display broken images or incorrect styling.

**Section sources**
- [index.html](file://src/Unlimotion.Browser/wwwroot/index.html#L13-L21)
- [app.css](file://src/Unlimotion.Browser/AppBundle/app.css#L23-L27)
- [index.html](file://landing/index.html#L12-L13)

## Browser Developer Tools and Diagnostics
The Unlimotion web application provides several mechanisms for diagnostics using browser developer tools. The launchSettings.json file includes an inspectUri configuration that enables debugging through the browser's developer tools, allowing inspection of the WebAssembly runtime and application state.

The main.js files include diagnostic tracing options that can be enabled for troubleshooting. The application also uses console logging for error reporting, as seen in the Unhandled Exception handler in AppHost.cs. These diagnostic capabilities allow developers to inspect network requests, JavaScript execution, and application errors.

When troubleshooting issues, developers should examine the browser's console for JavaScript errors, the network tab for failed resource loading, and the application tab for storage usage. The WebAssembly runtime may generate specific errors related to memory allocation, module loading, or interop failures that can be identified through these tools.

**Section sources**
- [launchSettings.json](file://src/Unlimotion.Browser/Properties/launchSettings.json#L1-L14)
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js#L5-L7)
- [main.js](file://src/Unlimotion.Browser/wwwroot/main.js#L5-L7)
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs#L42-L67)

## Performance Profiling and Reactive Updates
The Unlimotion application uses ReactiveUI for managing reactive updates and data binding. This framework enables efficient UI updates in response to data changes, but improper usage can lead to performance issues. The application should be profiled to ensure that reactive subscriptions are properly managed and disposed to prevent memory leaks.

Performance profiling should focus on the initialization sequence, as WebAssembly applications typically have longer startup times than traditional web applications. The modulepreload directives in the index.html files help optimize resource loading, but the overall payload size and initialization sequence should be monitored.

The application's use of SignalR for real-time updates requires careful management of connection state and message processing to avoid overwhelming the client with updates. The ServerTaskStorage class includes connection management logic that should be evaluated for efficiency and responsiveness under various network conditions.

**Section sources**
- [Program.cs](file://src/Unlimotion.Browser/Program.cs#L1-L52)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs#L1-L233)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L1-L41)

## Storage Quota Management
The Unlimotion web application may encounter storage quota limitations in the browser environment. The application stores settings in a JSON configuration file located in the ApplicationData folder, which is mapped to browser storage. Different browsers have varying storage limits and eviction policies that can affect the application's ability to persist data.

The application does not appear to implement specific storage quota management strategies, which could lead to issues when approaching browser storage limits. Users may experience data loss or application errors if the storage quota is exceeded. The application should implement error handling for storage operations and provide feedback to users when storage limits are approached.

IndexedDB and localStorage usage should be monitored, as these are subject to browser-specific quotas and may be cleared by user actions or browser cleanup processes. The application should be designed to handle storage errors gracefully and provide mechanisms for data backup and recovery.

**Section sources**
- [Program.cs](file://src/Unlimotion.Browser/Program.cs#L15-L25)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs#L1-L233)

## Conclusion
The Unlimotion web application implements a WebAssembly-based solution using Avalonia UI to provide a cross-platform experience. While this approach offers significant benefits in code reuse and consistency across platforms, it introduces specific challenges related to browser compatibility, security policies, and performance. Understanding these web-specific issues is essential for deploying and maintaining the application in various browser environments. Proper configuration of Content Security Policies, CORS settings, and server hosting parameters is critical for ensuring reliable operation. Additionally, monitoring performance and storage usage will help maintain a positive user experience as the application evolves.