-- Simulates the daily sweep: moves the test session to AwaitingFeedback so the Explorer can close + rate it.
USE [shms_database];
GO
UPDATE ListingViewingSessions SET Status='AwaitingFeedback', FeedbackPromptSentAt=GETUTCDATE() WHERE Id='52138ED1-AF3D-453A-A3C3-F08BFD1375CD';
SELECT Id, Status FROM ListingViewingSessions WHERE Id='52138ED1-AF3D-453A-A3C3-F08BFD1375CD';
GO
