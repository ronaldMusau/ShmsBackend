using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Notification;

public class BulkNotificationIdsDto
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one notification id is required.")]
    public List<Guid> Ids { get; set; } = new();
}
