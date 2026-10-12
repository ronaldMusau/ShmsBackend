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
--   TEST-G  Test Tenant AppealDenied      test-tenant-appealdenied@shms-test.invalid       (Approved, appeal Denied)
--   TEST-H  Test Tenant AppealResubmitted test-tenant-appealresubmitted@shms-test.invalid  (AwaitingApproval, appeal Resubmitted)
--   TEST-I  Test Tenant MultiOverdue      test-tenant-multioverdue@shms-test.invalid       (Aug/Sept/Oct overdue, no vacate)
--
-- Every row uses a fixed GUID starting 7E570000-0000-0000- ("TEST"), grouped by the 4th block:
--   0001 = landlord, 0002 = flat, 0003 = houses, 0004 = tenants,
--   0005 = vacate requests, 0006 = payments, 0007 = vacate settlements, 000C = Vacate approval step, 000E = pending rent changes, 000F = tenant house history.
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

-- =====================================================
-- TEST-E appeal: gives TEST-E's UnderAppeal request the appeal it represents — two inspection lines and an
-- UNRESOLVED appeal (Outcome NULL) disputing both — so deny-appeal can be exercised on it. New rows only; the
-- existing TEST-E request and settlement are not touched.
-- =====================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @VacateE uniqueidentifier = '7E570000-0000-0000-0005-00000000000E';
DECLARE @AppealE uniqueidentifier = '7E570000-0000-0000-0009-00000000000E';
DECLARE @LineE1  uniqueidentifier = '7E570000-0000-0000-0008-000000000E01';
DECLARE @LineE2  uniqueidentifier = '7E570000-0000-0000-0008-000000000E02';

IF NOT EXISTS (SELECT 1 FROM VacateRequests WHERE Id = @VacateE)
BEGIN
    PRINT '❌ TEST-E vacate request not found — the TEST-E/F block must seed first.';
END
ELSE IF EXISTS (SELECT 1 FROM VacateAppeals WHERE VacateRequestId = @VacateE)
     OR EXISTS (SELECT 1 FROM VacateInspectionLines WHERE Id IN (@LineE1, @LineE2))
BEGIN
    PRINT '⚠️ TEST-E appeal already seeded — nothing inserted.';
END
ELSE
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO VacateInspectionLines (Id, VacateRequestId, Description, AssessedAmount, LineOrder, CreatedAt)
        VALUES (@LineE1, @VacateE, N'Stained carpet in the bedroom — professional cleaning', 2000, 1, '2026-09-28T14:00:00'),
               (@LineE2, @VacateE, N'Broken towel rail in the bathroom', 3000, 2, '2026-09-28T14:10:00');

        INSERT INTO VacateAppeals (Id, VacateRequestId, SubmittedAt, Outcome, ResolvedAt, ResolvedByAdminId, Response)
        VALUES (@AppealE, @VacateE, '2026-10-06T09:30:00', NULL, NULL, NULL, NULL);

        INSERT INTO VacateAppealLines (Id, VacateAppealId, VacateInspectionLineId, Reason)
        VALUES ('7E570000-0000-0000-000A-000000000E01', @AppealE, @LineE1, N'The stain was there at move-in and is on my move-in checklist.'),
               ('7E570000-0000-0000-000A-000000000E02', @AppealE, @LineE2, N'The towel rail was already loose; 3,000 is more than a replacement costs.');

        COMMIT TRANSACTION;
        PRINT '✅ TEST-E appeal seeded.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        PRINT '❌ TEST-E appeal seeding failed — nothing inserted: ' + ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

-- =====================================================
-- Vacate approval sequence (configuration, not a test scenario): one step, the seeded SuperAdmin as approver,
-- matching the existing Complaints / FlatEdit sequences. Approvers are specific Admins ids (no role column).
-- Only inserted when no Vacate step exists at all, so a sequence configured through the UI is never touched.
-- =====================================================
SET NOCOUNT ON;

DECLARE @VacateApproverId uniqueidentifier = '372267DF-7926-4343-9633-8458385AC971';

IF EXISTS (SELECT 1 FROM ApprovalSequenceSteps WHERE Module = 'Vacate')
BEGIN
    PRINT '⚠️ Vacate approval sequence already configured — nothing inserted.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM Admins WHERE Id = @VacateApproverId AND IsActive = 1)
