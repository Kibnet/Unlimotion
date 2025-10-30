# Profile API

<cite>
**Referenced Files in This Document**
- [IProfile.cs](file://src/Unlimotion.Interface/IProfile.cs)
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface/ProfileService.cs)
- [Profile.cs](file://src/Unlimotion.Server.ServiceModel/Profile.cs)
- [UserProfileMold.cs](file://src/Unlimotion.Server.ServiceModel/Molds/UserProfileMold.cs)
- [MyUserProfileMold.cs](file://src/Unlimotion.Server.ServiceModel/Molds/MyUserProfileMold.cs)
- [User.cs](file://src/Unlimotion.Domain/User.cs)
- [AppModelMapping.cs](file://src/Unlimotion.Server/AppModelMapping.cs)
- [RequestExtensions.cs](file://src/Unlimotion.Server.ServiceInterface\RequestExtensions.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface\AuthService.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Architecture Overview](#architecture-overview)
3. [Core Components](#core-components)
4. [Authentication System](#authentication-system)
5. [RESTful Endpoints](#restful-endpoints)
6. [Data Models and Schemas](#data-models-and-schemas)
7. [Data Persistence Flow](#data-persistence-flow)
8. [Business Logic Implementation](#business-logic-implementation)
9. [Error Handling](#error-handling)
10. [Integration Examples](#integration-examples)
11. [Performance Considerations](#performance-considerations)
12. [Troubleshooting Guide](#troubleshooting-guide)

## Introduction

The Unlimotion Profile API provides comprehensive user profile management capabilities through RESTful endpoints. Built on the ServiceStack framework with RavenDB as the underlying data store, this API handles both retrieval and modification of user profile information while maintaining strict authentication requirements and data integrity.

The Profile API serves two primary functions: retrieving user profiles through the `GetProfile` endpoint and updating user profiles via the `SetProfile` endpoint. Both operations are secured using the `[Authenticate]` attribute and rely on user identification through session-based authentication tokens.

## Architecture Overview

The Profile API follows a layered architecture pattern with clear separation of concerns:

```mermaid
graph TB
subgraph "Client Layer"
Client[HTTP Client]
WebUI[Web Interface]
end
subgraph "Service Layer"
PS[ProfileService]
IS[IProfile Interface]
end
subgraph "Data Access Layer"
RS[RavenDB Session]
AM[AutoMapper]
end
subgraph "Domain Layer"
U[User Domain Model]
UM[User Profile Models]
end
subgraph "Authentication Layer"
AS[AuthService]
JWT[JWT Tokens]
AU[AuthUserSession]
end
Client --> PS
WebUI --> PS
PS --> IS
PS --> RS
PS --> AM
RS --> U
AM --> UM
AS --> JWT
JWT --> AU
AU --> PS
```

**Diagram sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface/ProfileService.cs#L13-L48)
- [IProfile.cs](file://src/Unlimotion.Interface/IProfile.cs#L8-L11)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface\AuthService.cs#L88-L127)

## Core Components

### ProfileService Implementation

The `ProfileService` class serves as the primary controller for profile operations, inheriting from ServiceStack's `Service` base class. It implements both GET and POST operations for profile management.

```mermaid
classDiagram
class ProfileService {
+IAsyncDocumentSession RavenSession
+IMapper Mapper
+Get(GetProfile) Task~UserProfileMold~
+Post(SetProfile) Task~UserProfileMold~
}
class IProfile {
<<interface>>
+Open(string userId) Task
}
class Service {
<<ServiceStack>>
+Request IRequest
}
ProfileService --|> Service
ProfileService ..|> IProfile
```

**Diagram sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface/ProfileService.cs#L13-L48)
- [IProfile.cs](file://src/Unlimotion.Interface/IProfile.cs#L8-L11)

### Authentication Integration

The service integrates seamlessly with ServiceStack's authentication system through the `[Authenticate]` attribute, ensuring that all profile operations require valid user authentication.

**Section sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface/ProfileService.cs#L18-L48)
- [IProfile.cs](file://src/Unlimotion.Interface/IProfile.cs#L8-L11)

## Authentication System

### JWT Token-Based Authentication

The Profile API relies on JWT (JSON Web Tokens) for user authentication. The authentication process involves:

1. **Token Validation**: Each request must include a valid JWT token in the Authorization header
2. **Session Extraction**: The `UserAuthId` is extracted from the JWT payload to identify the authenticated user
3. **Permission Verification**: The `[Authenticate]` attribute ensures only authenticated users can access profile endpoints

### Session Management

The authentication system uses ServiceStack's `AuthUserSession` to manage user sessions and extract user identifiers:

```mermaid
sequenceDiagram
participant Client
participant PS as ProfileService
participant RE as RequestExtensions
participant AS as AuthService
participant RS as RavenDB
Client->>PS : POST /setprofile
PS->>RE : ThrowIfUnauthorized()
RE->>AS : Extract UserAuthId from JWT
AS-->>RE : AuthUserSession
RE-->>PS : Validated session
PS->>RS : Query user by UserAuthId
RS-->>PS : User object
PS->>PS : Apply business logic
PS->>RS : Save updated user
PS-->>Client : Updated UserProfileMold
```

**Diagram sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface/ProfileService.cs#L25-L32)
- [RequestExtensions.cs](file://src/Unlimotion.Server.ServiceInterface\RequestExtensions.cs#L8-L17)

**Section sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface/ProfileService.cs#L18-L48)
- [RequestExtensions.cs](file://src/Unlimotion.Server.ServiceInterface\RequestExtensions.cs#L8-L17)

## RESTful Endpoints

### GetProfile Endpoint

Retrieves a user's profile information by user ID.

| Aspect | Details |
|--------|---------|
| **HTTP Method** | GET |
| **Endpoint** | `/getprofile` |
| **Authentication** | Required |
| **Request Schema** | `GetProfile` |
| **Response Schema** | `UserProfileMold` |
| **Content Type** | `application/json` |

#### Request Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `UserId` | string | Yes | Unique identifier of the user whose profile to retrieve |

#### Example Request
```http
GET /getprofile?UserId=users/123
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

#### Example Response
```json
{
  "id": "users/123",
  "login": "john.doe",
  "displayName": "John Doe",
  "aboutMe": "Software developer specializing in .NET applications"
}
```

### SetProfile Endpoint

Updates the authenticated user's profile information.

| Aspect | Details |
|--------|---------|
| **HTTP Method** | POST |
| **Endpoint** | `/setprofile` |
| **Authentication** | Required |
| **Request Schema** | `SetProfile` |
| **Response Schema** | `UserProfileMold` |
| **Content Type** | `application/json` |

#### Request Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `DisplayName` | string | Yes | User's display name (truncated to 32 characters) |
| `AboutMe` | string | Yes | User's self-description |

#### Example Request
```json
{
  "displayName": "Updated Display Name",
  "aboutMe": "Updated bio information about the user"
}
```

#### Example Response
```json
{
  "id": "users/123",
  "login": "john.doe",
  "displayName": "Updated Display Name",
  "aboutMe": "Updated bio information about the user"
}
```

**Section sources**
- [Profile.cs](file://src/Unlimotion.Server.ServiceModel/Profile.cs#L12-L32)

## Data Models and Schemas

### UserProfileMold

The primary data transfer object representing user profile information.

```mermaid
classDiagram
class UserProfileMold {
+string Id
+string Login
+string DisplayName
+string AboutMe
}
class MyUserProfileMold {
+bool IsPasswordSetted
}
class SetProfile {
+string DisplayName
+string AboutMe
}
class User {
+string Id
+string Login
+string DisplayName
+string AboutMe
+DateTimeOffset RegisteredTime
}
MyUserProfileMold --|> UserProfileMold
SetProfile --> User : "maps to"
User --> UserProfileMold : "mapped from"
```

**Diagram sources**
- [UserProfileMold.cs](file://src/Unlimotion.Server.ServiceModel\Molds\UserProfileMold.cs#L5-L19)
- [MyUserProfileMold.cs](file://src/Unlimotion.Server.ServiceModel\Molds\MyUserProfileMold.cs#L5-L10)
- [User.cs](file://src/Unlimotion.Domain\User.cs#L5-L15)

### Field Specifications

| Field | Type | Length Limit | Description |
|-------|------|--------------|-------------|
| `Id` | string | - | Unique user identifier |
| `Login` | string | - | Username for authentication |
| `DisplayName` | string | 32 characters | User's display name (truncated) |
| `AboutMe` | string | - | User's self-description |
| `IsPasswordSetted` | boolean | - | Indicates if user has set a password |

### AutoMapper Configuration

The system uses AutoMapper for seamless data transformation between models:

| Source Model | Target Model | Transformation Rules |
|--------------|--------------|---------------------|
| `User` | `UserProfileMold` | Direct property mapping |
| `SetProfile` | `User` | Excludes Id, Login, RegisteredTime |
| `User` | `MyUserProfileMold` | Inherits from UserProfileMold |

**Section sources**
- [UserProfileMold.cs](file://src/Unlimotion.Server.ServiceModel\Molds\UserProfileMold.cs#L5-L19)
- [MyUserProfileMold.cs](file://src/Unlimotion.Server.ServiceModel\Molds\MyUserProfileMold.cs#L5-L10)
- [AppModelMapping.cs](file://src/Unlimotion.Server\AppModelMapping.cs#L38-L46)

## Data Persistence Flow

### RavenDB Integration

The Profile API utilizes RavenDB for data persistence, configured through ServiceStack's dependency injection system:

```mermaid
flowchart TD
Start([Profile Operation]) --> Auth["Authentication Check"]
Auth --> Load["Load User from RavenDB"]
Load --> Transform["AutoMapper Transformation"]
Transform --> Business["Apply Business Logic"]
Business --> Store["Save to RavenDB"]
Store --> Response["Return Response"]
Auth --> |Invalid| AuthError["Throw Unauthorized Error"]
Load --> |Not Found| NotFoundError["Throw Not Found Error"]
Store --> |Success| Response
```

**Diagram sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L18-L48)

### Session Management

The service injects an `IAsyncDocumentSession` for database operations, ensuring asynchronous database access and proper resource management.

### Transaction Handling

All profile updates occur within a single transaction:
1. **Load Existing User**: Retrieve user by UserAuthId
2. **Apply Changes**: Update displayName with truncation logic
3. **Save Changes**: Persist modifications to RavenDB
4. **Return Result**: Map and return updated profile

**Section sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L15-L17)
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L25-L48)

## Business Logic Implementation

### Display Name Truncation

The system implements automatic truncation of display names to 32 characters to maintain data consistency and prevent potential issues with storage limits or UI constraints.

```mermaid
flowchart TD
Input["SetProfile Request"] --> Check["Check User Exists"]
Check --> |Exists| Truncate["Truncate DisplayName to 32 chars"]
Check --> |Not Found| Error["Throw Not Found Error"]
Truncate --> Map["AutoMap to User"]
Map --> Save["Save to Database"]
Save --> Return["Return Updated Profile"]
```

**Diagram sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L33-L35)

### User Validation Logic

The service performs comprehensive user validation:
1. **Authentication Verification**: Ensures user is authenticated
2. **User Existence Check**: Verifies the user exists in the database
3. **Data Integrity**: Maintains referential integrity during updates

### Error Conditions

| Condition | HTTP Status | Error Message | Resolution |
|-----------|-------------|---------------|------------|
| User not found | 404 | "Пользователь не найден в базе данных" | Verify user ID exists |
| Unauthorized access | 401 | "Unauthorized" | Provide valid JWT token |
| Invalid request | 400 | "Неверно составлен запрос" | Check request format |

**Section sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L37-L48)
- [RequestExtensions.cs](file://src/Unlimotion.Server.ServiceInterface\RequestExtensions.cs#L12-L17)

## Error Handling

### Exception Management

The Profile API implements comprehensive error handling through ServiceStack's exception management system:

```mermaid
flowchart TD
Request["Incoming Request"] --> TryCatch["Try-Catch Block"]
TryCatch --> AuthCheck{"Authentication Valid?"}
AuthCheck --> |No| Unauthorized["401 Unauthorized"]
AuthCheck --> |Yes| UserCheck{"User Exists?"}
UserCheck --> |No| NotFound["404 Not Found"]
UserCheck --> |Yes| Process["Process Request"]
Process --> Success["200 OK Response"]
Unauthorized --> ErrorResponse["Error Response"]
NotFound --> ErrorResponse
Success --> SuccessResponse["Success Response"]
```

**Diagram sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L25-L48)
- [RequestExtensions.cs](file://src/Unlimotion.Server.ServiceInterface\RequestExtensions.cs#L12-L17)

### Error Response Formats

All errors follow a consistent response format:
```json
{
  "errorCode": "UNAUTHORIZED_ACCESS",
  "message": "Пользователь не найден в базе данных",
  "details": "Additional contextual information"
}
```

### Authentication-Specific Errors

| Error Type | HTTP Status | Description |
|------------|-------------|-------------|
| Missing Token | 401 | No JWT token provided |
| Invalid Token | 401 | Malformed or expired JWT |
| Unauthorized Access | 401 | Insufficient permissions |

**Section sources**
- [RequestExtensions.cs](file://src/Unlimotion.Server.ServiceInterface\RequestExtensions.cs#L12-L17)
- [AuthService.cs](file://src/Unlimotion.Server\ServiceInterface\AuthService.cs#L88-L127)

## Integration Examples

### Complete Profile Update Workflow

```mermaid
sequenceDiagram
participant Client
participant PS as ProfileService
participant RE as RequestExtensions
participant RS as RavenDB
participant AM as AutoMapper
Client->>PS : POST /setprofile
Note over Client,PS : {"displayName" : "New Name", "aboutMe" : "Bio"}
PS->>RE : ThrowIfUnauthorized()
RE->>RE : Extract UserAuthId from JWT
RE-->>PS : Valid session
PS->>RS : Query<User>().FirstOrDefaultAsync(x => x.Id == uid)
RS-->>PS : User object
PS->>PS : Truncate displayName to 32 chars
PS->>AM : Map<SetProfile, User>(request, existedUser)
AM-->>PS : Updated User object
PS->>RS : StoreAsync(user)
PS->>RS : SaveChangesAsync()
PS->>AM : Map<UserProfileMold>(user)
AM-->>PS : UserProfileMold
PS-->>Client : Updated profile response
```

**Diagram sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L25-L48)

### Sample API Calls

#### Retrieving Another User's Profile
```bash
curl -X GET "https://api.unlimotion.com/getprofile?UserId=users/456" \
     -H "Authorization: Bearer YOUR_JWT_TOKEN" \
     -H "Content-Type: application/json"
```

#### Updating Current User's Profile
```bash
curl -X POST "https://api.unlimotion.com/setprofile" \
     -H "Authorization: Bearer YOUR_JWT_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{
           "displayName": "Updated Display Name",
           "aboutMe": "Updated bio information"
         }'
```

**Section sources**
- [Profile.cs](file://src/Unlimotion.Server.ServiceModel\Profile.cs#L12-L32)

## Performance Considerations

### Database Optimization

1. **Indexing Strategy**: RavenDB automatically indexes user IDs for efficient lookups
2. **Connection Pooling**: Uses ServiceStack's built-in connection pooling for RavenDB sessions
3. **Async Operations**: All database operations use async/await patterns for non-blocking I/O

### Caching Opportunities

While not currently implemented, potential caching strategies include:
- **Profile Caching**: Cache frequently accessed user profiles
- **Session Caching**: Cache user authentication state
- **Metadata Caching**: Cache user metadata for reduced database load

### Scalability Factors

| Factor | Impact | Mitigation Strategy |
|--------|--------|-------------------|
| Concurrent Requests | Linear scaling | Async operations, connection pooling |
| Database Size | Proportional growth | Proper indexing, partitioning |
| Memory Usage | User count × profile size | Efficient serialization, garbage collection |

## Troubleshooting Guide

### Common Issues and Solutions

#### 401 Unauthorized Errors
**Symptoms**: Authentication failures when accessing profile endpoints
**Causes**: 
- Missing or invalid JWT token
- Expired authentication token
- Incorrect Authorization header format

**Solutions**:
1. Verify JWT token is included in Authorization header
2. Ensure token format: `Bearer YOUR_JWT_TOKEN`
3. Refresh authentication token if expired

#### 404 Not Found Errors
**Symptoms**: User not found when retrieving profiles
**Causes**:
- Invalid user ID parameter
- User account deleted or inactive
- Cross-user access attempts

**Solutions**:
1. Verify user ID format (should match database convention)
2. Confirm user account exists and is active
3. Ensure proper user permissions

#### Display Name Truncation Issues
**Symptoms**: Unexpected truncation of display names
**Causes**:
- Business logic truncation to 32 characters
- Encoding issues with Unicode characters

**Solutions**:
1. Ensure display names are within 32 character limit
2. Handle Unicode normalization appropriately
3. Validate input length before submission

### Debug Information

Enable detailed logging for troubleshooting:
```csharp
// RavenDB query logging
Log.Information("Querying user: {UserId}", request.UserId);

// Authentication verification
Log.Debug("UserAuthId: {UserAuthId}", session?.UserAuthId);
```

### Monitoring Metrics

Key metrics to monitor:
- **Authentication Success Rate**: Percentage of successful authentications
- **Profile Load Times**: Average response time for profile operations
- **Database Query Performance**: RavenDB query execution times
- **Error Rates**: Breakdown of different error types

**Section sources**
- [ProfileService.cs](file://src/Unlimotion.Server.ServiceInterface\ProfileService.cs#L25-L48)
- [RequestExtensions.cs](file://src/Unlimotion.Server.ServiceInterface\RequestExtensions.cs#L12-L17)