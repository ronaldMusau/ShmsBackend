-- Gives every explorer the known test password (Test1234!) by copying the hash from the PreReg test tenant, then lists them.
USE [shms_database];
GO
DECLARE @h NVARCHAR(MAX) = (SELECT TOP 1 PasswordHash FROM PortalUsers WHERE FirstName LIKE 'PreReg%' AND PasswordHash <> '');
IF @h IS NULL THROW 50000, 'PreReg hash not found', 1;
UPDATE p SET PasswordHash=@h, IsEmailVerified=1, FailedLoginAttempts=0, IsLockedOut=0 FROM PortalUsers p JOIN Explorers e ON e.Id=p.Id;
SELECT p.Email, p.FirstName, p.LastName, p.Ward FROM PortalUsers p JOIN Explorers e ON e.Id=p.Id;
GO
