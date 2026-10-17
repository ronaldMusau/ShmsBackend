-- =====================================================
-- Smart Housing Management System - ANALYTICS test data
-- =====================================================
-- TEST DATA ONLY. Adds a second test property (under the existing TEST landlord, so the landlord login
-- test-landlord@shms-test.invalid sees it) with KNOWN numbers, so analytics can be checked to the shilling.
-- Re-runnable: nothing is inserted twice. Run AFTER seed-vacate-test-scenarios.sql.  Password: TestTenant123!
--
-- Flat  'TEST ANALYTICS PROPERTY - DO NOT USE'  management fee = 10%.  (TEST PROPERTY gets fee = 5%.)
-- Houses (rent): AN-1 8,000 | AN-2 10,000 | AN-3 12,000 | AN-4 15,000  (occupied)   AN-5 9,000 | AN-6 20,000 (vacant)
--   Expected monthly if every house were let : KES 74,000
--   Estimated monthly (occupied houses only) : KES 45,000     Vacant rent lost: KES 29,000
--   Occupancy: 4 of 6 houses = 66.7%  (AN-5 also carries an INACTIVE tenant record that must NOT count as occupied)
-- Each rent payment = rent + KES 200 service charge.
--   Aug : all 4 paid in full                                 -> rent 45,000   service charge 800
--   Sep : AN-1, AN-2 paid; AN-3 PARTIAL (6,000 of 12,200); AN-4 OVERDUE (15,200)
--   Oct : AN-1 paid TODAY 10:00 UTC; AN-2 paid TODAY exactly 00:00 UTC (date-boundary edge); AN-3 paid; AN-4 OVERDUE (15,200)
--   => Rent collected (this flat, Aug-Oct) = 98,901.64   Service charge collected = 1,898.36
--   => Management fee (10%) = 9,890.16    Net to landlord before expenses/deductions = 89,011.48
--   Overdue outstanding = 30,400 (2 payments)
-- Landlord-logged expenses (this flat): 3,000 (Aug 10) + 4,500 (Sep 12) + 1,500 (Oct 5) = 9,000
-- Management-logged expense (LandlordId NULL, must NOT appear for the landlord): 2,000 (Oct 6)
-- Deductions (this flat): 2,500 (Sep 20) + 1,800 (Oct 8) = 4,300
-- =====================================================

USE [shms_database];
SET QUOTED_IDENTIFIER ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LandlordId uniqueidentifier = '7E570000-0000-0000-0001-000000000001';
DECLARE @Flat1Id    uniqueidentifier = '7E570000-0000-0000-0002-000000000001';
DECLARE @FlatId     uniqueidentifier = '7E570000-0000-0000-0002-0000000000A2';
DECLARE @PasswordHash nvarchar(255) = '$2a$11$O5VcFJfBdUln78vNWTQID.9Mo.53528tvXMjqNeQ3ctqZqBmsmEwW';
DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @HouseTypeId uniqueidentifier = (SELECT TOP 1 Id FROM HouseTypes WHERE Name = 'Single Room' AND IsDeleted = 0);

IF NOT EXISTS (SELECT 1 FROM Landlords WHERE Id = @LandlordId)
BEGIN PRINT 'Run seed-vacate-test-scenarios.sql first (test landlord missing).'; RETURN; END
IF @HouseTypeId IS NULL
BEGIN PRINT 'No "Single Room" house type found.'; RETURN; END

-- Management fee on the original test flat too (idempotent)
UPDATE Flats SET ManagementFeePercentage = 5 WHERE Id = @Flat1Id AND ManagementFeePercentage IS NULL;

IF EXISTS (SELECT 1 FROM Flats WHERE Id = @FlatId)
BEGIN
    PRINT 'Analytics test data already seeded - nothing inserted.';
