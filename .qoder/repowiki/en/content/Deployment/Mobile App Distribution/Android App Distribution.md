# Android App Distribution

<cite>
**Referenced Files in This Document**   
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj)
- [AndroidManifest.xml](file://src/Unlimotion.Android/Properties/AndroidManifest.xml)
- [MainActivity.cs](file://src/Unlimotion.Android/MainActivity.cs)
- [Unlimotion.csproj](file://src/Unlimotion/Unlimotion.csproj)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Build Process and Configuration](#build-process-and-configuration)
3. [Version Management and Google Play Requirements](#version-management-and-google-play-requirements)
4. [APK and AAB Generation](#apk-and-aab-generation)
5. [Signing Process and Keystore Management](#signing-process-and-keystore-management)
6. [Google Play App Signing](#google-play-app-signing)
7. [Permissions and App Capabilities](#permissions-and-app-capabilities)
8. [Store Listing Preparation](#store-listing-preparation)
9. [Testing and Release Tracks](#testing-and-release-tracks)
10. [Security Best Practices](#security-best-practices)

## Introduction
This document provides comprehensive guidance for distributing the Unlimotion Android application through Google Play. It covers the complete build, configuration, signing, and release process for the Android application built using .NET MAUI with Avalonia UI framework. The documentation details the project configuration, version management, package generation, signing procedures, and deployment workflows necessary for successful Android app distribution.

## Build Process and Configuration

The Unlimotion Android application is built using MSBuild or .NET CLI with the Unlimotion.Android.csproj project file. The build process targets .NET 9.0 for Android platform with the necessary configurations for release builds.

The project configuration in Unlimotion.Android.csproj specifies key build properties:
- TargetFramework: net9.0-android
- SupportedOSPlatformVersion: 21 (Android 5.0 Lollipop)
- OutputType: Exe
- Nullable: enable
- ApplicationId: com.Kibnet.Unlimotion

The build process leverages the Microsoft.NET.Sdk project SDK, which provides the necessary targets and tasks for building Android applications with .NET. The project references Avalonia.Android package for UI rendering and Xamarin.AndroidX.Core.SplashScreen for splash screen functionality.

For release builds, the project should be compiled with appropriate configuration settings. The AndroidEnableProfiledAot property is currently set to false, indicating that Ahead-of-Time compilation is disabled. This can be enabled for improved performance by setting AndroidEnableProfiledAot to true in the release configuration.

The build process can be executed using either MSBuild or .NET CLI commands:
- Using .NET CLI: `dotnet build Unlimotion.sln -c Release -f net9.0-android`
- Using MSBuild: `msbuild Unlimotion.sln /p:Configuration=Release /p:TargetFramework=net9.0-android`

**Section sources**
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj#L1-L28)

## Version Management and Google Play Requirements

Version management for the Unlimotion Android application is handled through properties in the Unlimotion.Android.csproj file, which map to the AndroidManifest.xml attributes required by Google Play.

The project uses two version properties:
- ApplicationVersion: Maps to versionCode in AndroidManifest.xml
- ApplicationDisplayVersion: Maps to versionName in AndroidManifest.xml

Currently, the project is configured with:
- ApplicationVersion: 1
- ApplicationDisplayVersion: 1.0

For Google Play distribution, these values must be properly managed:
- versionCode (ApplicationVersion): An integer value that represents the internal version number. This must be incremented with each release and is used by Google Play to determine version ordering. It should never be decreased or reused.
- versionName (ApplicationDisplayVersion): A string value shown to users in Google Play. This typically follows semantic versioning (e.g., 1.0.0, 1.1.0) and can include marketing version information.

When preparing a new release, increment the ApplicationVersion for each build submitted to Google Play. The ApplicationDisplayVersion can be updated for major and minor releases but doesn't need to change for every build.

The version information is automatically injected into the generated APK or AAB package during the build process, eliminating the need for manual updates to AndroidManifest.xml.

**Section sources**
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj#L10-L13)

## APK and AAB Generation

The Unlimotion Android application can be packaged as either an APK (Android Package) or Android App Bundle (AAB). The packaging format is controlled by the AndroidPackageFormat property in the project file.

Currently, the project is configured to generate APKs:
```xml
<AndroidPackageFormat>apk</AndroidPackageFormat>
```

To generate an Android App Bundle instead, change this property to:
```xml
<AndroidPackageFormat>aab</AndroidPackageFormat>
```

### APK Generation
APKs are the traditional Android package format. They contain all resources and native libraries for all supported device configurations in a single package. To generate an APK:

```bash
dotnet publish -c Release -f net9.0-android /p:AndroidPackageFormat=apk
```

The generated APK will be located in the bin/Release/net9.0-android/publish directory.

### Android App Bundle (AAB) Generation
Android App Bundle is the preferred publishing format for Google Play. It offers several advantages over APKs:

1. **Smaller download sizes**: Google Play generates and serves optimized APKs for each user's device configuration, reducing app size.
2. **Dynamic delivery**: Enables features like on-demand delivery of features and resources.
3. **Simpler management**: Upload a single AAB instead of multiple APKs for different architectures.

To generate an AAB:

```bash
dotnet publish -c Release -f net9.0-android /p:AndroidPackageFormat=aab
```

The AAB format is recommended for Google Play distribution as it provides a better user experience through smaller download sizes and enables advanced delivery features.

**Section sources**
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj#L14-L15)

## Signing Process and Keystore Management

The Unlimotion Android application must be signed with a digital certificate before distribution. This section covers keystore generation, signing configuration, and CI/CD integration.

### Keystore Generation
Generate a signing keystore using keytool:

```bash
keytool -genkey -v -keystore unlimotion-release-key.keystore -alias unlimotion-key -keyalg RSA -keysize 2048 -validity 10000
```

This creates a keystore with:
- Key algorithm: RSA (2048-bit)
- Validity period: 10,000 days (~27 years)
- Alias: unlimotion-key

### Signing Configuration
The signing process is configured in the build system. For .NET Android projects, signing can be configured through MSBuild properties:

```xml
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
  <AndroidKeyStore>true</AndroidKeyStore>
  <AndroidSigningKeyStore>path/to/unlimotion-release-key.keystore</AndroidSigningKeyStore>
  <AndroidSigningKeyAlias>unlimotion-key</AndroidSigningKeyAlias>
  <AndroidSigningKeyPass>your-key-password</AndroidSigningKeyPass>
  <AndroidSigningStorePass>your-store-password</AndroidSigningStorePass>
</PropertyGroup>
```

### CI/CD Integration
For secure credential management in CI/CD environments:

1. Store keystore file and passwords as secure environment variables or secrets
2. Use base64 encoding for the keystore file when storing in environment variables
3. Decode and write the keystore file during the build process

Example GitHub Actions configuration:
```yaml
env:
  KEYSTORE_BASE64: ${{ secrets.KEYSTORE_BASE64 }}
  KEYSTORE_PASSWORD: ${{ secrets.KEYSTORE_PASSWORD }}
  KEY_PASSWORD: ${{ secrets.KEY_PASSWORD }}

steps:
- name: Decode keystore
  run: echo "$KEYSTORE_BASE64" | base64 -d > unlimotion-release-key.keystore
```

Never commit the keystore file or passwords to version control.

**Section sources**
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj)

## Google Play App Signing

Google Play App Signing is a program that allows Google to manage and protect your app's signing key. Enrolling in this program provides several benefits:

### Benefits of Enrollment
1. **Key recovery**: Google securely stores your app signing key, preventing loss of signing privileges
2. **Key rotation**: Ability to rotate your upload key if it's compromised
3. **App integrity**: Google verifies app updates with your app signing key
4. **Security**: Reduced risk of key theft or loss

### Enrollment Process
1. Generate an upload key (different from your original release key)
2. Upload the app bundle signed with the upload key to Google Play Console
3. Google Play generates an app signing key and returns a certificate
4. Confirm enrollment in Google Play Console

### Implications for Key Management
After enrollment:
- Use the upload key for signing app bundles before uploading to Google Play
- Google Play re-signs your app with the app signing key for distribution
- Keep the upload key secure but don't worry about permanent loss (it can be rotated)
- The app signing key is managed by Google and cannot be accessed or downloaded

For existing apps not enrolled in App Signing, you can enroll during a release. However, once enrolled, you cannot opt out.

The Unlimotion application should enroll in Google Play App Signing to benefit from key recovery and rotation capabilities, especially important for long-term app maintenance.

**Section sources**
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj)

## Permissions and App Capabilities

The Unlimotion Android application declares specific permissions in the AndroidManifest.xml file to access required device capabilities.

### Declared Permissions
The current AndroidManifest.xml includes the following permissions:

```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" />
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
```

These permissions enable:
- **INTERNET**: Network access for synchronization and API calls
- **READ_EXTERNAL_STORAGE**: Read access to external storage for file operations
- **WRITE_EXTERNAL_STORAGE**: Write access to external storage for data persistence

### Runtime Permission Handling
The MainActivity.cs implements runtime permission requests for external storage access:

```csharp
if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) != Permission.Granted)
{
    ActivityCompat.RequestPermissions(this, new string[] { Manifest.Permission.WriteExternalStorage }, RequestStorageId);
}
```

The app requests WRITE_EXTERNAL_STORAGE permission at runtime, which implicitly grants READ_EXTERNAL_STORAGE permission as well. This follows Android's runtime permission model introduced in API level 23.

### Storage Access
The application uses external storage for data persistence:
- When permission is granted: Uses external files directory
- When permission is denied: Falls back to internal app directory

This approach ensures the app functions even when external storage access is denied, though with limited storage capacity.

### Background Services
The codebase does not currently declare any background services in AndroidManifest.xml. However, the application uses Quartz.NET for job scheduling, which may require additional permissions for background execution on newer Android versions.

For future implementation of background sync or notifications, consider adding:
- WAKE_LOCK permission for background tasks
- POST_NOTIFICATIONS permission for Android 13+
- SCHEDULE_EXACT_ALARM permission for precise scheduling

**Section sources**
- [AndroidManifest.xml](file://src/Unlimotion.Android/Properties/AndroidManifest.xml#L1-L6)
- [MainActivity.cs](file://src/Unlimotion.Android/MainActivity.cs#L40-L69)

## Store Listing Preparation

Preparing a compelling store listing is crucial for the success of the Unlimotion Android application on Google Play. This section covers the essential elements of store listing preparation.

### Application Metadata
- **Title**: Unlimotion (consistent with the application label in AndroidManifest.xml)
- **Short description**: A concise summary of the app's core functionality (up to 80 characters)
- **Full description**: Detailed explanation of features, benefits, and use cases
- **ApplicationId**: com.Kibnet.Unlimotion (used as the unique package identifier)

### Screenshots and Media
Prepare high-quality screenshots that showcase the app's interface and key features:
1. Main task management interface
2. Task hierarchy and nesting capabilities
3. Roadmap view with task relationships
4. Settings and configuration screens
5. Mobile-specific UI elements

Screenshots should be provided in the following sizes:
- Phones: 320x480 to 1280x1920 pixels
- Seven-inch tablets: 600x960 to 1200x1920 pixels
- Ten-inch tablets: 768x1024 to 1536x2048 pixels

### Feature Graphic
Create a 1024x500 pixel banner that highlights the app's unique selling points. This appears at the top of the Play Store listing.

### Privacy Policy
A privacy policy is required for apps that collect user data. Given that Unlimotion stores task data locally and potentially syncs with remote repositories, a privacy policy should address:
- Data collected (task information, user preferences)
- Data storage (local device storage, optional Git repository)
- Data sharing (none, unless user configures remote sync)
- User rights (access, modification, deletion)

The privacy policy URL should be provided in the Google Play Console and linked from within the app.

### Localization
Consider providing localized store listings for key markets. The app already supports Russian (as evidenced by README.RU.md), so Russian store listing translations would be beneficial.

**Section sources**
- [AndroidManifest.xml](file://src/Unlimotion.Android/Properties/AndroidManifest.xml#L7-L8)
- [README.md](file://README.md)

## Testing and Release Tracks

The Google Play Console provides multiple testing tracks for distributing the Unlimotion application to different groups of users before a full production rollout.

### Internal Testing
- **Purpose**: Quick testing by developers and trusted team members
- **Setup**: Add testers via email addresses
- **Distribution**: Instant access to the latest build
- **Limit**: Up to 100 testers
- **Process**: Upload signed AAB, add testers, share opt-in link

Internal testing is ideal for verifying basic functionality and critical bug fixes before broader distribution.

### Closed Testing
- **Purpose**: Controlled release to a specific group of users
- **Setup**: Create tester groups (e.g., beta testers, early adopters)
- **Distribution**: Testers join via opt-in link
- **Limit**: Up to 2,000 testers per closed test
- **Feedback**: Collect ratings and reviews from testers

Closed testing allows for gathering feedback from a larger group while maintaining control over who can access the app.

### Open Testing
- **Purpose**: Public beta release
- **Setup**: No tester list required
- **Distribution**: Anyone can join via public opt-in link
- **Visibility**: App listing is visible to everyone, marked as "Beta"
- **Feedback**: Open to all participants

Open testing is useful for gathering widespread feedback and identifying edge cases before the production release.

### Production Rollout with Staged Releases
For production releases, use staged rollouts to gradually increase the percentage of users who receive the update:

1. **Initial rollout**: 10% of users
2. **Monitor metrics**: Crash reports, ANR (Application Not Responding) rates
3. **Gradual increase**: 20%, 50%, 100% over several days
4. **Rollback capability**: Quickly revert if critical issues are detected

Staged rollouts minimize the impact of potential bugs and allow for monitoring app performance with real users before full deployment.

The release process should follow this sequence:
1. Internal testing (immediate team)
2. Closed testing (selected beta testers)
3. Open testing (public beta)
4. Production rollout (staged release)

This approach ensures thorough testing and minimizes risk to the user base.

**Section sources**
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj)
- [README.md](file://README.md)

## Security Best Practices

Implementing robust security practices is essential for protecting user data and maintaining trust in the Unlimotion Android application.

### Server URL Management
- Store server URLs in configuration files rather than hardcoding
- Use different endpoints for development, staging, and production
- Implement certificate pinning for API connections to prevent man-in-the-middle attacks
- Support both HTTP and HTTPS, but default to HTTPS in production

### API Keys and Authentication Tokens
- Never hardcode API keys in source code
- Store sensitive credentials in secure storage (Android Keystore System)
- Use environment-specific configuration for different deployment stages
- Implement token refresh mechanisms for JWT authentication
- Set appropriate expiration times for tokens

The codebase shows evidence of JWT authentication implementation in the server components, with access and refresh tokens managed by JwtAuthProvider.

### Data Protection
- Encrypt sensitive data stored locally
- Use Android's EncryptedSharedPreferences for configuration data
- Implement proper session management with token expiration
- Secure backup data when using Git synchronization

### Code Security
- Obfuscate release builds using tools like dotfuscator
- Remove debug logging in release builds
- Validate and sanitize all user inputs
- Implement proper error handling without exposing sensitive information

### Network Security
- Use HTTPS for all network communications
- Implement network security configuration (network_security_config.xml)
- Validate SSL certificates
- Use secure protocols (TLS 1.2 or higher)

### Authentication Security
- Implement secure password storage (hashing with salt)
- Use biometric authentication where available
- Support multi-factor authentication
- Implement proper session timeout and logout functionality

The application should also consider implementing security headers and protections against common mobile vulnerabilities such as:
- Insecure data storage
- Insufficient transport layer protection
- Weak server-side controls
- Client-side injection

Regular security audits and penetration testing are recommended to identify and address potential vulnerabilities.

**Section sources**
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)