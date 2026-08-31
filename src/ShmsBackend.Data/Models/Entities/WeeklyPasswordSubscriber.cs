using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// Presence of a row = this admin is opted in to receive the weekly shared password by email.
/// AdminId is a loose reference to Admin.Id (no FK constraint); unique so an admin can't opt in twice.
/// </summary>
public class WeeklyPasswordSubscriber
{
    public Guid Id { get; set; }
    public Guid AdminId { get; set; }
    public DateTime CreatedAt { get; set; }
}
