using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// A portal user's uploaded ID document images. One row per portal user (PortalUserId is a loose
/// reference to PortalUser.Id — no FK; unique). Front and back are independent — either may be null.
/// </summary>
public class UserIdDocument
{
    public Guid Id { get; set; }
    public Guid PortalUserId { get; set; }
    public string? FrontImagePath { get; set; }
    public string? BackImagePath { get; set; }
    public DateTime? UploadedAt { get; set; }
}
