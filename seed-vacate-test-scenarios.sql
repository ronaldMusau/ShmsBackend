-- =====================================================
-- Smart Housing Management System - Vacate Payment Cap Test Scenarios
-- =====================================================
-- TEST DATA ONLY. Seeds one test property with four houses and four tenants,
-- each in a specific state for exercising the vacate-payment-cap logic.
--
-- All tenants (and the test landlord) log in with:
--   Password: TestTenant123!
--
--   TEST-A  Test Tenant NoCredit     test-tenant-nocredit@shms-test.invalid
--   TEST-B  Test Tenant CreditExact  test-tenant-creditexact@shms-test.invalid
--   TEST-C  Test Tenant CreditOver   test-tenant-creditover@shms-test.invalid
--   TEST-D  Test Tenant Rewards      test-tenant-rewards@shms-test.invalid
--   TEST-E  Test Tenant Appeal       test-tenant-appeal@shms-test.invalid      (UnderAppeal + live settlement)
--   TEST-F  Test Tenant PaidSettle   test-tenant-paidsettle@shms-test.invalid  (AwaitingFinalRemarks + paid settlement)
--
-- Every row uses a fixed GUID starting 7E570000-0000-0000- ("TEST"), grouped by the 4th block:
--   0001 = landlord, 0002 = flat, 0003 = houses, 0004 = tenants,
--   0005 = vacate requests, 0006 = payments, 0007 = vacate settlements.
-- TEST-E/F are seeded by a second, separately-guarded block, so re-running this script only adds them.
-- Houses/tenants/vacate requests/settlements end in A-F to match TEST-A..TEST-F;
-- payments end in <house letter> + YYYYMM (e.g. ...0006-00000A202609 = TEST-A, Sept 2026).
-- =====================================================

USE [shms_database];
-- Required by the filtered unique index on Houses; sqlcmd defaults it OFF. Set in its own batch (applies at parse time).
SET QUOTED_IDENTIFIER ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LandlordId uniqueidentifier = '7E570000-0000-0000-0001-000000000001';
DECLARE @FlatId     uniqueidentifier = '7E570000-0000-0000-0002-000000000001';

DECLARE @HouseA uniqueidentifier = '7E570000-0000-0000-0003-00000000000A';
DECLARE @HouseB uniqueidentifier = '7E570000-0000-0000-0003-00000000000B';
DECLARE @HouseC uniqueidentifier = '7E570000-0000-0000-0003-00000000000C';
DECLARE @HouseD uniqueidentifier = '7E570000-0000-0000-0003-00000000000D';

DECLARE @TenantA uniqueidentifier = '7E570000-0000-0000-0004-00000000000A';
DECLARE @TenantB uniqueidentifier = '7E570000-0000-0000-0004-00000000000B';
DECLARE @TenantC uniqueidentifier = '7E570000-0000-0000-0004-00000000000C';
DECLARE @TenantD uniqueidentifier = '7E570000-0000-0000-0004-00000000000D';

-- BCrypt hash of "TestTenant123!" (generated with TempHash, work factor 11)
DECLARE @PasswordHash nvarchar(255) = '$2a$11$O5VcFJfBdUln78vNWTQID.9Mo.53528tvXMjqNeQ3ctqZqBmsmEwW';
DECLARE @Now datetime2 = SYSUTCDATETIME();

-- Existing "Single Room" house type (house types are shared reference data — not created here)
DECLARE @HouseTypeId uniqueidentifier = (SELECT TOP 1 Id FROM HouseTypes WHERE Name = 'Single Room' AND IsDeleted = 0);

IF @HouseTypeId IS NULL
BEGIN
    PRINT '❌ No "Single Room" house type found. Run migrations / create house types first.';
    RETURN;
END

IF EXISTS (SELECT 1 FROM Flats WHERE Id = @FlatId)
   OR EXISTS (SELECT 1 FROM PortalUsers WHERE Id IN (@LandlordId, @TenantA, @TenantB, @TenantC, @TenantD))
BEGIN
    PRINT '⚠️ Vacate test scenarios already seeded — nothing inserted.';
