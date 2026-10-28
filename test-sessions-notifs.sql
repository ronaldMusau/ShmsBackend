USE [shms_database];
GO
SELECT TOP 8 TargetUserId, Audience, LEFT(Message,160) AS Msg, CreatedAt FROM Notifications WHERE CreatedAt > DATEADD(hour,-1,GETUTCDATE()) AND TargetUserId <> '372267DF-7926-4343-9633-8458385AC971' ORDER BY CreatedAt DESC;
GO
