# Authentication

<cite>
**Referenced Files in This Document**   
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs)
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)
- [Auth.cs](file://src/Unlimotion.Server.ServiceModel/Auth.cs)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs)
- [LoginHistory.cs](file://src/Unlimotion.Server.ServiceModel/Molds/LoginHistory.cs)
- [UserLoginAudit.cs](file://src/Unlimotion.Server.ServiceModel/Molds/UserLoginAudit.cs)
- [LoginAudit.cs](file://src/Unlimotion.Server.ServiceModel/LoginAudit.cs)
- [User.cs](file://src/Unlimotion.Domain/User.cs)
- [IChatHub.cs](file://src/Unlimotion.Interface/IChatHub.cs)
- [LogOn.cs](file://src/Unlimotion.Interface/LogOn.cs)
- [TokenResult.cs](file://src/Unlimotion.Server.ServiceModel/Molds/TokenResult.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Authentication Architecture](#authentication-architecture)
3. [JWT Token Workflow](#jwt-token-workflow)
4. [Login Process in ChatHub.Login](#login-process-in-chathublogin)
5. [User Session Management](#user-session-management)
6. [ServiceStack Integration](#servicestack-integration)
7. [User Audit Logging](#user-audit-logging)
8. [Error Handling](#error-handling)
9. [Token Generation and Client Implementation](#token-generation-and-client-implementation)
10. [Conclusion](#conclusion)

## Introduction
This document provides comprehensive documentation for Unlimotion's authentication system using JWT tokens. The system implements a secure authentication mechanism that integrates SignalR for real-time communication, ServiceStack for service endpoints, and RavenDB for data persistence. The authentication flow centers around JWT token validation, user session management, and comprehensive audit logging. This documentation details the complete authentication workflow, from token generation to session validation and error handling.

## Authentication Architecture
The Unlimotion authentication system follows a layered architecture that integrates multiple components to provide secure user authentication and authorization. The system leverages JWT tokens for stateless authentication, SignalR for real-time communication, and ServiceStack's authentication framework for service-level security.

```mermaid
graph TD
Client[Client Application] --> |JWT Token| SignalR[SignalR Hub]
Client --> |Credentials| ServiceStack[ServiceStack API]
ServiceStack --> |Generate Token| JWT[JWT Provider]
JWT --> |Store Key| AppHost[AppHost Configuration]
SignalR --> |Validate Token| JWT
SignalR --> |Store Session| Context[Connection Context]
SignalR --> |Audit Login| RavenDB[RavenDB Storage]
ServiceStack --> |Authorize| AuthFeature[AuthFeature]
AuthFeature --> |Verify| JWT
TaskService[TaskService] --> |Authenticate| AuthFeature
Audit[User Audit] --> |Store| LoginAudit[LoginAudit Document]
style JWT fill:#4CAF50
style AuthFeature fill:#2196F3
style SignalR fill:#FF9800
```

**Diagram sources**
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs)
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)

**Section sources**
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs)
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)

## JWT Token Workflow
The JWT token workflow in Unlimotion follows a standard security pattern with additional encryption and session management features. The system generates encrypted JWT tokens (JWE) using RS512 algorithm and stores critical user information in the token payload.

```mermaid
sequenceDiagram
participant Client
participant AuthService
participant JwtProvider
participant ChatHub
Client->>AuthService : POST /password/login<br/>{login, password}
AuthService->>AuthService : Validate credentials
AuthService->>AuthService : Load User from RavenDB
AuthService->>JwtProvider : Generate JWT token pair
JwtProvider-->>AuthService : Access Token + Refresh Token
AuthService-->>Client : Return TokenResult
Client->>ChatHub : Connect to /chathub
Client->>ChatHub : Login(token, os, ip, clientVersion)
ChatHub->>JwtProvider : GetVerifiedJwtPayload(token)
JwtProvider-->>ChatHub : Verified JWT payload
ChatHub->>ChatHub : Extract uid, login, session
ChatHub->>ChatHub : Store in Context.Items
ChatHub->>RavenDB : Load LoginAudit record
ChatHub->>RavenDB : Update audit information
ChatHub-->>Client : Send LogOn response
Note over ChatHub,Client : Token validation and<br/>session establishment
```

**Diagram sources**
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs#L157-L217)
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs#L131-L162)

**Section sources**
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs)

## Login Process in ChatHub.Login
The ChatHub.Login method implements the core authentication workflow for SignalR connections. This method validates the JWT token, extracts user identity information, establishes the user session, and performs audit logging.

```mermaid
flowchart TD
Start([Login Method Entry]) --> ValidateToken["GetVerifiedJwtPayload(token)"]
ValidateToken --> TokenValid{"Token Valid?"}
TokenValid --> |No| HandleError["Catch Exception"]
TokenValid --> |Yes| ExtractClaims["Extract sub, name, session from payload"]
ExtractClaims --> StoreContext["Store uid, login, session in Context.Items"]
StoreContext --> AddToGroups["Add to Logined and User_{uid} groups"]
AddToGroups --> CheckUser["Load User by uid from RavenDB"]
CheckUser --> UserExists{"User Found?"}
UserExists --> |No| SendError["Send ErrorUserNotFound"]
UserExists --> |Yes| CheckExpiration["Parse and validate exp claim"]
CheckExpiration --> Expired{"Token Expired?"}
Expired --> |Yes| SendExpired["Send ErrorExpiredToken"]
Expired --> |No| UpdateAudit["Update LoginAudit record"]
UpdateAudit --> SendSuccess["Send LogOn with Ok status"]
SendSuccess --> End([Login Complete])
SendError --> End
SendExpired --> End
HandleError --> End
style ValidateToken fill:#2196F3
style ExtractClaims fill:#2196F3
style StoreContext fill:#4CAF50
style UpdateAudit fill:#FF9800
```

**Diagram sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs#L131-L220)

**Section sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs#L131-L220)

## User Session Management
User session information is stored in the SignalR connection context and used for authorization throughout the user's session. The system maintains user identity and session data in memory for the duration of the connection.

```mermaid
classDiagram
class ChatHub {
+Login(token, os, ip, clientVersion)
+OnDisconnectedAsync(exception)
+SaveTask(hubTask)
+UpdateMyDisplayName(name)
+DeleteTasks(idTasks)
}
class ConnectionContext {
+Items : Dictionary[string, object]
+ConnectionId : string
}
class SessionData {
+uid : string
+login : string
+session : string
+nickname : string
}
ChatHub --> ConnectionContext : "uses"
ConnectionContext --> SessionData : "contains"
note right of ChatHub
Manages SignalR hub methods
Validates JWT tokens
Establishes user sessions
end
note right of ConnectionContext
Stores session data in Items dictionary
Persists for connection duration
Accessible to all hub methods
end
note right of SessionData
uid : User ID from JWT sub claim
login : Username from JWT name claim
session : Unique session ID
nickname : Display name from User entity
end
```

**Diagram sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs#L131-L162)
- [IChatHub.cs](file://src/Unlimotion.Interface/IChatHub.cs)

**Section sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs)
- [IChatHub.cs](file://src/Unlimotion.Interface/IChatHub.cs)

## ServiceStack Integration
The authentication system integrates with ServiceStack's authentication framework to provide consistent security across HTTP API endpoints. The [Authenticate] attribute is used to protect service methods and ensure only authorized users can access them.

```mermaid
sequenceDiagram
participant Client
participant TaskService
participant AuthFeature
participant JwtProvider
Client->>TaskService : GET /tasks
TaskService->>AuthFeature : Request.ThrowIfUnauthorized()
AuthFeature->>JwtProvider : Validate JWT token
JwtProvider-->>AuthFeature : Verified AuthUserSession
AuthFeature-->>TaskService : Return UserAuthId
TaskService->>TaskService : Query RavenDB for user tasks
TaskService-->>Client : Return TaskItemPage
alt Unauthorized Request
Client->>TaskService : GET /tasks (invalid token)
TaskService->>AuthFeature : Request.ThrowIfUnauthorized()
AuthFeature->>JwtProvider : Validate JWT token
JwtProvider-->>AuthFeature : Token validation failed
AuthFeature-->>Client : Return 401 Unauthorized
end
Note over AuthFeature,TaskService : ServiceStack authentication<br/>interceptor pattern
```

**Diagram sources**
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs#L20-L30)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs#L61-L90)

**Section sources**
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)

## User Audit Logging
The system implements comprehensive user audit logging that records operating system, IP address, client version, and session information upon each login. This information is stored in RavenDB for security monitoring and analysis.

```mermaid
flowchart TD
A[Login Method] --> B{UserLoginAudit Exists?}
B --> |No| C[Create New LoginAudit]
B --> |Yes| D{Session Changed?}
D --> |No| E[No Update Needed]
D --> |Yes| F[Update Audit Fields]
C --> G[Set Id = uid + '/LoginAudit']
G --> H[Set OperatingSystem]
H --> I[Set IpAddress]
I --> J[Set NameVersionClient]
J --> K[Set DateOfEntry]
K --> L[Set SessionId]
L --> M[Store in RavenDB]
F --> N[Update All Fields]
N --> M
M --> O[Save Changes]
O --> P[Login Complete]
E --> P
style C fill:#4CAF50
style F fill:#FF9800
style M fill:#2196F3
```

**Diagram sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs#L191-L220)
- [UserLoginAudit.cs](file://src/Unlimotion.Server.ServiceModel/Molds/UserLoginAudit.cs)

**Section sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs#L191-L220)
- [UserLoginAudit.cs](file://src/Unlimotion.Server.ServiceModel/Molds/UserLoginAudit.cs)

## Error Handling
The authentication system implements comprehensive error handling for common authentication failures, including expired tokens, invalid signatures, and missing user accounts. Errors are communicated to clients through appropriate status codes and error messages.

```mermaid
flowchart TD
A[Authentication Error] --> B{Error Type}
B --> C[Expired Token]
B --> D[Invalid Signature]
B --> E[Missing User Account]
B --> F[Other Errors]
C --> G[LogOn.Error = ErrorExpiredToken]
C --> H[Status Code: 419]
C --> I[Message: Token is expired]
D --> J[TokenException in ChatHub]
D --> K[Log Warning: Bad token]
D --> L[Silent Failure]
E --> M[LogOn.Error = ErrorUserNotFound]
E --> N[Status Code: 404]
E --> O[Message: User not found]
F --> P[HttpError with appropriate code]
F --> Q[ServiceStack Exception Handling]
F --> R[Log Exception Details]
style C fill:#F44336
style D fill:#F44336
style E fill:#F44336
style F fill:#9E9E9E
```

**Diagram sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs#L191-L220)
- [LogOn.cs](file://src/Unlimotion.Interface/LogOn.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)

**Section sources**
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs)
- [LogOn.cs](file://src/Unlimotion.Interface/LogOn.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)

## Token Generation and Client Implementation
The token generation process creates encrypted JWT tokens with appropriate claims and expiration times. Client applications must implement proper token management to maintain user sessions.

```mermaid
sequenceDiagram
participant Client
participant AuthService
participant JwtProvider
Client->>AuthService : POST /password/login
activate AuthService
AuthService->>AuthService : Validate login/password
AuthService->>AuthService : Load User from RavenDB
AuthService->>JwtProvider : CreateJwtPayload()
JwtProvider-->>AuthService : JWT payload with claims
AuthService->>JwtProvider : CreateEncryptedJweToken()
JwtProvider-->>AuthService : Encrypted access token
AuthService->>JwtProvider : Create refresh token
JwtProvider-->>AuthService : Encrypted refresh token
AuthService-->>Client : Return TokenResult
deactivate AuthService
Note over AuthService,JwtProvider : Token generation with<br/>RS512 encryption and<br/>JWE format
Client->>Client : Store tokens securely
Client->>SignalR : Connect with token
SignalR->>SignalR : Validate token on login
```

**Diagram sources**
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs#L157-L217)
- [TokenResult.cs](file://src/Unlimotion.Server.ServiceModel/Molds/TokenResult.cs)

**Section sources**
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)
- [TokenResult.cs](file://src/Unlimotion.Server.ServiceModel/Molds/TokenResult.cs)

## Conclusion
The Unlimotion authentication system provides a robust and secure mechanism for user authentication using JWT tokens. The system integrates SignalR for real-time communication, ServiceStack for service-level security, and RavenDB for persistent storage of user and audit data. Key features include encrypted JWT tokens, comprehensive session management, detailed audit logging, and consistent error handling. The architecture ensures that user identity is securely maintained throughout the application, with proper authorization enforced at both the SignalR hub and service endpoint levels. This documentation provides a comprehensive overview of the authentication workflow, from token generation to session validation and error handling.