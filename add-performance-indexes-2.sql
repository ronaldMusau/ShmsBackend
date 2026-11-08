-- Second batch of indexes, chosen from perf-diagnostics.sql: foreign keys that had no index, plus columns used to look records up
-- (M-Pesa callback ids, "my records" lists, per-user history) and the notifications list.
-- Safe to run more than once: each index is created only if it is missing and the table/columns exist.
-- Run:  sqlcmd -I -S localhost -U sa -P "<password>" -C -d shms_database -i add-performance-indexes.sql  (-I is required: one index is filtered)
SET NOCOUNT ON;

DECLARE @ix TABLE (tbl sysname, name sysname, cols nvarchar(400), incl nvarchar(400) NULL);
INSERT INTO @ix VALUES
 ('ApprovalSequenceSteps','IX_ApprovalSequenceSteps_ApproverId','[ApproverId]',NULL),
 ('ComplaintApprovalActions','IX_ComplaintApprovalActions_ApproverId','[ApproverId]',NULL),
 ('ComplaintApprovalActions','IX_ComplaintApprovalActions_ComplaintId','[ComplaintId]',NULL),
 ('ComplaintAttachments','IX_ComplaintAttachments_ComplaintId','[ComplaintId]',NULL),
 ('Complaints','IX_Complaints_ComplaintTypeId','[ComplaintTypeId]',NULL),
 ('Complaints','IX_Complaints_FlatId','[FlatId]',NULL),
 ('Complaints','IX_Complaints_HouseId','[HouseId]',NULL),
 ('ComplaintStatusHistory','IX_ComplaintStatusHistory_ComplaintId','[ComplaintId]',NULL),
 ('ComplaintTypes','IX_ComplaintTypes_CreatedBy','[CreatedBy]',NULL),
 ('Deductions','IX_Deductions_ComplaintId','[ComplaintId]',NULL),
 ('FlatEditRequests','IX_FlatEditRequests_FlatId','[FlatId]',NULL),
 ('HouseListingComments','IX_HouseListingComments_HouseId','[HouseId]',NULL),
 ('Houses','IX_Houses_HouseTypeId','[HouseTypeId]',NULL),
 ('ListingViewingSessions','IX_ListingViewingSessions_HouseId','[HouseId]',NULL),
 ('PendingRentChanges','IX_PendingRentChanges_HouseId','[HouseId]',NULL),
 ('SessionMessages','IX_SessionMessages_SessionId','[SessionId]',NULL),
 ('VacateAppealLines','IX_VacateAppealLines_VacateAppealId','[VacateAppealId]',NULL),
 ('VacateAppealLines','IX_VacateAppealLines_VacateInspectionLineId','[VacateInspectionLineId]',NULL),
 ('VacateAppeals','IX_VacateAppeals_VacateRequestId','[VacateRequestId]',NULL),
 ('VacateApprovalActions','IX_VacateApprovalActions_VacateRequestId','[VacateRequestId]',NULL),
 ('VacateCheckoutAttempts','IX_VacateCheckoutAttempts_VacateSettlementId','[VacateSettlementId]',NULL),
 ('VacateForfeitedAdvances','IX_VacateForfeitedAdvances_VacateRequestId','[VacateRequestId]',NULL),
 ('VacateInspectionLineAttachments','IX_VacateInspectionLineAttachments_VacateInspectionLineId','[VacateInspectionLineId]',NULL),
 ('VacateInspectionLines','IX_VacateInspectionLines_VacateRequestId','[VacateRequestId]',NULL),
 ('VacateMessages','IX_VacateMessages_VacateRequestId','[VacateRequestId]',NULL),
 ('VacateSettlements','IX_VacateSettlements_VacateRequestId','[VacateRequestId]',NULL),
 ('Payments','IX_Payments_LandlordId','[LandlordId]',NULL),
 ('Payments','IX_Payments_MpesaTransactionId','[MpesaTransactionId]',NULL),
 ('Payments','IX_Payments_MerchantRequestId','[MerchantRequestId]',NULL),
 ('PaymentApplications','IX_PaymentApplications_CheckoutRequestId','[CheckoutRequestId]',NULL),
 ('VacateCheckoutAttempts','IX_VacateCheckoutAttempts_CheckoutRequestId','[CheckoutRequestId]',NULL),
 ('VacateRequests','IX_VacateRequests_TenantId','[TenantId]',NULL),
 ('VacateRequests','IX_VacateRequests_LandlordId','[LandlordId]',NULL),
 ('VacateRequests','IX_VacateRequests_FlatId','[FlatId]',NULL),
 ('VacateRequests','IX_VacateRequests_HouseId','[HouseId]',NULL),
 ('VacateRequests','IX_VacateRequests_AssignedAgentId','[AssignedAgentId]',NULL),
 ('VacateSettlements','IX_VacateSettlements_TenantId','[TenantId]',NULL),
 ('VacateSettlements','IX_VacateSettlements_LandlordId','[LandlordId]',NULL),
 ('VacateSettlements','IX_VacateSettlements_FlatId','[FlatId]',NULL),
 ('VacateSettlements','IX_VacateSettlements_HouseId','[HouseId]',NULL),
 ('VacateForfeitedAdvances','IX_VacateForfeitedAdvances_TenantId','[TenantId]',NULL),
 ('VacateForfeitedAdvances','IX_VacateForfeitedAdvances_LandlordId','[LandlordId]',NULL),
 ('VacateForfeitedAdvances','IX_VacateForfeitedAdvances_FlatId','[FlatId]',NULL),
 ('VacateForfeitedAdvances','IX_VacateForfeitedAdvances_HouseId','[HouseId]',NULL),
 ('Deductions','IX_Deductions_TenantId','[TenantId]',NULL),
 ('Deductions','IX_Deductions_FlatId','[FlatId]',NULL),
 ('Deductions','IX_Deductions_HouseId','[HouseId]',NULL),
 ('Expenses','IX_Expenses_HouseId','[HouseId]',NULL),
 ('TenantHouseHistories','IX_TenantHouseHistories_TenantId','[TenantId]',NULL),
 ('HouseListingBookmarks','IX_HouseListingBookmarks_ExplorerId','[ExplorerId]',NULL),
 ('HouseListingLikes','IX_HouseListingLikes_ExplorerId','[ExplorerId]',NULL),
 ('HouseListingRatings','IX_HouseListingRatings_ExplorerId','[ExplorerId]',NULL),
 ('HouseListingComments','IX_HouseListingComments_ExplorerId','[ExplorerId]',NULL),
 ('LoginActivities','IX_LoginActivities_UserId','[UserId]',NULL),
 ('PushSubscriptions','IX_PushSubscriptions_UserId','[UserId]',NULL),
 ('SecurityEvents','IX_SecurityEvents_ActorUserId','[ActorUserId]',NULL),
 ('ComplaintMessages','IX_ComplaintMessages_SenderUserId','[SenderUserId]',NULL),
 ('SessionMessages','IX_SessionMessages_SenderUserId','[SenderUserId]',NULL),
 ('VacateMessages','IX_VacateMessages_SenderUserId','[SenderUserId]',NULL),
 ('Admins','IX_Admins_NationalId','[NationalId]',NULL),
 ('PortalUsers','IX_PortalUsers_NationalId','[NationalId]',NULL),
 ('Complaints','IX_Complaints_EscalatedToAgentId','[EscalatedToAgentId]',NULL),
 ('ExplorerInterests','IX_ExplorerInterests_SessionId','[SessionId]',NULL),
 ('Notifications','IX_Notifications_Target_Audience_Deleted_CreatedAt','[TargetUserId],[Audience],[IsDeleted],[CreatedAt] DESC','[IsRead]');

DECLARE @t sysname, @n sysname, @c nvarchar(400), @i nvarchar(400), @sql nvarchar(max);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT tbl, name, cols, incl FROM @ix;
OPEN cur; FETCH NEXT FROM cur INTO @t, @n, @c, @i;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID('dbo.' + @t) IS NULL
        PRINT 'skip (no table): ' + @t + ' / ' + @n;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = @n AND object_id = OBJECT_ID('dbo.' + @t))
        PRINT 'exists: ' + @n;
    ELSE
    BEGIN
        SET @sql = N'CREATE INDEX ' + QUOTENAME(@n) + N' ON dbo.' + QUOTENAME(@t) + N' (' + @c + N')'
                 + CASE WHEN @i IS NULL THEN N'' ELSE N' INCLUDE (' + @i + N')' END + N' WITH (ONLINE = OFF);';
        BEGIN TRY
            EXEC sp_executesql @sql;
            PRINT 'created: ' + @n;
        END TRY
        BEGIN CATCH
            PRINT 'FAILED: ' + @n + ' -> ' + ERROR_MESSAGE();
        END CATCH
    END
    FETCH NEXT FROM cur INTO @t, @n, @c, @i;
END
CLOSE cur; DEALLOCATE cur;
