-- =====================================================
-- Smart Housing Management System - Super Admin Seed
-- =====================================================
-- Email: musauronald02@gmail.com
-- Password: Ronald123!
-- UserType: 0 (SuperAdmin)
-- =====================================================

USE [shms_database];
GO

-- Check if database exists, if not create it
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'shms_database')
BEGIN
    CREATE DATABASE [shms_database];
    PRINT '✅ Database created: shms_database';
END
GO

USE [shms_database];
GO

-- Check if Admins table exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Admins')
BEGIN
    PRINT '❌ Admins table does not exist. Please run migrations first:';
    PRINT '   dotnet ef database update --project src/ShmsBackend.Data';
    RETURN;
END
GO

-- Seed Super Admin
DECLARE @UserId uniqueidentifier = NEWID();
DECLARE @Now datetime = GETUTCDATE();
DECLARE @Email nvarchar(255) = 'musauronald02@gmail.com';

-- Check if user already exists
IF NOT EXISTS (SELECT 1 FROM Admins WHERE Email = @Email)
BEGIN
    BEGIN TRANSACTION;

    -- Insert into base Admins table
    INSERT INTO Admins (
        Id, 
        Email, 
        PasswordHash, 
        FirstName, 
        LastName, 
        PhoneNumber, 
        IsActive, 
        IsEmailVerified, 
        UserType, 
        CreatedAt, 
        UpdatedAt, 
        CreatedBy
    )
    VALUES (
        @UserId, 
        @Email, 
        '/HqwfFYz1zfeZjqTqbgbNIXR2457pPS3ChQ2mDefO6C', -- Password: Ronald123!
        'Ronald', 
        'Musau', 
        '+254700000000', 
        1, -- IsActive
        1, -- IsEmailVerified
        0, -- 0 = SuperAdmin
        @Now, 
        @Now, 
        NULL
    );

    -- Insert into SuperAdmins child table
    INSERT INTO SuperAdmins (Id, SuperAdminPermissions)
    VALUES (@UserId, 'full_access');

    COMMIT TRANSACTION;

    PRINT '✅ Super Admin created successfully!';
    
    -- Show the created user
    SELECT 
        a.Id,
        a.Email,
        a.FirstName + ' ' + a.LastName AS FullName,
        CASE a.UserType 
            WHEN 0 THEN 'SuperAdmin'
            WHEN 1 THEN 'Admin'
            WHEN 2 THEN 'Manager'
            WHEN 3 THEN 'Accountant'
            WHEN 4 THEN 'Secretary'
        END AS Role,
        a.IsActive,
        a.CreatedAt
    FROM Admins a
    WHERE a.Email = @Email;
END
ELSE
BEGIN
    PRINT '⚠️ Super Admin already exists!';
    
    -- Show existing user
    SELECT 
        a.Id,
        a.Email,
        a.FirstName + ' ' + a.LastName AS FullName,
        CASE a.UserType 
            WHEN 0 THEN 'SuperAdmin'
            WHEN 1 THEN 'Admin'
            WHEN 2 THEN 'Manager'
            WHEN 3 THEN 'Accountant'
            WHEN 4 THEN 'Secretary'
        END AS Role,
        a.IsActive,
        a.CreatedAt
    FROM Admins a
    WHERE a.Email = @Email;
END
GO

-- Show all admins in the system
PRINT '
📊 Current Admins in System:';
SELECT 
    Email,
    CASE UserType 
        WHEN 0 THEN 'SuperAdmin'
        WHEN 1 THEN 'Admin'
        WHEN 2 THEN 'Manager'
        WHEN 3 THEN 'Accountant'
        WHEN 4 THEN 'Secretary'
    END AS Role,
    IsActive,
    CreatedAt
FROM Admins
ORDER BY UserType, Email;
GO
