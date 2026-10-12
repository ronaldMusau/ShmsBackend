using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Controllers;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using Xunit;

namespace ShmsBackend.Tests;

public class ApproverLookupTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid DeletedAdminId = Guid.NewGuid();
    private static readonly Guid PortalUserId = Guid.NewGuid();

    private static ShmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ShmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ShmsDbContext(options);

        context.SuperAdmins.Add(new SuperAdmin
        {
            Id = AdminId, Email = "approver@staff.test", FirstName = "Staff", LastName = "Approver",
            PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        context.SuperAdmins.Add(new SuperAdmin
        {
            Id = DeletedAdminId, Email = "gone@staff.test", FirstName = "Former", LastName = "Approver",
            PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsDeleted = true
        });
        context.Landlords.Add(new Landlord
        {
            Id = PortalUserId, Email = "landlord@portal.test", FirstName = "Portal", LastName = "Landlord",
            PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task GetContactAsync_AdminApprover_ResolvesStaffEmailAsNonPortalUser()
    {
        using var context = CreateContext();

        var contact = await ApproverLookup.GetContactAsync(context, AdminId);

        Assert.NotNull(contact);
        Assert.Equal("approver@staff.test", contact!.Email);
        Assert.Equal("Staff", contact.FirstName);
        Assert.False(contact.IsPortalUser);
    }

    [Fact]
    public async Task GetContactAsync_PortalApprover_ResolvesAsPortalUser()
    {
        using var context = CreateContext();

        var contact = await ApproverLookup.GetContactAsync(context, PortalUserId);

        Assert.NotNull(contact);
        Assert.Equal("landlord@portal.test", contact!.Email);
        Assert.True(contact.IsPortalUser);
    }

    [Fact]
    public async Task GetContactAsync_SoftDeletedAdminOrUnknownId_ReturnsNull()
    {
        using var context = CreateContext();

        Assert.Null(await ApproverLookup.GetContactAsync(context, DeletedAdminId));
        Assert.Null(await ApproverLookup.GetContactAsync(context, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetNamesAsync_MergesBothTables_IncludesSoftDeletedAdmin_OmitsUnknown()
    {
        using var context = CreateContext();
        var unknownId = Guid.NewGuid();

        var names = await ApproverLookup.GetNamesAsync(context, new[] { AdminId, DeletedAdminId, PortalUserId, unknownId });

        Assert.Equal("Staff Approver", names[AdminId]);
        Assert.Equal("Former Approver", names[DeletedAdminId]);
        Assert.Equal("Portal Landlord", names[PortalUserId]);
        Assert.False(names.ContainsKey(unknownId));
    }
}
