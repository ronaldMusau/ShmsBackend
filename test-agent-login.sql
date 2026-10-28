-- Gives both test agents the known test password (Test1234!) so agent-side session actions can be tested.
USE [shms_database];
GO
DECLARE @h NVARCHAR(MAX) = (SELECT TOP 1 PasswordHash FROM PortalUsers WHERE FirstName LIKE 'PreReg%' AND PasswordHash <> '');
IF @h IS NULL THROW 50000, 'PreReg hash not found', 1;
UPDATE p SET PasswordHash=@h, IsEmailVerified=1, FailedLoginAttempts=0, IsLockedOut=0 FROM PortalUsers p JOIN Agents a ON a.Id=p.Id;
SELECT p.Email, p.FirstName FROM PortalUsers p JOIN Agents a ON a.Id=p.Id;
GO
