USE [shms_database];
GO
SELECT TOP 10 p.Id, p.Email, p.FirstName, p.IsEmailVerified FROM Explorers e JOIN PortalUsers p ON p.Id=e.Id;
GO