END
ELSE
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        -- ── Test landlord (Flats require one) ────────────────────────────────
        INSERT INTO PortalUsers (Id, Email, PasswordHash, FirstName, LastName, PhoneNumber,
                                 IsActive, IsEmailVerified, PortalUserType, CreatedAt, UpdatedAt)
        VALUES (@LandlordId, 'test-landlord@shms-test.invalid', @PasswordHash, 'Test', 'Landlord', '254700000001',
                1, 1, 0 /* Landlord */, @Now, @Now);

        INSERT INTO Landlords (Id, AgencyName) VALUES (@LandlordId, 'TEST — DO NOT USE');

        -- ── Test property ────────────────────────────────────────────────────
        -- RentDueDay 5 (so Sept/Oct are past due); rewards enabled so TEST-D can redeem.
        INSERT INTO Flats (Id, FlatName, LandlordId, RentDueDay, BillableGracePeriodMonths, VacateNoticeDeadlineDay,
                           SitDeposit, RewardEnabled, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@FlatId, N'TEST PROPERTY — DO NOT USE', @LandlordId, 5, 3, 15,
                0, 1, @Now, @Now, 0);

        INSERT INTO Houses (Id, HouseNumber, HouseTypeId, RentFee, DepositFee, Description,
                            OccupancyStatus, PaymentStatus, FlatId, CreatedAt, UpdatedAt,
                            IsListingHidden, CommentsMuted, IsAwaitingExistingTenant, IsDeleted)
        VALUES
            (@HouseA, 'TEST-A', @HouseTypeId, 5000, 5000, 'TEST — vacate cap: no credit',     'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0),
            (@HouseB, 'TEST-B', @HouseTypeId, 5000, 5000, 'TEST — vacate cap: credit exact',  'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0),
            (@HouseC, 'TEST-C', @HouseTypeId, 5000, 5000, 'TEST — vacate cap: credit over',   'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0),
            (@HouseD, 'TEST-D', @HouseTypeId, 5000, 5000, 'TEST — vacate cap: rewards',       'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0);

        -- ── Tenants: Active, onboarded, cycle 1, moved in August 2026 ────────
        INSERT INTO PortalUsers (Id, Email, PasswordHash, FirstName, LastName, PhoneNumber,
                                 IsActive, IsEmailVerified, PortalUserType, CreatedAt, UpdatedAt)
        VALUES
            (@TenantA, 'test-tenant-nocredit@shms-test.invalid',    @PasswordHash, 'Test Tenant', 'NoCredit',    '254708374149', 1, 1, 2 /* Tenant */, @Now, @Now),
            (@TenantB, 'test-tenant-creditexact@shms-test.invalid', @PasswordHash, 'Test Tenant', 'CreditExact', '254700000002', 1, 1, 2, @Now, @Now),
            (@TenantC, 'test-tenant-creditover@shms-test.invalid',  @PasswordHash, 'Test Tenant', 'CreditOver',  '254700000003', 1, 1, 2, @Now, @Now),
            (@TenantD, 'test-tenant-rewards@shms-test.invalid',     @PasswordHash, 'Test Tenant', 'Rewards',     '254700000004', 1, 1, 2, @Now, @Now);

        -- RedemptionRate is 0.005 KES/point, so TEST-D's 5,000,000 points = KES 25,000 (> 20,000 owed through Dec).
        INSERT INTO Tenants (Id, HouseId, TenantStatus, HasCompletedInitialPayment, TenancyCycle,
                             LeaseStartMonth, LeaseStartYear, PointsBalance)
        VALUES
            (@TenantA, @HouseA, 2 /* Active */, 1, 1, 8, 2026, 0),
            (@TenantB, @HouseB, 2, 1, 1, 8, 2026, 0),
            (@TenantC, @HouseC, 2, 1, 1, 8, 2026, 0),
            (@TenantD, @HouseD, 2, 1, 1, 8, 2026, 5000000);

        -- ── Payments ─────────────────────────────────────────────────────────
        -- Status: 2 Paid, 4 Overdue, 5 Failed.  Type: 0 InitialPayment, 1 Rent.  Method: 0 Mpesa.
        DECLARE @Rows TABLE (
            Id uniqueidentifier, TenantId uniqueidentifier, HouseId uniqueidentifier,
            Mo int, Yr int, Amount decimal(18,2), AmountPaid decimal(18,2),
            Status int, PayType int, IsInitial bit, RentAmount decimal(18,2), DepositAmount decimal(18,2));

        -- Base setup for every tenant: August initial payment (rent + deposit) paid; Sept + Oct rent overdue.
        INSERT INTO @Rows
        SELECT CAST('7E570000-0000-0000-0006-00000' + t.Letter + '20260' + CAST(m.Mo AS varchar(1)) AS uniqueidentifier),
               t.TenantId, t.HouseId, m.Mo, 2026,
               m.Amount, m.AmountPaid, m.Status, m.PayType, m.IsInitial, 5000, m.DepositAmount
        FROM (VALUES ('A', @TenantA, @HouseA), ('B', @TenantB, @HouseB),
                     ('C', @TenantC, @HouseC), ('D', @TenantD, @HouseD)) t(Letter, TenantId, HouseId)
        CROSS JOIN (VALUES (8, 10000, 10000, 2, 0, 1, 5000),
                           (9,  5000,     0, 4, 1, 0, NULL)) m(Mo, Amount, AmountPaid, Status, PayType, IsInitial, DepositAmount);

        INSERT INTO @Rows VALUES
            ('7E570000-0000-0000-0006-00000A202610', @TenantA, @HouseA, 10, 2026, 5000, 0, 4, 1, 0, 5000, NULL),
            ('7E570000-0000-0000-0006-00000B202610', @TenantB, @HouseB, 10, 2026, 5000, 0, 4, 1, 0, 5000, NULL),
            ('7E570000-0000-0000-0006-00000C202610', @TenantC, @HouseC, 10, 2026, 5000, 0, 4, 1, 0, 5000, NULL),
            ('7E570000-0000-0000-0006-00000D202610', @TenantD, @HouseD, 10, 2026, 5000, 0, 4, 1, 0, 5000, NULL),
            -- TEST-A: failed January 2027 row (after the vacate month) for the RetryPayment test
            ('7E570000-0000-0000-0006-00000A202701', @TenantA, @HouseA,  1, 2027, 3000, 0, 5, 1, 0, 3000, NULL),
            -- TEST-B: November + December 2026 paid in advance
            ('7E570000-0000-0000-0006-00000B202611', @TenantB, @HouseB, 11, 2026, 5000, 5000, 2, 1, 0, 5000, NULL),
            ('7E570000-0000-0000-0006-00000B202612', @TenantB, @HouseB, 12, 2026, 5000, 5000, 2, 1, 0, 5000, NULL),
            -- TEST-C: November + December 2026 + January 2027 paid in advance
            ('7E570000-0000-0000-0006-00000C202611', @TenantC, @HouseC, 11, 2026, 5000, 5000, 2, 1, 0, 5000, NULL),
            ('7E570000-0000-0000-0006-00000C202612', @TenantC, @HouseC, 12, 2026, 5000, 5000, 2, 1, 0, 5000, NULL),
            ('7E570000-0000-0000-0006-00000C202701', @TenantC, @HouseC,  1, 2027, 5000, 5000, 2, 1, 0, 5000, NULL);

        INSERT INTO Payments (Id, TenantId, HouseId, FlatId, LandlordId,
                              Amount, AmountPaid, Balance, RentAmount, DepositAmount,
                              PaymentStatus, PaymentType, PaymentMethod, MpesaReceiptNumber,
                              DueDate, PaidAt, Month, Year, RetryCount, Description, Notes,
                              IsInitialPayment, TenancyCycle, IsDeleted, CreatedAt, UpdatedAt)
        SELECT r.Id, r.TenantId, r.HouseId, @FlatId, @LandlordId,
               r.Amount, r.AmountPaid, r.Amount - r.AmountPaid, r.RentAmount, r.DepositAmount,
               r.Status, r.PayType,
               CASE WHEN r.AmountPaid > 0 THEN 0 END,
               CASE WHEN r.AmountPaid > 0 THEN 'TEST' + RIGHT(REPLACE(CAST(r.Id AS varchar(36)), '-', ''), 6) END,
               DATEFROMPARTS(r.Yr, r.Mo, 5),
               CASE WHEN r.AmountPaid > 0 THEN CAST('2026-08-01' AS datetime2) END,
               r.Mo, r.Yr, 0,
               CASE WHEN r.IsInitial = 1 THEN 'TEST — Initial payment (deposit + first rent)'
                    ELSE 'TEST — Rent ' + DATENAME(month, DATEFROMPARTS(r.Yr, r.Mo, 1)) + ' ' + CAST(r.Yr AS varchar(4)) END,
               'TEST SEED — seed-vacate-test-scenarios.sql',
               r.IsInitial, 1, 0, @Now, @Now
        FROM @Rows r;

        -- ── Approved vacate requests (ClosedAt NULL = still active) ──────────
        INSERT INTO VacateRequests (Id, TenantId, TenancyCycle, HouseId, FlatId, LandlordId,
                                    Status, NeedsResubmission, ApprovalAttemptNumber,
                                    VacateMonth, VacateYear, SitDeposit, ClosedAt,
                                    ReviewedAt, Reason, InitiationType, CreatedAt, UpdatedAt, IsDeleted)
        VALUES
            ('7E570000-0000-0000-0005-00000000000A', @TenantA, 1, @HouseA, @FlatId, @LandlordId, 'Approved', 0, 1, 12, 2026, 0, NULL, @Now, 'TEST — vacate cap: no credit',    'Voluntary', @Now, @Now, 0),
            ('7E570000-0000-0000-0005-00000000000B', @TenantB, 1, @HouseB, @FlatId, @LandlordId, 'Approved', 0, 1, 12, 2026, 0, NULL, @Now, 'TEST — vacate cap: credit exact', 'Voluntary', @Now, @Now, 0),
            ('7E570000-0000-0000-0005-00000000000C', @TenantC, 1, @HouseC, @FlatId, @LandlordId, 'Approved', 0, 1, 11, 2026, 0, NULL, @Now, 'TEST — vacate cap: credit over',  'Voluntary', @Now, @Now, 0),
            ('7E570000-0000-0000-0005-00000000000D', @TenantD, 1, @HouseD, @FlatId, @LandlordId, 'Approved', 0, 1, 12, 2026, 0, NULL, @Now, 'TEST — vacate cap: rewards',      'Voluntary', @Now, @Now, 0);

        COMMIT TRANSACTION;
        PRINT '✅ Vacate test scenarios seeded.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        PRINT '❌ Seeding failed — nothing inserted: ' + ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

-- =====================================================
-- TEST-E / TEST-F: cancellation and re-approval guards
--   TEST-E  VacateRequest UnderAppeal (not Approved) but with a live settlement — cancel must still be refused.
--   TEST-F  VacateRequest AwaitingFinalRemarks with an already-PAID settlement — re-approval must be refused.
-- Uses the same test landlord / flat as above, so that block must have run first.
-- =====================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LandlordId uniqueidentifier = '7E570000-0000-0000-0001-000000000001';
DECLARE @FlatId     uniqueidentifier = '7E570000-0000-0000-0002-000000000001';

DECLARE @HouseE uniqueidentifier = '7E570000-0000-0000-0003-00000000000E';
DECLARE @HouseF uniqueidentifier = '7E570000-0000-0000-0003-00000000000F';
DECLARE @TenantE uniqueidentifier = '7E570000-0000-0000-0004-00000000000E';
DECLARE @TenantF uniqueidentifier = '7E570000-0000-0000-0004-00000000000F';
DECLARE @VacateE uniqueidentifier = '7E570000-0000-0000-0005-00000000000E';
DECLARE @VacateF uniqueidentifier = '7E570000-0000-0000-0005-00000000000F';

DECLARE @PasswordHash nvarchar(255) = '$2a$11$O5VcFJfBdUln78vNWTQID.9Mo.53528tvXMjqNeQ3ctqZqBmsmEwW';
DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @HouseTypeId uniqueidentifier = (SELECT TOP 1 Id FROM HouseTypes WHERE Name = 'Single Room' AND IsDeleted = 0);

IF NOT EXISTS (SELECT 1 FROM Flats WHERE Id = @FlatId)
BEGIN
    PRINT '❌ Test property not found — the TEST-A..D block must seed first.';
END
ELSE IF EXISTS (SELECT 1 FROM PortalUsers WHERE Id IN (@TenantE, @TenantF))
     OR EXISTS (SELECT 1 FROM Houses WHERE Id IN (@HouseE, @HouseF))
BEGIN
    PRINT '⚠️ TEST-E/F scenarios already seeded — nothing inserted.';
END
ELSE
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO Houses (Id, HouseNumber, HouseTypeId, RentFee, DepositFee, Description,
                            OccupancyStatus, PaymentStatus, FlatId, CreatedAt, UpdatedAt,
                            IsListingHidden, CommentsMuted, IsAwaitingExistingTenant, IsDeleted)
        VALUES
            (@HouseE, 'TEST-E', @HouseTypeId, 5000, 5000, 'TEST — cancel guard: under appeal with settlement', 'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0),
            (@HouseF, 'TEST-F', @HouseTypeId, 5000, 5000, 'TEST — re-approval guard: paid settlement',         'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0);

        INSERT INTO PortalUsers (Id, Email, PasswordHash, FirstName, LastName, PhoneNumber,
                                 IsActive, IsEmailVerified, PortalUserType, CreatedAt, UpdatedAt)
        VALUES
            (@TenantE, 'test-tenant-appeal@shms-test.invalid',     @PasswordHash, 'Test Tenant', 'Appeal',     '254700000005', 1, 1, 2 /* Tenant */, @Now, @Now),
            (@TenantF, 'test-tenant-paidsettle@shms-test.invalid', @PasswordHash, 'Test Tenant', 'PaidSettle', '254700000006', 1, 1, 2, @Now, @Now);

        INSERT INTO Tenants (Id, HouseId, TenantStatus, HasCompletedInitialPayment, TenancyCycle,
                             LeaseStartMonth, LeaseStartYear, PointsBalance)
        VALUES
            (@TenantE, @HouseE, 2 /* Active */, 1, 1, 8, 2026, 0),
            (@TenantF, @HouseF, 2, 1, 1, 8, 2026, 0);

        -- August initial payment (rent + deposit) paid; Sept + Oct rent overdue — same base as TEST-A..D.
        INSERT INTO Payments (Id, TenantId, HouseId, FlatId, LandlordId,
                              Amount, AmountPaid, Balance, RentAmount, DepositAmount,
                              PaymentStatus, PaymentType, PaymentMethod, MpesaReceiptNumber,
                              DueDate, PaidAt, Month, Year, RetryCount, Description, Notes,
                              IsInitialPayment, TenancyCycle, IsDeleted, CreatedAt, UpdatedAt)
        SELECT CAST('7E570000-0000-0000-0006-00000' + t.Letter + '20' + RIGHT('0000' + CAST(m.Yr * 100 + m.Mo AS varchar(6)), 4) AS uniqueidentifier),
               t.TenantId, t.HouseId, @FlatId, @LandlordId,
               m.Amount, m.AmountPaid, m.Amount - m.AmountPaid, 5000, m.DepositAmount,
               m.Status, m.PayType,
               CASE WHEN m.AmountPaid > 0 THEN 0 END,
               CASE WHEN m.AmountPaid > 0 THEN 'TEST' + t.Letter + CAST(m.Yr * 100 + m.Mo AS varchar(6)) END,
               DATEFROMPARTS(m.Yr, m.Mo, 5),
               CASE WHEN m.AmountPaid > 0 THEN CAST('2026-08-01' AS datetime2) END,
               m.Mo, m.Yr, 0,
               CASE WHEN m.IsInitial = 1 THEN 'TEST — Initial payment (deposit + first rent)'
                    ELSE 'TEST — Rent ' + DATENAME(month, DATEFROMPARTS(m.Yr, m.Mo, 1)) + ' ' + CAST(m.Yr AS varchar(4)) END,
               'TEST SEED — seed-vacate-test-scenarios.sql',
               m.IsInitial, 1, 0, @Now, @Now
        FROM (VALUES ('E', @TenantE, @HouseE), ('F', @TenantF, @HouseF)) t(Letter, TenantId, HouseId)
        CROSS JOIN (VALUES ( 8, 2026, 10000, 10000, 2, 0, 1, 5000),
                           ( 9, 2026,  5000,     0, 4, 1, 0, NULL),
                           (10, 2026,  5000,     0, 4, 1, 0, NULL)) m(Mo, Yr, Amount, AmountPaid, Status, PayType, IsInitial, DepositAmount);

        INSERT INTO VacateRequests (Id, TenantId, TenancyCycle, HouseId, FlatId, LandlordId,
                                    Status, NeedsResubmission, ApprovalAttemptNumber,
                                    VacateMonth, VacateYear, SitDeposit, ClosedAt,
                                    ReviewedAt, Reason, InitiationType, CreatedAt, UpdatedAt, IsDeleted)
        VALUES
            (@VacateE, @TenantE, 1, @HouseE, @FlatId, @LandlordId, 'UnderAppeal',          0, 1, 12, 2026, 0, NULL, @Now, 'TEST — cancel guard: under appeal with settlement', 'Voluntary', @Now, @Now, 0),
            (@VacateF, @TenantF, 1, @HouseF, @FlatId, @LandlordId, 'AwaitingFinalRemarks', 0, 2, 12, 2026, 0, NULL, @Now, 'TEST — re-approval guard: paid settlement',         'Voluntary', @Now, @Now, 0);

        -- Settlements from the earlier approval. TEST-E: live, unpaid. TEST-F: live, already paid.
        INSERT INTO VacateSettlements (Id, VacateRequestId, LandlordId, TenantId, HouseId, FlatId,
                                       Direction, Amount, Description, PaidAt, CreatedAt, IsVoided)
        VALUES
            ('7E570000-0000-0000-0007-00000000000E', @VacateE, @LandlordId, @TenantE, @HouseE, @FlatId,
             'TenantOwes', 5000, 'TEST — settlement from earlier approval (unpaid)', NULL, @Now, 0),
            ('7E570000-0000-0000-0007-00000000000F', @VacateF, @LandlordId, @TenantF, @HouseF, @FlatId,
             'TenantOwes', 5000, 'TEST — settlement from earlier approval (already paid)', CAST('2026-10-01T09:00:00' AS datetime2), @Now, 0);

        COMMIT TRANSACTION;
        PRINT '✅ TEST-E/F scenarios seeded.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        PRINT '❌ TEST-E/F seeding failed — nothing inserted: ' + ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

-- Show what's seeded
SELECT h.HouseNumber, pu.FirstName + ' ' + pu.LastName AS Tenant, pu.Email, t.PointsBalance,
       v.VacateMonth, v.VacateYear, v.Status AS VacateStatus
FROM Tenants t
JOIN PortalUsers pu ON pu.Id = t.Id
JOIN Houses h ON h.Id = t.HouseId
LEFT JOIN VacateRequests v ON v.TenantId = t.Id AND v.IsDeleted = 0
WHERE t.Id IN ('7E570000-0000-0000-0004-00000000000A', '7E570000-0000-0000-0004-00000000000B', '7E570000-0000-0000-0004-00000000000E', '7E570000-0000-0000-0004-00000000000F',
               '7E570000-0000-0000-0004-00000000000C', '7E570000-0000-0000-0004-00000000000D')
ORDER BY h.HouseNumber;

SELECT h.HouseNumber, p.Year, p.Month, p.Amount, p.AmountPaid, p.Balance,
       CASE p.PaymentStatus WHEN 2 THEN 'Paid' WHEN 4 THEN 'Overdue' WHEN 5 THEN 'Failed' ELSE CAST(p.PaymentStatus AS varchar(5)) END AS Status,
       p.IsInitialPayment, CAST(p.DueDate AS date) AS DueDate
FROM Payments p
JOIN Houses h ON h.Id = p.HouseId
WHERE p.FlatId = '7E570000-0000-0000-0002-000000000001'
ORDER BY h.HouseNumber, p.Year, p.Month;
GO

SELECT h.HouseNumber, s.Id AS SettlementId, s.Direction, s.Amount, s.PaidAt, s.IsVoided, v.Status AS VacateStatus
FROM VacateSettlements s
JOIN VacateRequests v ON v.Id = s.VacateRequestId
JOIN Houses h ON h.Id = s.HouseId
WHERE s.FlatId = '7E570000-0000-0000-0002-000000000001'
ORDER BY h.HouseNumber, s.CreatedAt;
GO