END
ELSE
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO Flats (Id, FlatName, LandlordId, RentDueDay, BillableGracePeriodMonths, VacateNoticeDeadlineDay,
                           SitDeposit, RewardEnabled, ManagementFeePercentage, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@FlatId, N'TEST ANALYTICS PROPERTY - DO NOT USE', @LandlordId, 5, 3, 15, 0, 0, 10, @Now, @Now, 0);

        DECLARE @H1 uniqueidentifier='7E570000-0000-0000-0003-0000000000A1', @H2 uniqueidentifier='7E570000-0000-0000-0003-0000000000A2', @H3 uniqueidentifier='7E570000-0000-0000-0003-0000000000A3',
                @H4 uniqueidentifier='7E570000-0000-0000-0003-0000000000A4', @H5 uniqueidentifier='7E570000-0000-0000-0003-0000000000A5', @H6 uniqueidentifier='7E570000-0000-0000-0003-0000000000A6';
        INSERT INTO Houses (Id, HouseNumber, HouseTypeId, RentFee, DepositFee, Description,
                            OccupancyStatus, PaymentStatus, FlatId, CreatedAt, UpdatedAt,
                            IsListingHidden, CommentsMuted, IsAwaitingExistingTenant, IsDeleted)
        VALUES
            (@H1,'AN-1',@HouseTypeId, 8000, 8000,'TEST analytics','Occupied','Paid',   @FlatId,@Now,@Now,1,0,0,0),
            (@H2,'AN-2',@HouseTypeId,10000,10000,'TEST analytics','Occupied','Paid',   @FlatId,@Now,@Now,1,0,0,0),
            (@H3,'AN-3',@HouseTypeId,12000,12000,'TEST analytics','Occupied','Paid',   @FlatId,@Now,@Now,1,0,0,0),
            (@H4,'AN-4',@HouseTypeId,15000,15000,'TEST analytics','Occupied','Overdue',@FlatId,@Now,@Now,1,0,0,0),
            (@H5,'AN-5',@HouseTypeId, 9000, 9000,'TEST analytics','Vacant',  'NotPaid',@FlatId,@Now,@Now,1,0,0,0),
            (@H6,'AN-6',@HouseTypeId,20000,20000,'TEST analytics','Vacant',  'NotPaid',@FlatId,@Now,@Now,1,0,0,0);

        DECLARE @T1 uniqueidentifier='7E570000-0000-0000-0004-0000000000A1', @T2 uniqueidentifier='7E570000-0000-0000-0004-0000000000A2', @T3 uniqueidentifier='7E570000-0000-0000-0004-0000000000A3',
                @T4 uniqueidentifier='7E570000-0000-0000-0004-0000000000A4', @T5 uniqueidentifier='7E570000-0000-0000-0004-0000000000A5';
        INSERT INTO PortalUsers (Id, Email, PasswordHash, FirstName, LastName, PhoneNumber, IsActive, IsEmailVerified, PortalUserType, CreatedAt, UpdatedAt)
        VALUES
            (@T1,'analytics-t1@shms-test.invalid',@PasswordHash,'Analytics','One',  '254700000101',1,1,2,@Now,@Now),
            (@T2,'analytics-t2@shms-test.invalid',@PasswordHash,'Analytics','Two',  '254700000102',1,1,2,@Now,@Now),
            (@T3,'analytics-t3@shms-test.invalid',@PasswordHash,'Analytics','Three','254700000103',1,1,2,@Now,@Now),
            (@T4,'analytics-t4@shms-test.invalid',@PasswordHash,'Analytics','Four', '254700000104',1,1,2,@Now,@Now),
            (@T5,'analytics-t5@shms-test.invalid',@PasswordHash,'Analytics','MovedOut','254700000105',1,1,2,@Now,@Now);
        INSERT INTO Tenants (Id, HouseId, TenantStatus, HasCompletedInitialPayment, TenancyCycle, LeaseStartMonth, LeaseStartYear, PointsBalance)
        VALUES
            (@T1,@H1,2,1,1,8,2026,0),(@T2,@H2,2,1,1,8,2026,0),(@T3,@H3,2,1,1,8,2026,0),(@T4,@H4,2,1,1,8,2026,0),
            (@T5,@H5,0 /* Inactive - must not count as occupied */,0,1,1,2026,0);

        -- Payments (Status: 2 Paid, 3 PartiallyPaid, 4 Overdue; Type 1 Rent; Method 0 Mpesa)
        DECLARE @P TABLE (Id uniqueidentifier, TenantId uniqueidentifier, HouseId uniqueidentifier, Mo int, Yr int,
                          Amount decimal(18,2), AmountPaid decimal(18,2), Status int, RentAmount decimal(18,2), PaidAt datetime2 NULL);
        INSERT INTO @P VALUES
            ('7E570000-0000-0000-0006-00000000A001', @T1, @H1, 8, 2026, 8200, 8200, 2, 8000, '2026-08-05T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A002', @T2, @H2, 8, 2026, 10200, 10200, 2, 10000, '2026-08-05T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A003', @T3, @H3, 8, 2026, 12200, 12200, 2, 12000, '2026-08-05T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A004', @T4, @H4, 8, 2026, 15200, 15200, 2, 15000, '2026-08-05T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A005', @T1, @H1, 9, 2026, 8200, 8200, 2, 8000, '2026-09-04T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A006', @T2, @H2, 9, 2026, 10200, 10200, 2, 10000, '2026-09-06T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A007', @T3, @H3, 9, 2026, 12200, 6000, 3, 12000, '2026-09-10T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A008', @T4, @H4, 9, 2026, 15200, 0, 4, 15000, NULL),
            ('7E570000-0000-0000-0006-00000000A009', @T1, @H1, 10, 2026, 8200, 8200, 2, 8000, '2026-10-17T10:00:00'),
            ('7E570000-0000-0000-0006-00000000A00A', @T2, @H2, 10, 2026, 10200, 10200, 2, 10000, '2026-10-17T00:00:00'),
            ('7E570000-0000-0000-0006-00000000A00B', @T3, @H3, 10, 2026, 12200, 12200, 2, 12000, '2026-10-03T08:00:00'),
            ('7E570000-0000-0000-0006-00000000A00C', @T4, @H4, 10, 2026, 15200, 0, 4, 15000, NULL);

        INSERT INTO Payments (Id, TenantId, HouseId, FlatId, LandlordId, Amount, AmountPaid, Balance, RentAmount, ServiceChargeAmount,
                              PaymentStatus, PaymentType, PaymentMethod, MpesaReceiptNumber, DueDate, PaidAt, Month, Year, RetryCount,
                              Description, Notes, IsInitialPayment, TenancyCycle, IsDeleted, CreatedAt, UpdatedAt)
        SELECT p.Id, p.TenantId, p.HouseId, @FlatId, @LandlordId, p.Amount, p.AmountPaid, p.Amount - p.AmountPaid, p.RentAmount, 200,
               p.Status, 1, CASE WHEN p.AmountPaid > 0 THEN 0 END,
               CASE WHEN p.AmountPaid > 0 THEN 'ANL' + RIGHT(REPLACE(CAST(p.Id AS varchar(36)), '-', ''), 7) END,
               DATEFROMPARTS(p.Yr, p.Mo, 5), p.PaidAt, p.Mo, p.Yr, 0,
               'TEST - Analytics rent ' + DATENAME(month, DATEFROMPARTS(p.Yr, p.Mo, 1)) + ' ' + CAST(p.Yr AS varchar(4)),
               'TEST SEED - seed-analytics-test-data.sql', 0, 1, 0, @Now, @Now
        FROM @P p;

        -- Expenses: three landlord-logged on this flat + one Management-logged (LandlordId NULL)
        INSERT INTO Expenses (Id, LandlordId, CreatedByUserId, FlatId, HouseId, Amount, Description, ExpenseDate, IsDeleted, CreatedAt, UpdatedAt)
        VALUES
            ('7E570000-0000-0000-0008-0000000000A1', @LandlordId, @LandlordId, @FlatId, NULL, 3000, 'TEST - Plumbing repair',       '2026-08-10', 0, @Now, @Now),
            ('7E570000-0000-0000-0008-0000000000A2', @LandlordId, @LandlordId, @FlatId, NULL, 4500, 'TEST - Painting common areas', '2026-09-12', 0, @Now, @Now),
            ('7E570000-0000-0000-0008-0000000000A3', @LandlordId, @LandlordId, @FlatId, NULL, 1500, 'TEST - Security lights',       '2026-10-05', 0, @Now, @Now),
            ('7E570000-0000-0000-0008-0000000000A4', NULL,        @LandlordId, @FlatId, NULL, 2000, 'TEST - Management-logged expense (landlord must not see)', '2026-10-06', 0, @Now, @Now);

        -- Deductions (need a complaint id: borrow an existing TEST complaint)
        DECLARE @ComplaintId uniqueidentifier = (SELECT TOP 1 Id FROM Complaints WHERE FlatId = @Flat1Id ORDER BY CreatedAt);
        IF @ComplaintId IS NOT NULL
            INSERT INTO Deductions (Id, LandlordId, TenantId, HouseId, FlatId, ComplaintId, Amount, Description, DeductionMonth, DeductionYear, CreatedAt)
            VALUES
                ('7E570000-0000-0000-0009-0000000000A1', @LandlordId, @T1, @H1, @FlatId, @ComplaintId, 2500, 'TEST - analytics deduction Sep', 9, 2026, '2026-09-20'),
                ('7E570000-0000-0000-0009-0000000000A2', @LandlordId, @T2, @H2, @FlatId, @ComplaintId, 1800, 'TEST - analytics deduction Oct', 10, 2026, '2026-10-08');
        ELSE PRINT 'No TEST complaint found - deductions skipped.';

        COMMIT TRANSACTION;
        PRINT 'Analytics test data seeded.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        PRINT 'Seed failed: ' + ERROR_MESSAGE();
        THROW;
    END CATCH
END