BEGIN
    PRINT '❌ SuperAdmin 372267DF-7926-4343-9633-8458385AC971 not found or inactive — Vacate approval sequence not seeded.';
END
ELSE
BEGIN
    INSERT INTO ApprovalSequenceSteps (Id, Module, StepOrder, ApproverId, CreatedAt)
    VALUES ('7E570000-0000-0000-000C-000000000001', 'Vacate', 1, @VacateApproverId, SYSUTCDATETIME());
    PRINT '✅ Vacate approval sequence seeded (step 1: SuperAdmin).';
END
GO

-- =====================================================
-- TEST-G: Approved vacate, live unpaid settlement, appeal DENIED (with inspection lines + one real photo)
-- G/H/I aren't hex digits, so their GUIDs end in 10 / 11 / 12 instead of a letter.
-- 0008 = inspection lines, 0009 = appeals, 000A = appeal lines, 000B = inspection-line attachments.
-- The photo row needs this file on disk (generated alongside this script; it's served by UseStaticFiles):
--   src/ShmsBackend.Api/wwwroot/vacate-attachments/7e570000-0000-0000-0005-000000000010/7e570000-0000-0000-000b-000000001001.png
-- =====================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LandlordId uniqueidentifier = '7E570000-0000-0000-0001-000000000001';
DECLARE @FlatId     uniqueidentifier = '7E570000-0000-0000-0002-000000000001';
DECLARE @HouseG   uniqueidentifier = '7E570000-0000-0000-0003-000000000010';
DECLARE @TenantG  uniqueidentifier = '7E570000-0000-0000-0004-000000000010';
DECLARE @VacateG  uniqueidentifier = '7E570000-0000-0000-0005-000000000010';
DECLARE @AppealG  uniqueidentifier = '7E570000-0000-0000-0009-000000000010';
DECLARE @LineG1   uniqueidentifier = '7E570000-0000-0000-0008-000000001001';
DECLARE @LineG2   uniqueidentifier = '7E570000-0000-0000-0008-000000001002';
DECLARE @LineG3   uniqueidentifier = '7E570000-0000-0000-0008-000000001003';

DECLARE @PasswordHash nvarchar(255) = '$2a$11$O5VcFJfBdUln78vNWTQID.9Mo.53528tvXMjqNeQ3ctqZqBmsmEwW';
DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @HouseTypeId uniqueidentifier = (SELECT TOP 1 Id FROM HouseTypes WHERE Name = 'Single Room' AND IsDeleted = 0);
-- Any existing admin (prefers the seeded SuperAdmin) for ResolvedByAdminId / FinalRemarksByAdminId.
DECLARE @AdminId uniqueidentifier = COALESCE(
    (SELECT Id FROM Admins WHERE Id = '372267DF-7926-4343-9633-8458385AC971'),
    (SELECT TOP 1 Id FROM Admins ORDER BY CreatedAt));

IF NOT EXISTS (SELECT 1 FROM Flats WHERE Id = @FlatId)
BEGIN
    PRINT '❌ Test property not found — the TEST-A..D block must seed first.';
END
ELSE IF EXISTS (SELECT 1 FROM PortalUsers WHERE Id = @TenantG) OR EXISTS (SELECT 1 FROM Houses WHERE Id = @HouseG)
BEGIN
    PRINT '⚠️ TEST-G scenario already seeded — nothing inserted.';
END
ELSE
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO Houses (Id, HouseNumber, HouseTypeId, RentFee, DepositFee, Description,
                            OccupancyStatus, PaymentStatus, FlatId, CreatedAt, UpdatedAt,
                            IsListingHidden, CommentsMuted, IsAwaitingExistingTenant, IsDeleted)
        VALUES (@HouseG, 'TEST-G', @HouseTypeId, 5000, 5000, 'TEST — appeal denied, original settlement stands',
                'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0);

        INSERT INTO PortalUsers (Id, Email, PasswordHash, FirstName, LastName, PhoneNumber,
                                 IsActive, IsEmailVerified, PortalUserType, CreatedAt, UpdatedAt)
        VALUES (@TenantG, 'test-tenant-appealdenied@shms-test.invalid', @PasswordHash, 'Test Tenant', 'AppealDenied',
                '254700000007', 1, 1, 2 /* Tenant */, @Now, @Now);

        INSERT INTO Tenants (Id, HouseId, TenantStatus, HasCompletedInitialPayment, TenancyCycle,
                             LeaseStartMonth, LeaseStartYear, PointsBalance)
        VALUES (@TenantG, @HouseG, 2 /* Active */, 1, 1, 8, 2026, 0);

        -- Same base as TEST-A: August initial payment paid; Sept + Oct rent overdue.
        INSERT INTO Payments (Id, TenantId, HouseId, FlatId, LandlordId,
                              Amount, AmountPaid, Balance, RentAmount, DepositAmount,
                              PaymentStatus, PaymentType, PaymentMethod, MpesaReceiptNumber,
                              DueDate, PaidAt, Month, Year, RetryCount, Description, Notes,
                              IsInitialPayment, TenancyCycle, IsDeleted, CreatedAt, UpdatedAt)
        SELECT CAST('7E570000-0000-0000-0006-000010' + CAST(m.Yr * 100 + m.Mo AS varchar(6)) AS uniqueidentifier),
               @TenantG, @HouseG, @FlatId, @LandlordId,
               m.Amount, m.AmountPaid, m.Amount - m.AmountPaid, 5000, m.DepositAmount,
               m.Status, m.PayType,
               CASE WHEN m.AmountPaid > 0 THEN 0 END,
               CASE WHEN m.AmountPaid > 0 THEN 'TESTG' + CAST(m.Yr * 100 + m.Mo AS varchar(6)) END,
               DATEFROMPARTS(m.Yr, m.Mo, 5),
               CASE WHEN m.AmountPaid > 0 THEN CAST('2026-08-01' AS datetime2) END,
               m.Mo, m.Yr, 0,
               CASE WHEN m.IsInitial = 1 THEN 'TEST — Initial payment (deposit + first rent)'
                    ELSE 'TEST — Rent ' + DATENAME(month, DATEFROMPARTS(m.Yr, m.Mo, 1)) + ' ' + CAST(m.Yr AS varchar(4)) END,
               'TEST SEED — seed-vacate-test-scenarios.sql',
               m.IsInitial, 1, 0, @Now, @Now
        FROM (VALUES ( 8, 2026, 10000, 10000, 2, 0, 1, 5000),
                     ( 9, 2026,  5000,     0, 4, 1, 0, NULL),
                     (10, 2026,  5000,     0, 4, 1, 0, NULL)) m(Mo, Yr, Amount, AmountPaid, Status, PayType, IsInitial, DepositAmount);

        -- Approved once (final remarks issued), then appealed, then the appeal was denied → back to Approved.
        INSERT INTO VacateRequests (Id, TenantId, TenancyCycle, HouseId, FlatId, LandlordId,
                                    Status, NeedsResubmission, ApprovalAttemptNumber, CurrentApprovalStepOrder,
                                    VacateMonth, VacateYear, SitDeposit, ClosedAt,
                                    InspectionAssignedAt, InspectionSubmittedAt,
                                    FinalRemarks, FinalRemarksAt, FinalRemarksByAdminId,
                                    ReviewedAt, Reason, InitiationType, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@VacateG, @TenantG, 1, @HouseG, @FlatId, @LandlordId,
                'Approved', 0, 1, NULL,
                12, 2026, 0, NULL,
                '2026-09-25T08:00:00', '2026-09-28T15:00:00',
                N'TEST — Inspection reviewed; damages assessed as listed.', '2026-10-02T09:00:00', @AdminId,
                '2026-10-02T09:00:00', 'TEST — appeal denied scenario', 'Voluntary', @Now, @Now, 0);

        INSERT INTO VacateInspectionLines (Id, VacateRequestId, Description, AssessedAmount, LineOrder, CreatedAt)
        VALUES (@LineG1, @VacateG, N'Scuffed and marked wall paint in the living room — full repaint', 3500, 1, '2026-09-28T14:00:00'),
               (@LineG2, @VacateG, N'Kitchen cabinet door hinge broken off', 2000, 2, '2026-09-28T14:10:00'),
               (@LineG3, @VacateG, N'Window keys missing (2)', 1500, 3, '2026-09-28T14:20:00');

        INSERT INTO VacateInspectionLineAttachments (Id, VacateInspectionLineId, VacateRequestId, FileType, FilePath, FileSizeBytes, UploadedAt)
        VALUES ('7E570000-0000-0000-000B-000000001001', @LineG1, @VacateG, 'Image',
                '/vacate-attachments/7e570000-0000-0000-0005-000000000010/7e570000-0000-0000-000b-000000001001.png',
                5547, '2026-09-28T14:05:00');

        -- Illustrative amount (damages 7,000 less the 5,000 deposit), not a CalculateVacateSettlementAsync result.
        INSERT INTO VacateSettlements (Id, VacateRequestId, LandlordId, TenantId, HouseId, FlatId,
                                       Direction, Amount, Description, PaidAt, CreatedAt, IsVoided)
        VALUES ('7E570000-0000-0000-0007-000000000010', @VacateG, @LandlordId, @TenantG, @HouseG, @FlatId,
                'TenantOwes', 2000, 'TEST — damages KES 7,000 less deposit KES 5,000 (unpaid)', NULL, '2026-10-02T09:00:00', 0);

        INSERT INTO VacateAppeals (Id, VacateRequestId, SubmittedAt, Outcome, ResolvedAt, ResolvedByAdminId, Response)
        VALUES (@AppealG, @VacateG, '2026-10-05T10:00:00', 'Denied', '2026-10-08T14:30:00', @AdminId,
                N'We reviewed the inspection photos and the agent''s report. The repaint and cabinet repair reflect the contractor''s quotes, so the original assessment stands.');

        INSERT INTO VacateAppealLines (Id, VacateAppealId, VacateInspectionLineId, Reason)
        VALUES ('7E570000-0000-0000-000A-000000001001', @AppealG, @LineG1, N'The marks were already there when I moved in — I have move-in photos showing them.'),
               ('7E570000-0000-0000-000A-000000001002', @AppealG, @LineG2, N'The hinge wore out from normal use; it was loose for months and I reported it.'),
               ('7E570000-0000-0000-000A-000000001003', @AppealG, @LineG3, N'I handed both window keys to the caretaker on the inspection day.');

        COMMIT TRANSACTION;
        PRINT '✅ TEST-G scenario seeded.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        PRINT '❌ TEST-G seeding failed — nothing inserted: ' + ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

-- =====================================================
-- TEST-H: appeal RESUBMITTED — the request went back into approval (AwaitingApproval, attempt 2), the
-- settlement from the first approval is still live (resubmit doesn't touch it), and the appeal is marked
-- Resubmitted with no admin/response (exactly what ResubmitVacateRequest writes).
-- Photo file: src/ShmsBackend.Api/wwwroot/vacate-attachments/7e570000-0000-0000-0005-000000000011/7e570000-0000-0000-000b-000000001101.png
-- =====================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LandlordId uniqueidentifier = '7E570000-0000-0000-0001-000000000001';
DECLARE @FlatId     uniqueidentifier = '7E570000-0000-0000-0002-000000000001';
DECLARE @HouseH   uniqueidentifier = '7E570000-0000-0000-0003-000000000011';
DECLARE @TenantH  uniqueidentifier = '7E570000-0000-0000-0004-000000000011';
DECLARE @VacateH  uniqueidentifier = '7E570000-0000-0000-0005-000000000011';
DECLARE @AppealH  uniqueidentifier = '7E570000-0000-0000-0009-000000000011';
DECLARE @LineH1   uniqueidentifier = '7E570000-0000-0000-0008-000000001101';
DECLARE @LineH2   uniqueidentifier = '7E570000-0000-0000-0008-000000001102';
DECLARE @LineH3   uniqueidentifier = '7E570000-0000-0000-0008-000000001103';

DECLARE @PasswordHash nvarchar(255) = '$2a$11$O5VcFJfBdUln78vNWTQID.9Mo.53528tvXMjqNeQ3ctqZqBmsmEwW';
DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @HouseTypeId uniqueidentifier = (SELECT TOP 1 Id FROM HouseTypes WHERE Name = 'Single Room' AND IsDeleted = 0);
DECLARE @AdminId uniqueidentifier = COALESCE(
    (SELECT Id FROM Admins WHERE Id = '372267DF-7926-4343-9633-8458385AC971'),
    (SELECT TOP 1 Id FROM Admins ORDER BY CreatedAt));
-- First Vacate approval step, as a real resubmit would set it (falls back to 1 if none is configured).
DECLARE @FirstStep int = COALESCE((SELECT MIN(StepOrder) FROM ApprovalSequenceSteps WHERE Module = 'Vacate'), 1);

IF NOT EXISTS (SELECT 1 FROM Flats WHERE Id = @FlatId)
BEGIN
    PRINT '❌ Test property not found — the TEST-A..D block must seed first.';
END
ELSE IF EXISTS (SELECT 1 FROM PortalUsers WHERE Id = @TenantH) OR EXISTS (SELECT 1 FROM Houses WHERE Id = @HouseH)
BEGIN
    PRINT '⚠️ TEST-H scenario already seeded — nothing inserted.';
END
ELSE
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO Houses (Id, HouseNumber, HouseTypeId, RentFee, DepositFee, Description,
                            OccupancyStatus, PaymentStatus, FlatId, CreatedAt, UpdatedAt,
                            IsListingHidden, CommentsMuted, IsAwaitingExistingTenant, IsDeleted)
        VALUES (@HouseH, 'TEST-H', @HouseTypeId, 5000, 5000, 'TEST — appeal resubmitted for re-approval',
                'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0);

        INSERT INTO PortalUsers (Id, Email, PasswordHash, FirstName, LastName, PhoneNumber,
                                 IsActive, IsEmailVerified, PortalUserType, CreatedAt, UpdatedAt)
        VALUES (@TenantH, 'test-tenant-appealresubmitted@shms-test.invalid', @PasswordHash, 'Test Tenant', 'AppealResubmitted',
                '254700000008', 1, 1, 2 /* Tenant */, @Now, @Now);

        INSERT INTO Tenants (Id, HouseId, TenantStatus, HasCompletedInitialPayment, TenancyCycle,
                             LeaseStartMonth, LeaseStartYear, PointsBalance)
        VALUES (@TenantH, @HouseH, 2 /* Active */, 1, 1, 8, 2026, 0);

        INSERT INTO Payments (Id, TenantId, HouseId, FlatId, LandlordId,
                              Amount, AmountPaid, Balance, RentAmount, DepositAmount,
                              PaymentStatus, PaymentType, PaymentMethod, MpesaReceiptNumber,
                              DueDate, PaidAt, Month, Year, RetryCount, Description, Notes,
                              IsInitialPayment, TenancyCycle, IsDeleted, CreatedAt, UpdatedAt)
        SELECT CAST('7E570000-0000-0000-0006-000011' + CAST(m.Yr * 100 + m.Mo AS varchar(6)) AS uniqueidentifier),
               @TenantH, @HouseH, @FlatId, @LandlordId,
               m.Amount, m.AmountPaid, m.Amount - m.AmountPaid, 5000, m.DepositAmount,
               m.Status, m.PayType,
               CASE WHEN m.AmountPaid > 0 THEN 0 END,
               CASE WHEN m.AmountPaid > 0 THEN 'TESTH' + CAST(m.Yr * 100 + m.Mo AS varchar(6)) END,
               DATEFROMPARTS(m.Yr, m.Mo, 5),
               CASE WHEN m.AmountPaid > 0 THEN CAST('2026-08-01' AS datetime2) END,
               m.Mo, m.Yr, 0,
               CASE WHEN m.IsInitial = 1 THEN 'TEST — Initial payment (deposit + first rent)'
                    ELSE 'TEST — Rent ' + DATENAME(month, DATEFROMPARTS(m.Yr, m.Mo, 1)) + ' ' + CAST(m.Yr AS varchar(4)) END,
               'TEST SEED — seed-vacate-test-scenarios.sql',
               m.IsInitial, 1, 0, @Now, @Now
        FROM (VALUES ( 8, 2026, 10000, 10000, 2, 0, 1, 5000),
                     ( 9, 2026,  5000,     0, 4, 1, 0, NULL),
                     (10, 2026,  5000,     0, 4, 1, 0, NULL)) m(Mo, Yr, Amount, AmountPaid, Status, PayType, IsInitial, DepositAmount);

        -- First approval's FinalRemarks stay as they were — resubmit doesn't clear them.
        INSERT INTO VacateRequests (Id, TenantId, TenancyCycle, HouseId, FlatId, LandlordId,
                                    Status, NeedsResubmission, ApprovalAttemptNumber, CurrentApprovalStepOrder,
                                    VacateMonth, VacateYear, SitDeposit, ClosedAt,
                                    InspectionAssignedAt, InspectionSubmittedAt,
                                    FinalRemarks, FinalRemarksAt, FinalRemarksByAdminId,
                                    ReviewedAt, Reason, InitiationType, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@VacateH, @TenantH, 1, @HouseH, @FlatId, @LandlordId,
                'AwaitingApproval', 0, 2, @FirstStep,
                12, 2026, 0, NULL,
                '2026-09-25T08:00:00', '2026-09-28T15:00:00',
                N'TEST — Inspection reviewed; damages assessed as listed.', '2026-10-02T09:00:00', @AdminId,
                '2026-10-02T09:00:00', 'TEST — appeal resubmitted scenario', 'Voluntary', @Now, @Now, 0);

        INSERT INTO VacateInspectionLines (Id, VacateRequestId, Description, AssessedAmount, LineOrder, CreatedAt)
        VALUES (@LineH1, @VacateH, N'Cracked floor tile in the bathroom — replace 2 tiles', 2500, 1, '2026-09-28T14:00:00'),
               (@LineH2, @VacateH, N'Front door lock damaged — replace lock set', 3000, 2, '2026-09-28T14:10:00'),
               (@LineH3, @VacateH, N'Bedroom curtain rail pulled out of the wall', 1200, 3, '2026-09-28T14:20:00');

        INSERT INTO VacateInspectionLineAttachments (Id, VacateInspectionLineId, VacateRequestId, FileType, FilePath, FileSizeBytes, UploadedAt)
        VALUES ('7E570000-0000-0000-000B-000000001101', @LineH1, @VacateH, 'Image',
                '/vacate-attachments/7e570000-0000-0000-0005-000000000011/7e570000-0000-0000-000b-000000001101.png',
                5626, '2026-09-28T14:05:00');

        -- From the first approval; still live because resubmit leaves the settlement alone.
        -- Illustrative amount (damages 6,700 less the 5,000 deposit).
        INSERT INTO VacateSettlements (Id, VacateRequestId, LandlordId, TenantId, HouseId, FlatId,
                                       Direction, Amount, Description, PaidAt, CreatedAt, IsVoided)
        VALUES ('7E570000-0000-0000-0007-000000000011', @VacateH, @LandlordId, @TenantH, @HouseH, @FlatId,
                'TenantOwes', 1700, 'TEST — damages KES 6,700 less deposit KES 5,000 (unpaid)', NULL, '2026-10-02T09:00:00', 0);

        INSERT INTO VacateAppeals (Id, VacateRequestId, SubmittedAt, Outcome, ResolvedAt, ResolvedByAdminId, Response)
        VALUES (@AppealH, @VacateH, '2026-10-05T10:00:00', 'Resubmitted', '2026-10-07T11:00:00', NULL, NULL);

        INSERT INTO VacateAppealLines (Id, VacateAppealId, VacateInspectionLineId, Reason)
        VALUES ('7E570000-0000-0000-000A-000000001101', @AppealH, @LineH1, N'The tile cracked when the building settled — other units have the same crack.'),
               ('7E570000-0000-0000-000A-000000001102', @AppealH, @LineH2, N'The lock was stiff from the start; a quote of 3,000 is far above a normal lock set.'),
               ('7E570000-0000-0000-000A-000000001103', @AppealH, @LineH3, N'The rail was only held by two screws into soft plaster and fell on its own.');

        COMMIT TRANSACTION;
        PRINT '✅ TEST-H scenario seeded.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        PRINT '❌ TEST-H seeding failed — nothing inserted: ' + ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

-- =====================================================
-- TEST-I: several overdue months, no vacate — for checking that a self-pay amount lands on the oldest row
-- and cascades forward. Moved in July 2026 (initial payment paid); August, September and October are all
-- overdue with different balances: Aug 3,000 left (2,000 paid), Sept 5,000 (nothing paid), Oct 3,500 left (1,500 paid).
-- =====================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LandlordId uniqueidentifier = '7E570000-0000-0000-0001-000000000001';
DECLARE @FlatId     uniqueidentifier = '7E570000-0000-0000-0002-000000000001';
DECLARE @HouseI   uniqueidentifier = '7E570000-0000-0000-0003-000000000012';
DECLARE @TenantI  uniqueidentifier = '7E570000-0000-0000-0004-000000000012';

DECLARE @PasswordHash nvarchar(255) = '$2a$11$O5VcFJfBdUln78vNWTQID.9Mo.53528tvXMjqNeQ3ctqZqBmsmEwW';
DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @HouseTypeId uniqueidentifier = (SELECT TOP 1 Id FROM HouseTypes WHERE Name = 'Single Room' AND IsDeleted = 0);

IF NOT EXISTS (SELECT 1 FROM Flats WHERE Id = @FlatId)
BEGIN
    PRINT '❌ Test property not found — the TEST-A..D block must seed first.';
END
ELSE IF EXISTS (SELECT 1 FROM PortalUsers WHERE Id = @TenantI) OR EXISTS (SELECT 1 FROM Houses WHERE Id = @HouseI)
BEGIN
    PRINT '⚠️ TEST-I scenario already seeded — nothing inserted.';
END
ELSE
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO Houses (Id, HouseNumber, HouseTypeId, RentFee, DepositFee, Description,
                            OccupancyStatus, PaymentStatus, FlatId, CreatedAt, UpdatedAt,
                            IsListingHidden, CommentsMuted, IsAwaitingExistingTenant, IsDeleted)
        VALUES (@HouseI, 'TEST-I', @HouseTypeId, 5000, 5000, 'TEST — multiple overdue months, oldest-first payment',
                'Occupied', 'Overdue', @FlatId, @Now, @Now, 1, 0, 0, 0);

        INSERT INTO PortalUsers (Id, Email, PasswordHash, FirstName, LastName, PhoneNumber,
                                 IsActive, IsEmailVerified, PortalUserType, CreatedAt, UpdatedAt)
        VALUES (@TenantI, 'test-tenant-multioverdue@shms-test.invalid', @PasswordHash, 'Test Tenant', 'MultiOverdue',
                '254700000009', 1, 1, 2 /* Tenant */, @Now, @Now);

        INSERT INTO Tenants (Id, HouseId, TenantStatus, HasCompletedInitialPayment, TenancyCycle,
                             LeaseStartMonth, LeaseStartYear, PointsBalance)
        VALUES (@TenantI, @HouseI, 2 /* Active */, 1, 1, 7, 2026, 0);

        -- Status: 2 Paid, 4 Overdue (partially-paid rows past their due date are Overdue too).
        INSERT INTO Payments (Id, TenantId, HouseId, FlatId, LandlordId,
                              Amount, AmountPaid, Balance, RentAmount, DepositAmount,
                              PaymentStatus, PaymentType, PaymentMethod, MpesaReceiptNumber,
                              DueDate, PaidAt, Month, Year, RetryCount, Description, Notes,
                              IsInitialPayment, TenancyCycle, IsDeleted, CreatedAt, UpdatedAt)
        SELECT CAST('7E570000-0000-0000-0006-000012' + CAST(m.Yr * 100 + m.Mo AS varchar(6)) AS uniqueidentifier),
               @TenantI, @HouseI, @FlatId, @LandlordId,
               m.Amount, m.AmountPaid, m.Amount - m.AmountPaid, 5000, m.DepositAmount,
               m.Status, m.PayType,
               CASE WHEN m.AmountPaid > 0 THEN 0 END,
               CASE WHEN m.AmountPaid > 0 THEN 'TESTI' + CAST(m.Yr * 100 + m.Mo AS varchar(6)) END,
               DATEFROMPARTS(m.Yr, m.Mo, 5),
               CASE WHEN m.AmountPaid > 0 THEN DATEFROMPARTS(m.Yr, m.Mo, 3) END,
               m.Mo, m.Yr, 0,
               CASE WHEN m.IsInitial = 1 THEN 'TEST — Initial payment (deposit + first rent)'
                    ELSE 'TEST — Rent ' + DATENAME(month, DATEFROMPARTS(m.Yr, m.Mo, 1)) + ' ' + CAST(m.Yr AS varchar(4)) END,
               'TEST SEED — seed-vacate-test-scenarios.sql',
               m.IsInitial, 1, 0, @Now, @Now
        FROM (VALUES ( 7, 2026, 10000, 10000, 2, 0, 1, 5000),
                     ( 8, 2026,  5000,  2000, 4, 1, 0, NULL),
                     ( 9, 2026,  5000,     0, 4, 1, 0, NULL),
                     (10, 2026,  5000,  1500, 4, 1, 0, NULL)) m(Mo, Yr, Amount, AmountPaid, Status, PayType, IsInitial, DepositAmount);

        COMMIT TRANSACTION;
        PRINT '✅ TEST-I scenario seeded.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        PRINT '❌ TEST-I seeding failed — nothing inserted: ' + ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

-- =====================================================
-- TEST-I pending rent change: rent drops to 2,000 from December 2026 (unapplied). 2,000 sits in a different
-- service-charge tier (1,000–2,000 → 10) from today's 5,000 (3,000–7,000 → 20), so rows created for December
-- onward show both the new rent and a recomputed service charge.
-- =====================================================
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM Houses WHERE Id = '7E570000-0000-0000-0003-000000000012')
BEGIN
    PRINT '❌ TEST-I house not found — the TEST-I block must seed first.';
END
ELSE IF EXISTS (SELECT 1 FROM PendingRentChanges WHERE Id = '7E570000-0000-0000-000E-000000000012')
BEGIN
    PRINT '⚠️ TEST-I pending rent change already seeded — nothing inserted.';
END
ELSE
BEGIN
    INSERT INTO PendingRentChanges (Id, HouseId, NewRentFee, NewDepositFee, EffectiveMonth, EffectiveYear, CreatedAt, AppliedAt, CreatedByUserId)
    VALUES ('7E570000-0000-0000-000E-000000000012', '7E570000-0000-0000-0003-000000000012', 2000, 5000, 12, 2026,
            SYSUTCDATETIME(), NULL, '372267DF-7926-4343-9633-8458385AC971');
    PRINT '✅ TEST-I pending rent change seeded (2,000 from December 2026).';
END
GO

-- =====================================================
-- TEST-I tenant history: the open TenantHouseHistories row the app writes when a tenant moves in. Makes TEST-I
-- an "ever occupied" house, so HouseController.BulkPriceChange must refuse it (occupied-house price changes go
-- through the flat edit request flow). Other TEST houses still have no history.
-- =====================================================
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM Houses WHERE Id = '7E570000-0000-0000-0003-000000000012')
BEGIN
    PRINT '❌ TEST-I house not found — the TEST-I block must seed first.';
END
ELSE IF EXISTS (SELECT 1 FROM TenantHouseHistories WHERE Id = '7E570000-0000-0000-000F-000000000012')
BEGIN
    PRINT '⚠️ TEST-I tenant history already seeded — nothing inserted.';
END
ELSE
BEGIN
    INSERT INTO TenantHouseHistories (Id, HouseId, TenantId, TenantFirstName, TenantLastName, TenantEmail, TenantPhone,
                                      HouseNumber, FlatName, AssignedAt, RemovedAt, TenancyCycle)
    VALUES ('7E570000-0000-0000-000F-000000000012', '7E570000-0000-0000-0003-000000000012', '7E570000-0000-0000-0004-000000000012',
            'Test Tenant', 'MultiOverdue', 'test-tenant-multioverdue@shms-test.invalid', '254700000009',
            'TEST-I', N'TEST PROPERTY — DO NOT USE', '2026-07-01T09:00:00', NULL, 1);
    PRINT '✅ TEST-I tenant history seeded.';
END
GO

-- Show what's seeded
SELECT h.HouseNumber, pu.FirstName + ' ' + pu.LastName AS Tenant, pu.Email, t.PointsBalance,
       v.VacateMonth, v.VacateYear, v.Status AS VacateStatus
FROM Tenants t
JOIN PortalUsers pu ON pu.Id = t.Id
JOIN Houses h ON h.Id = t.HouseId
LEFT JOIN VacateRequests v ON v.TenantId = t.Id AND v.IsDeleted = 0
WHERE h.FlatId = '7E570000-0000-0000-0002-000000000001'
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

SELECT h.HouseNumber, a.Outcome, a.SubmittedAt, a.ResolvedAt, a.ResolvedByAdminId, a.Response,
       (SELECT COUNT(*) FROM VacateAppealLines l WHERE l.VacateAppealId = a.Id) AS AppealLines,
       (SELECT COUNT(*) FROM VacateInspectionLines il WHERE il.VacateRequestId = v.Id) AS InspectionLines,
       (SELECT COUNT(*) FROM VacateInspectionLineAttachments at WHERE at.VacateRequestId = v.Id) AS Photos,
       v.Status AS VacateStatus, v.ApprovalAttemptNumber
FROM VacateAppeals a
JOIN VacateRequests v ON v.Id = a.VacateRequestId
JOIN Houses h ON h.Id = v.HouseId
WHERE v.FlatId = '7E570000-0000-0000-0002-000000000001'
ORDER BY h.HouseNumber;
GO
