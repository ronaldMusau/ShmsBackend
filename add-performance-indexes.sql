-- Performance indexes for list pages, charts and the public listing at large data volumes.
-- Safe to run more than once: each index is created only if it is missing and the table/columns exist.
-- Run:  sqlcmd -I -S localhost -U sa -P "<password>" -C -d shms_database -i add-performance-indexes.sql  (-I is required: one index is filtered)
SET NOCOUNT ON;

DECLARE @ix TABLE (tbl sysname, name sysname, cols nvarchar(400), incl nvarchar(400) NULL);
INSERT INTO @ix VALUES
 ('Payments','IX_Payments_Year_Month','[Year],[Month]','[FlatId],[PaymentStatus],[IsDeleted]'),
 ('Payments','IX_Payments_FlatId_Year_Month','[FlatId],[Year],[Month]',NULL),
 ('Payments','IX_Payments_CreatedAt','[CreatedAt] DESC',NULL),
 ('Complaints','IX_Complaints_Status_CreatedAt','[Status],[CreatedAt] DESC',NULL),
 ('Complaints','IX_Complaints_CreatedAt','[CreatedAt] DESC',NULL),
 ('Expenses','IX_Expenses_ExpenseDate','[ExpenseDate]',NULL),
 ('Expenses','IX_Expenses_FlatId_ExpenseDate','[FlatId],[ExpenseDate]',NULL),
 ('Expenses','IX_Expenses_LandlordId_ExpenseDate','[LandlordId],[ExpenseDate]',NULL),
 ('Notifications','IX_Notifications_Target_IsRead_CreatedAt','[TargetUserId],[IsRead],[CreatedAt] DESC',NULL),
 ('TenantHouseHistories','IX_TenantHouseHistories_House_RemovedAt','[HouseId],[RemovedAt]','[TenantId],[AssignedAt],[TenantFirstName],[TenantLastName]'),
 ('HouseTypeImages','IX_HouseTypeImages_Flat_Type_Sort','[FlatId],[HouseTypeId],[SortOrder]','[ImagePath]'),
 ('Houses','IX_Houses_FlatId_OccupancyStatus','[FlatId],[OccupancyStatus]',NULL),
 ('Tenants','IX_Tenants_HouseId','[HouseId]',NULL),
 ('ListingViewingSessions','IX_ListingViewingSessions_Agent_ScheduledAt','[AgentId],[ScheduledAt]',NULL),
 ('ListingViewingSessions','IX_ListingViewingSessions_Explorer_CreatedAt','[ExplorerId],[CreatedAt] DESC',NULL);

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
