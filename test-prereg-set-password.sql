-- Gives the verified "PreReg Verify" test tenant a known password (Test1234!) so its portal can be checked.
USE [shms_database];
GO
UPDATE PortalUsers SET PasswordHash = '$2b$11$svu10PSiY9oDHYx6Xm2RkuqvATYFFBezEDWXK7BHm43LFwKh.kn1G', IsActive = 1, UpdatedAt = GETUTCDATE()
WHERE Email = 'musauronald02+prereg@gmail.com';
SELECT Email, IsEmailVerified, IsActive FROM PortalUsers WHERE Email = 'musauronald02+prereg@gmail.com';
GO
