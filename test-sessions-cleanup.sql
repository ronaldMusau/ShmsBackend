-- Removes the sessions/interests created by today's test runs on house TEST-D (7E570000-...-00000000000D).
-- Leaves every other session untouched. Safe to re-run.
USE [shms_database];
GO
DECLARE @h UNIQUEIDENTIFIER = '7E570000-0000-0000-0003-00000000000D';
DELETE m FROM SessionMessages m JOIN ListingViewingSessions s ON s.Id=m.SessionId WHERE s.HouseId=@h AND s.CreatedAt >= '2026-10-28';
UPDATE ExplorerInterests SET SessionId=NULL WHERE SessionId IN (SELECT Id FROM ListingViewingSessions WHERE HouseId=@h AND CreatedAt >= '2026-10-28');
DELETE FROM ListingViewingSessions WHERE HouseId=@h AND CreatedAt >= '2026-10-28';
DELETE FROM ExplorerInterests WHERE HouseId=@h AND CreatedAt >= '2026-10-28';
SELECT COUNT(*) AS RemainingTestDSessions FROM ListingViewingSessions WHERE HouseId=@h;
GO
