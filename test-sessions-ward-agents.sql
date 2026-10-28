-- Read-only: agents, their ward and flats, and the ward of each session house
USE [shms_database];
GO
SELECT p.Id, p.FirstName, p.LastName, p.Email, p.Ward, p.IsActive FROM Agents a JOIN PortalUsers p ON p.Id=a.Id;
GO
SELECT af.AgentId, f.Id AS FlatId, f.Ward FROM AgentFlats af JOIN Flats f ON f.Id=af.FlatId;
GO
SELECT s.Id, s.Status, s.ScheduledAt, s.AgentId, f.Ward FROM ListingViewingSessions s JOIN Houses h ON h.Id=s.HouseId JOIN Flats f ON f.Id=h.FlatId ORDER BY s.ScheduledAt DESC;
GO
