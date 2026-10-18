-- Simulates what the 10:00 daily job does once a tenant's vacate month has ended:
-- flips the outgoing tenant to SettlingVacate (TenantStatus = 4). Run in SSMS on shms_database,
-- then call POST /api/vacate/run-auto-close (or wait for the 10:05 job) to watch the closure.
--   TEST-Q  Advance Credit : has a PAID pre-registered successor (house must stay Occupied)
--   TEST-D  Rewards        : no successor (house must flip to Vacant and return to public listing)
USE [shms_database];
GO
UPDATE t SET t.TenantStatus = 4
FROM Tenants t JOIN PortalUsers u ON u.Id = t.Id
WHERE u.Email IN ('test-tenant-advancecredit@shms-test.invalid', 'test-tenant-rewards@shms-test.invalid')
  AND u.IsDeleted = 0;
SELECT u.Email, t.TenantStatus FROM Tenants t JOIN PortalUsers u ON u.Id = t.Id
WHERE u.Email IN ('test-tenant-advancecredit@shms-test.invalid', 'test-tenant-rewards@shms-test.invalid');
GO

-- 72-hour expiry test: back-date the UNPAID explorer-converted tenant (zz.xd@example.com on ZNW2) by 4 days.
-- The pre-registration expiry sweep runs when the backend starts (then every 6 hours), so restart the
-- backend after running this. Expected: tenant disappears, ZedXD's explorer interest returns to Pending.
UPDATE PortalUsers SET CreatedAt = DATEADD(day, -4, GETUTCDATE())
WHERE Email = 'zz.xd@example.com' AND IsDeleted = 0;
GO
