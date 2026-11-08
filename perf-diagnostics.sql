-- Read-only performance diagnostics. Changes nothing. Run after using the apps for a while (the numbers reset when SQL Server restarts):
--   sqlcmd -I -S localhost -U sa -C -d shms_database -i perf-diagnostics.sql -o perf-diagnostics-result.txt -W -s"|"
SET NOCOUNT ON;

PRINT '=== 1. Slowest queries by average time (top 20) ===';
SELECT TOP 20
    CAST(qs.total_elapsed_time / qs.execution_count / 1000.0 AS decimal(12,1)) AS avg_ms,
    qs.execution_count AS runs,
    CAST(qs.total_logical_reads / qs.execution_count AS bigint) AS avg_reads,
    LEFT(REPLACE(REPLACE(SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
        ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset)/2)+1),
        CHAR(13),' '), CHAR(10),' '), 300) AS query_text
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.dbid = DB_ID() AND qs.execution_count >= 3
ORDER BY avg_ms DESC;

PRINT '=== 2. Indexes SQL Server says are missing (best first) ===';
SELECT TOP 20
    CAST(gs.avg_total_user_cost * gs.avg_user_impact * (gs.user_seeks + gs.user_scans) AS bigint) AS impact_score,
    gs.user_seeks + gs.user_scans AS times_wanted,
    OBJECT_NAME(mid.object_id) AS table_name,
    mid.equality_columns, mid.inequality_columns, mid.included_columns
FROM sys.dm_db_missing_index_group_stats gs
JOIN sys.dm_db_missing_index_groups mig ON mig.index_group_handle = gs.group_handle
JOIN sys.dm_db_missing_index_details mid ON mid.index_handle = mig.index_handle
WHERE mid.database_id = DB_ID()
ORDER BY impact_score DESC;

PRINT '=== 3. Foreign-key columns with no index at all ===';
SELECT OBJECT_NAME(fkc.parent_object_id) AS table_name, COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS column_name
FROM sys.foreign_key_columns fkc
WHERE NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    WHERE ic.object_id = fkc.parent_object_id AND ic.column_id = fkc.parent_column_id AND ic.key_ordinal = 1)
ORDER BY table_name, column_name;

PRINT '=== 4. Id-style columns that are not foreign keys and have no index (candidates for filters) ===';
SELECT t.name AS table_name, c.name AS column_name
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
WHERE (c.name LIKE '%Id' AND c.name <> 'Id')
  AND NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns f WHERE f.parent_object_id = c.object_id AND f.parent_column_id = c.column_id)
  AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = c.object_id AND ic.column_id = c.column_id AND ic.key_ordinal = 1)
  AND t.name NOT LIKE '\_\_%' ESCAPE '\'
ORDER BY t.name, c.name;

PRINT '=== 5. Biggest tables (rows) ===';
SELECT TOP 15 t.name AS table_name, SUM(p.rows) AS row_count
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
GROUP BY t.name ORDER BY row_count DESC;
