-- Re-opens TEST-N "Test Tenant Overdue" as a leaving tenant (SettlingVacate) owing September + October rent in full,
-- with their refund still unpaid, so the Leaving Tenants pages can be exercised again. ZedXC keeps TEST-N occupied.
USE [shms_database];
GO
DECLARE @T uniqueidentifier = '7E570000-0000-0000-0004-000000000014';
UPDATE PortalUsers SET IsDeleted = 0, DeletedAt = NULL, IsActive = 1 WHERE Id = @T;
UPDATE Tenants SET TenantStatus = 4 WHERE Id = @T;
UPDATE Payments SET AmountPaid = 0, Balance = Amount, PaymentStatus = 4, PaidAt = NULL, MpesaReceiptNumber = NULL
WHERE TenantId = @T AND IsDeleted = 0 AND ((Year = 2026 AND Month IN (9, 10)));
UPDATE VacateRequests SET ClosedAt = NULL, ClosedByAdminId = NULL WHERE TenantId = @T AND IsDeleted = 0;
UPDATE TenantHouseHistories SET RemovedAt = NULL WHERE TenantId = @T AND RemovedAt IS NOT NULL;
UPDATE VacateSettlements SET PaidAt = NULL WHERE TenantId = @T AND Direction = 'ManagementOwes' AND IsVoided = 0;
SELECT u.Email, u.IsDeleted, t.TenantStatus FROM Tenants t JOIN PortalUsers u ON u.Id = t.Id WHERE t.Id = @T;
SELECT Year, Month, Amount, AmountPaid, Balance, PaymentStatus FROM Payments WHERE TenantId = @T ORDER BY Year, Month;
GO
