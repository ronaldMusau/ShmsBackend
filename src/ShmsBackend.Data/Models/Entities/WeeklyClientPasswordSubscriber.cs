using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// Presence of a row = this ADMIN staff member is opted in to receive the weekly client-portal
/// support password by email. Subscribers are staff (Admin.Id), never portal users. AdminId is a
/// loose reference to Admin.Id (no FK constraint); unique so an admin can't opt in twice.
/// This list governs email delivery only — it never gates portal login.
/// </summary>
public class WeeklyClientPasswordSubscriber
{
    public Guid Id { get; set; }
    public Guid AdminId { get; set; }
    public DateTime CreatedAt { get; set; }
}
