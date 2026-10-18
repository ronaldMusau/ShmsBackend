-- TEST-N "Test Tenant Overdue" has unpaid rent and an Approved vacate. Put them in SettlingVacate (what the
-- daily job does once the vacate month has passed) so arrears-after-move-out can be tested.
USE [shms_database];
GO
UPDATE t SET t.TenantStatus = 4
FROM Tenants t JOIN PortalUsers u ON u.Id = t.Id
WHERE u.Email = 'test-tenant-overdue@shms-test.invalid' AND u.IsDeleted = 0;
SELECT u.Email, t.TenantStatus FROM Tenants t JOIN PortalUsers u ON u.Id = t.Id
WHERE u.Email = 'test-tenant-overdue@shms-test.invalid';
GO
