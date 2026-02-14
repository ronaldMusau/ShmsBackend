USE shms_database;
GO

DECLARE @UserId uniqueidentifier = NEWID();
DECLARE @Now datetime = GETUTCDATE();

-- Check if user already exists
IF NOT EXISTS (SELECT 1 FROM Admins WHERE Email = 'musauronald02@gmail.com')
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
        'musauronald02@gmail.com', 
        '/HqwfFYz1zfeZjqTqbgbNIXR2457pPS3ChQ2mDefO6C', 
        'Ronald', 
        'Musau', 
        '+254700000000', 
        1, 
        1, 
        0, -- 0 = SuperAdmin (UserType.SuperAdmin)
        @Now, 
        @Now, 
        NULL
    );

    -- Insert into SuperAdmins child table
    INSERT INTO SuperAdmins (Id, SuperAdminPermissions)
    VALUES (@UserId, 'full_access');

    COMMIT TRANSACTION;

    PRINT 'Super Admin created successfully!';
    
    -- Show the created user
    SELECT 
        a.Id,
        a.Email,
        a.FirstName,
        a.LastName,
        a.UserType,
        'SuperAdmin' as RoleType,
        a.IsActive,
        a.CreatedAt
    FROM Admins a
    WHERE a.Email = 'musauronald02@gmail.com';
END
ELSE
BEGIN
    PRINT 'Super Admin already exists!';
    
    -- Show existing user
    SELECT 
        a.Id,
        a.Email,
        a.FirstName,
        a.LastName,
        a.UserType,
        CASE 
            WHEN sa.Id IS NOT NULL THEN 'SuperAdmin'
            WHEN au.Id IS NOT NULL THEN 'Admin'
            WHEN m.Id IS NOT NULL THEN 'Manager'
            WHEN acc.Id IS NOT NULL THEN 'Accountant'
            WHEN sec.Id IS NOT NULL THEN 'Secretary'
        END as RoleType,
        a.IsActive,
        a.CreatedAt
    FROM Admins a
    LEFT JOIN SuperAdmins sa ON a.Id = sa.Id
    LEFT JOIN AdminUsers au ON a.Id = au.Id
    LEFT JOIN Managers m ON a.Id = m.Id
    LEFT JOIN Accountants acc ON a.Id = acc.Id
    LEFT JOIN Secretaries sec ON a.Id = sec.Id
    WHERE a.Email = 'musauronald02@gmail.com';
END
GO
