-- Gives the "PreReg Verify" test tenant a known email-verification token so the verify-email API can be called
-- without the (undeliverable) email. Token is valid for 1 day.
USE [shms_database];
GO
UPDATE u SET EmailVerificationToken = 'prereg-test-token-0001', EmailVerificationTokenExpiry = DATEADD(day, 1, GETUTCDATE()), IsEmailVerified = 0
FROM PortalUsers u WHERE u.Email = 'musauronald02+prereg@gmail.com';
SELECT u.Email, u.IsEmailVerified, u.EmailVerificationToken, t.TenantStatus, t.HasCompletedInitialPayment, t.LeaseStartMonth, t.LeaseStartYear
FROM PortalUsers u JOIN Tenants t ON t.Id = u.Id WHERE u.Email = 'musauronald02+prereg@gmail.com';
GO
