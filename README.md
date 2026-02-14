# Smart Housing Management System (SHMS) - Backend API

![.NET](https://img.shields.io/badge/.NET-8.0-blue?logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Status](https://img.shields.io/badge/status-Active-brightgreen)

A comprehensive backend API for managing student housing operations, built with ASP.NET Core 8.0. Features sophisticated role-based authentication, multi-tier admin support, secure OTP verification, and comprehensive user management capabilities.

## 📋 Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Role Hierarchy](#role-hierarchy--permission-matrix)
- [Technology Stack](#-technology-stack)
- [Installation](#-installation-guide)
- [API Endpoints](#-api-endpoints-reference)
- [Authentication Flow](#-authentication-flow-2-step-verification)
- [Project Structure](#-project-structure)
- [Testing](#-testing)
- [Security](#-security-features)
- [Contributing](#-contributing)
- [License](#-license)

## Overview

SHMS is designed to streamline student housing management with a robust backend API that handles:
- **User Management** - Multi-role support with granular permissions
- **Authentication** - Two-step OTP verification with JWT tokens
- **Real-time Notifications** - SignalR integration for instant updates
- **Email Services** - Integration with Resend API for notifications
- **Caching** - Redis for performance optimization

## Architecture

The system implements **Table-per-Type (TPT)** inheritance pattern for efficient role management:

```
Admins (Base Table)
├── SuperAdmins     (Full system access)
├── AdminUsers      (Middle management)
├── Managers        (Department managers)
├── Accountants     (Financial officers)
└── Secretaries     (Administrative staff)
```

## Role Hierarchy & Permission Matrix

### User Types & Access Levels

| Value | Role | Access Level | Description |
|-------|------|--------------|-------------|
| 0 | SuperAdmin | 👑 Full System | Complete CRUD operations on all users |
| 1 | Admin | 📊 Middle Management | Can manage Managers, Accountants, Secretaries |
| 2 | Manager | 📋 Department Level | Self-management only |
| 3 | Accountant | 💰 Financial | Self-management only |
| 4 | Secretary | 📝 Administrative | Self-management only |

### Detailed Permission Matrix

| Action | SuperAdmin | Admin | Manager | Accountant | Secretary |
|--------|-----------|-------|---------|-----------|-----------|
| Create Admin | ✅ | ❌ | ❌ | ❌ | ❌ |
| Create Manager | ✅ | ✅ | ❌ | ❌ | ❌ |
| Create Accountant | ✅ | ✅ | ❌ | ❌ | ❌ |
| Create Secretary | ✅ | ✅ | ❌ | ❌ | ❌ |
| View All Users | ✅ | Only lower roles* | ❌ | ❌ | ❌ |
| View Own Profile | ✅ | ✅ | ✅ | ✅ | ✅ |
| Update Self | ✅ | ✅ | ✅ | ✅ | ✅ |
| Update Others | ✅ | Only lower roles | ❌ | ❌ | ❌ |
| Delete Users | ✅ | ❌ | ❌ | ❌ | ❌ |

*Lower roles = Manager, Accountant, Secretary only

## 🚀 Technology Stack

### Core Framework
- **.NET 8.0** - Modern, cross-platform framework
- **C# 12** - Latest language features

### Data Layer
- **Entity Framework Core 8.0** - ORM with LINQ support
- **SQL Server** - Primary database
- **Redis** - Distributed caching & OTP storage

### Authentication & Security
- **JWT Bearer** - Stateless authentication
- **BCrypt.Net-Next** - Secure password hashing
- **OTP Verification** - Two-step login flow
- **Token Blacklisting** - Secure logout mechanism

### Communication
- **Resend API** - Email delivery service
- **SignalR** - Real-time notifications
- **RESTful APIs** - Standard HTTP endpoints

### Documentation & Testing
- **Swagger/OpenAPI** - Interactive API documentation
- **xUnit** - Unit testing framework
- **Moq** - Mocking framework

## 📁 Project Structure

```
ShmsBackend/
├── src/
│   ├── ShmsBackend.Api/                    # Web API Project
│   │   ├── Controllers/                     # API Endpoints
│   │   ├── Services/                        # Business Logic
│   │   │   ├── Auth/
│   │   │   ├── User/
│   │   │   ├── Email/
│   │   │   └── OTP/
│   │   ├── Middleware/                      # Custom middleware
│   │   ├── Configuration/                   # Settings classes
│   │   ├── DTOs/                            # Data Transfer Objects
│   │   ├── Responses/                       # API Responses
│   │   ├── Hubs/                            # SignalR Hubs
│   │   ├── Utilities/                       # Helper classes
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   └── ShmsBackend.Data/                    # Data Layer
│       ├── Context/
│       ├── Models/
│       │   └── Entities/
│       ├── Repositories/
│       ├── Enums/
│       └── Migrations/
│
├── tests/
│   └── ShmsBackend.Tests/
│
├── docker-compose.yml
├── seed-admin.sql
└── README.md
```

## 🔐 Authentication Flow (2-Step Verification)

### Step 1: Pre-Login (Email + Role Selection)

```http
POST /api/auth/pre-login
Content-Type: application/json

{
    "email": "admin@shms.com",
    "selectedUserType": 1
}
```

**Response:**
```json
{
    "success": true,
    "data": {
        "email": "admin@shms.com",
        "selectedUserType": 1,
        "firstName": "John",
        "lastName": "Doe",
        "message": "Verification code sent to your email"
    }
}
```

### Step 2: Verify OTP + Password

```http
POST /api/auth/verify-otp
Content-Type: application/json

{
    "email": "admin@shms.com",
    "selectedUserType": 1,
    "password": "********",
    "otp": "123456"
}
```

**Success Response:**
```json
{
    "success": true,
    "data": {
        "accessToken": "eyJhbGciOiJIUzI1NiIs...",
        "refreshToken": "a1b2c3d4e5f6...",
        "userId": "123e4567-e89b-12d3-a456-426614174000",
        "email": "admin@shms.com",
        "firstName": "John",
        "lastName": "Doe",
        "userType": 1,
        "expiresAt": "2024-01-01T12:00:00Z"
    },
    "message": "Login successful"
}
```

## 📦 Installation Guide

### Prerequisites

- .NET 8.0 SDK
- SQL Server (2019 or later)
- Redis (Windows/Linux/Mac)
- Visual Studio 2022 or VS Code
- Git

### Step-by-Step Installation

#### 1. Clone Repository

```bash
git clone https://github.com/ronaldMusau/ShmsBackend.git
cd ShmsBackend
```

#### 2. Configure Database Connection

Edit `src/ShmsBackend.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=SHMS_DB;User Id=sa;Password=YourStrong!Password;TrustServerCertificate=True;"
  },
  "RedisOptions": {
    "Configuration": "localhost:6379,password=YourRedisPassword,abortConnect=false",
    "InstanceName": "SHMS:"
  },
  "JwtOptions": {
    "Secret": "Your-Super-Secret-JWT-Key-At-Least-32-Chars-Long",
    "Issuer": "SHMS_API",
    "Audience": "SHMS_Client"
  },
  "ResendOptions": {
    "ApiKey": "your-resend-api-key"
  }
}
```

#### 3. Install Redis (Windows)

```powershell
# Using Chocolatey
choco install redis-64 -y
redis-server
```

Or download from [Redis.io](https://redis.io/download)

#### 4. Apply Database Migrations

```bash
cd src/ShmsBackend.Api
dotnet ef database update --project ../ShmsBackend.Data
```

#### 5. Seed Super Admin

```bash
sqlcmd -S localhost -U sa -P "YourPassword" -i seed-admin.sql
```

#### 6. Run the Application

```bash
dotnet run
```

The API will be available at `https://localhost:7001`

Access Swagger documentation: `https://localhost:7001/swagger`

## 🔌 API Endpoints Reference

### Base URL

```
Development: https://localhost:7001
Production:  https://your-domain.com
```

### Authentication Endpoints

| Method | Endpoint | Description | Authorization |
|--------|----------|-------------|----------------|
| POST | `/api/auth/pre-login` | Request OTP | None |
| POST | `/api/auth/verify-otp` | Verify & Login | None |
| POST | `/api/auth/refresh-token` | Refresh JWT | None |
| POST | `/api/auth/logout` | Logout | ✅ JWT |
| POST | `/api/auth/request-password-reset` | Reset password | None |

### User Management Endpoints

| Method | Endpoint | Description | Required Role |
|--------|----------|-------------|----------------|
| POST | `/api/user` | Create user | SuperAdmin/Admin |
| GET | `/api/user` | Get all users | SuperAdmin/Admin |
| GET | `/api/user/{id}` | Get user by ID | All authenticated |
| PUT | `/api/user/{id}` | Update user | SuperAdmin/Admin |
| DELETE | `/api/user/{id}` | Delete user | SuperAdmin |
| PATCH | `/api/user/{id}/toggle-status` | Toggle status | SuperAdmin |
| GET | `/api/user/{id}/type` | Get user type | All authenticated |

### Health Check

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/health` | API health status |

### SignalR Hub

| Endpoint | Description |
|----------|-------------|
| `/hubs/notifications` | Real-time notifications |

## 📝 Example API Calls

### Create a Manager (as Admin)

```http
POST /api/user
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
    "email": "manager@shms.com",
    "password": "Manager@123",
    "firstName": "Sarah",
    "lastName": "Johnson",
    "phoneNumber": "+254722333444",
    "userType": 2,
    "managedDepartment": "Student Housing",
    "teamSize": 8
}
```

### Update Secretary (as Admin)

```http
PUT /api/user/123e4567-e89b-12d3-a456-426614174000
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
    "firstName": "Updated Name",
    "officeNumber": "OFF-202",
    "isActive": true
}
```

## 🧪 Testing

### Run Unit Tests

```bash
cd tests/ShmsBackend.Tests
dotnet test
```

### Test Coverage

- Authentication flow
- Role-based authorization
- CRUD operations
- Permission validation
- OTP verification

## 🚢 Deployment

### Docker Support

```bash
# Build Docker image
docker build -t shms-api .

# Run container
docker run -d -p 8080:8080 --name shms-api shms-api
```

### IIS Deployment

```bash
# Publish application
dotnet publish -c Release

# Deploy to IIS with appropriate application pool
# Configure environment variables
```

## 🔒 Security Features

- **JWT Authentication** - Stateless token-based auth
- **OTP Verification** - Two-step login process
- **Password Hashing** - BCrypt with salt
- **Token Blacklisting** - Immediate logout capability
- **Role-Based Access** - Granular permissions
- **SQL Injection Protection** - EF Core parameterized queries
- **CORS Policy** - Configured allowed origins
- **HTTPS Enforcement** - Secure communication

## 📊 Database Schema

The system uses a Table-per-Type (TPT) inheritance pattern:

```sql
-- Base table for all users
CREATE TABLE Admins (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Email NVARCHAR(255) UNIQUE,
    PasswordHash NVARCHAR(MAX),
    FirstName NVARCHAR(100),
    LastName NVARCHAR(100),
    PhoneNumber NVARCHAR(20),
    IsActive BIT DEFAULT 1,
    IsEmailVerified BIT DEFAULT 0,
    UserType INT NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CreatedBy UNIQUEIDENTIFIER NULL
);

-- Role-specific tables
CREATE TABLE SuperAdmins (Id UNIQUEIDENTIFIER PRIMARY KEY REFERENCES Admins(Id));
CREATE TABLE AdminUsers (Id UNIQUEIDENTIFIER PRIMARY KEY REFERENCES Admins(Id));
CREATE TABLE Managers (Id UNIQUEIDENTIFIER PRIMARY KEY REFERENCES Admins(Id));
CREATE TABLE Accountants (Id UNIQUEIDENTIFIER PRIMARY KEY REFERENCES Admins(Id));
CREATE TABLE Secretaries (Id UNIQUEIDENTIFIER PRIMARY KEY REFERENCES Admins(Id));
```

## 🤝 Contributing

Contributions are welcome! Here's how to contribute:

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📧 Support & Contact

- **Issues**: [GitHub Issues](https://github.com/ronaldMusau/ShmsBackend/issues)
- **Discussions**: [GitHub Discussions](https://github.com/ronaldMusau/ShmsBackend/discussions)

## 🙏 Acknowledgments

- ASP.NET Core Team
- Entity Framework Core Team
- Open-source community contributors

---

**Last Updated**: February 2026

Made with ❤️ by Ronald Musau
