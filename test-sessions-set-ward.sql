-- Puts the Test Agent in ward Muthwani (same ward as Agent Ronald's pending session) and moves
-- that session + the declined one to future dates so reminders/reassign can be tested.
USE [shms_database];
GO
UPDATE PortalUsers SET Ward='Muthwani' WHERE Id='7E570000-0000-0000-0011-000000000001';
UPDATE ListingViewingSessions SET ScheduledAt=DATEADD(day,2,CAST(CAST(GETUTCDATE() AS date) AS datetime2)) WHERE Id IN ('9EAA6B0B-FC55-4712-954A-FE7157A4E0CD','CBB49662-0816-42F9-9D00-4AD0ED9CE066');
GO
