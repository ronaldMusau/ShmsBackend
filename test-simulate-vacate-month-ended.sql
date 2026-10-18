-- Simulates what the 10:00 daily job does once a tenant's vacate month has ended:
-- flips the outgoing tenant to SettlingVacate (TenantStatus = 4). Run in SSMS on shms_database,
-- then call POST /api/vacate/run-auto-close (or wait for the 10:05 job) to watch the closure.
--   TEST-Q  Advance Credit : has a PAID pre-registered successor (house must stay Occupied)
--   TEST-D  Rewards        : no successor (house must flip to Vacant and return to public listing)
USE [shms_database];
GO
UPDATE PortalUsers SET TenantStatus = 4, UpdatedAt = GETUTCDATE()
WHERE Email IN ('test-tenant-advancecredit@shms-test.invalid', 'test-tenant-rewards@shms-test.invalid')
  AND IsDeleted = 0;
SELECT Email, TenantStatus FROM PortalUsers
WHERE Email IN ('test-tenant-advancecredit@shms-test.invalid', 'test-tenant-rewards@shms-test.invalid');
GO
