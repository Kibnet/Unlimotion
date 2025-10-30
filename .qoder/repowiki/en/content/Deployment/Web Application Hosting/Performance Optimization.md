# Performance Optimization

<cite>
**Referenced Files in This Document**   
- [Program.cs](file://src/Unlimotion.Browser/Program.cs)
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js)
- [index.html](file://src/Unlimotion.Browser/AppBundle/index.html)
- [Unlimotion.Browser.csproj](file://src/Unlimotion.Browser/Unlimotion.Browser.csproj)
- [runtimeconfig.template.json](file://src/Unlimotion.Browser/runtimeconfig.template.json)
- [Program.cs](file://src/Unlimotion.Server/Program.cs)
- [Startup.cs](file://src/Unlimotion.Server/Startup.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [WebAssembly Payload Size Reduction](#webassembly-payload-size-reduction)
3. [Lazy Loading Strategies](#lazy-loading-strategies)
4. [Service Worker Caching](#service-worker-caching)
5. [Program.cs Configuration Impact](#programcs-configuration-impact)
6. [main.js Bootstrap Optimization](#mainjs-bootstrap-optimization)
7. [Brotli Compression Implementation](#brotli-compression-implementation)
8. [HTTP/2 and Browser Caching](#http2-and-browser-caching)
9. [Code Splitting and Resource Preloading](#code-splitting-and-resource-preloading)
10. [Performance Monitoring](#performance-monitoring)
11. [Optimization Benchmarks](#optimization-benchmarks)

## Introduction
This document provides comprehensive performance optimization guidance for the Unlimotion web application, a Blazor WebAssembly application built with Avalonia UI framework. The application leverages .NET 9.0 for browser deployment, utilizing WebAssembly for client-side execution. The optimization strategies focus on critical performance areas including WebAssembly payload size reduction, efficient loading strategies, caching mechanisms, and server configuration optimizations. The documentation covers both client-side and server-side optimizations to ensure optimal user experience across initial load, time to interactive, and subsequent visit performance metrics.

## WebAssembly Payload Size Reduction
The Unlimotion application's WebAssembly payload size can be optimized through several configuration settings in the project file and runtime configuration. The application is configured with .NET 9.0 targeting the browser runtime, as specified in the Unlimotion.Browser.csproj file. Key optimization opportunities include IL trimming, compression settings, and selective assembly loading.

The runtime configuration in runtimeconfig.template.json specifies browser-specific hosting properties that can impact payload size and loading behavior. By default, the application loads the complete .NET runtime and all referenced assemblies, which contributes to the initial payload size. Implementing IL trimming through project configuration can significantly reduce the final bundle size by removing unused code from the final WebAssembly output.

Additional optimization can be achieved by configuring the linker behavior in the project file to remove unused assemblies and types. The current configuration does not specify aggressive trimming, leaving room for optimization by adding linker descriptors or configuration to eliminate unused code paths. This is particularly important for the Avalonia UI framework components, which may include functionality not utilized by the specific application features.

**Section sources**
- [Unlimotion.Browser.csproj](file://src/Unlimotion.Browser/Unlimotion.Browser.csproj#L1-L16)
- [runtimeconfig.template.json](file://src/Unlimotion.Browser/runtimeconfig.template.json#L1-L9)

## Lazy Loading Strategies
The Unlimotion application can implement lazy loading strategies to improve initial load performance by deferring the loading of non-critical components and assemblies. Currently, the application loads all dependencies synchronously during the bootstrap process, as evidenced by the main.js file which imports dotnet.js and immediately creates the runtime instance.

Optimization opportunities exist in implementing dynamic import patterns for non-essential functionality, such as feature modules that are not required for the initial application state. The application could be restructured to load core functionality first, then asynchronously load additional modules based on user navigation or interaction patterns.

The Program.cs file in the browser project contains initialization logic that could be optimized for lazy loading, particularly around service registration and configuration loading. By deferring the registration of non-essential services until they are needed, the application can reduce the initial processing overhead during startup.

**Section sources**
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js#L1-L12)
- [Program.cs](file://src/Unlimotion.Browser/Program.cs#L1-L51)

## Service Worker Caching
The Unlimotion application can leverage service workers for efficient caching of static assets and API responses to improve subsequent visit performance. While the current implementation does not explicitly show service worker registration, the application structure supports Progressive Web App capabilities that can be enhanced with service worker implementation.

Service worker caching strategies should focus on caching static assets such as CSS, JavaScript, and images with long expiration times, while implementing cache-first or stale-while-revalidate strategies for API responses. This approach ensures fast loading on subsequent visits while maintaining data freshness.

The application's static assets, including the WebAssembly runtime and application assemblies, are ideal candidates for service worker caching. By implementing proper cache versioning and update strategies, the application can balance between fast loading and ensuring users receive updated content when changes are deployed.

**Section sources**
- [index.html](file://src/Unlimotion.Browser/AppBundle/index.html#L1-L29)

## Program.cs Configuration Impact
The Program.cs file in the Unlimotion.Browser project significantly impacts startup performance through its service configuration and initialization sequence. The current implementation synchronously configures multiple services during application startup, including AutoMapper, configuration providers, and dependency injection registrations.

The initialization sequence in Program.cs performs several operations that affect startup time:
- Creation of the application data directory path
- Configuration file initialization and loading
- AutoMapper configuration
- Service registration in the dependency container
- Task storage registration based on configuration

Each of these operations adds to the startup time, particularly the file system operations and configuration loading. Optimizing this sequence by deferring non-essential service registrations or implementing asynchronous initialization patterns could improve startup performance.

The use of WritableJsonConfigurationFabric to create the configuration object involves file system access that could be optimized through caching or by reducing the number of configuration reads during startup.

**Section sources**
- [Program.cs](file://src/Unlimotion.Browser/Program.cs#L1-L51)

## main.js Bootstrap Optimization
The main.js file serves as the entry point for the WebAssembly application and contains critical bootstrap logic that directly impacts initial load performance. The current implementation follows a standard Blazor WebAssembly bootstrap pattern but has several optimization opportunities.

The bootstrap process in main.js can be optimized by:
- Implementing diagnostic tracing selectively rather than disabling it globally
- Optimizing the application arguments parsing
- Reducing the number of synchronous operations during runtime creation
- Implementing progressive loading of the runtime components

The current implementation uses `withDiagnosticTracing(false)` which disables diagnostic features but doesn't optimize the loading sequence. A more sophisticated approach would conditionally enable diagnostics based on environment or user preferences.

The `runMainAndExit` method call in the AppBundle version of main.js terminates the application after execution, which may not be optimal for single-page applications that need to maintain state. This pattern should be evaluated against the application's usage scenarios.

**Section sources**
- [main.js](file://src/Unlimotion.Browser/AppBundle/main.js#L1-L12)
- [main.js](file://src/Unlimotion.Browser/wwwroot/main.js#L1-L13)

## Brotli Compression Implementation
Implementing Brotli compression for static assets can significantly reduce the payload size transferred to clients, improving load times especially on slower connections. The Unlimotion application would benefit from Brotli compression of its WebAssembly assemblies, JavaScript files, CSS, and other static resources.

The server configuration in Program.cs and Startup.cs should be enhanced to include Brotli compression middleware. Currently, the server implementation uses default ASP.NET Core hosting without explicit compression configuration. Adding the ResponseCompression service with Brotli support would enable compression of responses based on client capabilities.

Compression should be applied to:
- WebAssembly binary files (.wasm)
- JavaScript files (.js)
- CSS files (.css)
- JSON responses
- HTML documents

The compression level should be optimized for the balance between CPU usage and compression ratio, typically level 4-6 for production environments. Static compression during the build process could also be implemented to reduce server processing overhead.

**Section sources**
- [Program.cs](file://src/Unlimotion.Server/Program.cs#L1-L49)
- [Startup.cs](file://src/Unlimotion.Server/Startup.cs#L1-L62)

## HTTP/2 and Browser Caching
Enabling HTTP/2 and proper browser caching headers can dramatically improve the Unlimotion application's performance by reducing connection overhead and leveraging client-side caching. The current server configuration does not explicitly enable HTTP/2, relying on default ASP.NET Core behavior.

HTTP/2 benefits for the application include:
- Multiplexing of requests over a single connection
- Header compression
- Server push capabilities
- Improved prioritization of resources

Browser caching should be implemented with appropriate cache headers for different asset types:
- Static assets (JS, CSS, images): Long cache durations with content hashing
- API responses: Shorter cache durations with validation tokens
- HTML documents: No caching or very short cache durations

The Startup.cs file should be modified to include response caching middleware and configure cache profiles for different content types. The UseHsts() call in production environments indicates awareness of security headers but doesn't address performance-related headers.

**Section sources**
- [Startup.cs](file://src/Unlimotion.Server/Startup.cs#L1-L62)
- [Program.cs](file://src/Unlimotion.Server/Program.cs#L1-L49)

## Code Splitting and Resource Preloading
Code splitting and resource preloading strategies can optimize the loading sequence and reduce initial payload size. The current implementation loads all application code upfront, but the modular structure of the application suggests opportunities for code splitting.

The index.html file already includes modulepreload links for critical JavaScript files, which is a good practice for reducing render-blocking resources. This pattern should be extended to include other critical resources needed for the initial render.

Code splitting opportunities include:
- Separating UI framework code from application logic
- Splitting feature modules that are not required for initial load
- Creating shared libraries for commonly used functionality
- Implementing dynamic imports for less frequently used features

Resource hints such as preload, prefetch, and preconnect should be strategically used to prioritize critical resources while avoiding unnecessary bandwidth usage. The current preload configuration focuses on JavaScript modules but could be expanded to include critical CSS and fonts.

**Section sources**
- [index.html](file://src/Unlimotion.Browser/AppBundle/index.html#L1-L29)

## Performance Monitoring
Monitoring load performance using browser developer tools is essential for identifying bottlenecks and measuring optimization effectiveness. Key metrics to monitor include:

- **Initial load time**: Time from navigation to first meaningful paint
- **Time to interactive**: Time until the application is fully responsive
- **Total blocking time**: Sum of all time periods between FCP and Time to Interactive, when task length exceeded 50ms
- **Speed Index**: How quickly content is visually displayed
- **First Contentful Paint (FCP)**: When the first text or image is rendered
- **Largest Contentful Paint (LCP)**: When the largest content element becomes visible

Browser developer tools should be used to:
- Analyze network waterfall charts to identify slow resources
- Examine JavaScript execution time and call stacks
- Monitor memory usage and garbage collection patterns
- Audit for performance best practices
- Simulate different network conditions and device capabilities

The application should implement performance monitoring APIs to collect these metrics in production and identify areas for further optimization.

## Optimization Benchmarks
Establishing clear optimization targets provides measurable goals for performance improvements. Recommended benchmarks for the Unlimotion application:

### Initial Load Performance Targets
- **First Contentful Paint**: < 1.8 seconds on 4G networks
- **Largest Contentful Paint**: < 2.5 seconds on 4G networks
- **Time to Interactive**: < 3.5 seconds on 4G networks
- **Total Page Weight**: < 2MB (compressed) for initial load
- **Number of HTTP Requests**: < 25 for initial load

### Subsequent Visit Performance Targets
- **Cache Hit Rate**: > 95% for static assets
- **Repeat Visit Load Time**: < 1 second (with service worker)
- **Time to Interactive**: < 1.5 seconds (with service worker)

### WebAssembly-Specific Targets
- **Runtime Download Size**: < 1.5MB (Brotli compressed)
- **Runtime Initialization Time**: < 800ms on mid-range devices
- **Assembly Loading Time**: < 500ms for all assemblies

These benchmarks should be measured using Lighthouse, WebPageTest, or similar tools under simulated 4G network conditions (400ms RTT, 1.6Mbps down, 0.7Mbps up) to ensure realistic performance assessment.