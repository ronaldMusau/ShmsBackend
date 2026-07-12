using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Agent;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Portal;

public class AgentService : IAgentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AgentService> _logger;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly ShmsDbContext _context;

    public AgentService(
        IUnitOfWork unitOfWork,
        ILogger<AgentService> logger,
        IEmailService emailService,
        INotificationService notificationService,
        IFrontendUrlService frontendUrlService,
        ITokenBlacklistService tokenBlacklistService,
        ShmsDbContext context)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _emailService = emailService;
        _notificationService = notificationService;
        _frontendUrlService = frontendUrlService;
        _tokenBlacklistService = tokenBlacklistService;
        _context = context;
    }

    public async Task<Agent> CreateAsync(CreateAgentDto dto)
    {
        var existing = await _unitOfWork.Agents.GetByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException($"Agent with email {dto.Email} already exists");

        var deleted = await _unitOfWork.Agents.GetDeletedByEmailAsync(dto.Email);
        if (deleted != null)
        {
            deleted.FirstName = dto.FirstName;
            deleted.LastName = dto.LastName;
            deleted.PhoneNumber = dto.PhoneNumber;
            deleted.NationalId = dto.NationalId;
            deleted.DateOfBirth = dto.DateOfBirth;
            deleted.LicenseNumber = dto.LicenseNumber;
            deleted.County = dto.County;
            deleted.Constituency = dto.Constituency;
            deleted.Ward = dto.Ward;
            deleted.IsDeleted = false;
            deleted.DeletedAt = null;
            deleted.IsActive = false;
            deleted.IsEmailVerified = false;
            deleted.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
            deleted.EmailVerificationToken = null;
            deleted.EmailVerificationTokenExpiry = null;
            deleted.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Agents.UpdateAsync(deleted);
            await _unitOfWork.SaveChangesAsync();
            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            deleted.EmailVerificationToken = token;
            deleted.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
            deleted.TemporaryInitialPassword = dto.Password;
            await _unitOfWork.SaveChangesAsync();
            var link = _frontendUrlService.GetPortalEmailVerificationUrl(token, deleted.Email, PortalUserType.Agent);
            var deletedEmailSent = false;
            for (var attempt = 1; attempt <= 3 && !deletedEmailSent; attempt++)
            {
                try
                {
                    await _emailService.SendPortalVerifyWithPasswordEmailAsync(deleted.Email, deleted.FirstName, link, dto.Password);
                    deletedEmailSent = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send verification email to agent {Email} (attempt {Attempt}/3)", deleted.Email, attempt);
                    if (attempt < 3) await Task.Delay(2000);
                }
            }
            if (deletedEmailSent)
            {
                deleted.VerificationEmailSentAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
            }
            return deleted;
        }

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Email = dto.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            NationalId = dto.NationalId,
            DateOfBirth = dto.DateOfBirth,
            LicenseNumber = dto.LicenseNumber,
            County = dto.County,
            Constituency = dto.Constituency,
            Ward = dto.Ward,
            IsActive = false,
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Agents.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        var verificationToken = Guid.NewGuid().ToString("N");
        agent.EmailVerificationToken = verificationToken;
        agent.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
        agent.TemporaryInitialPassword = dto.Password;
        await _unitOfWork.SaveChangesAsync();

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(verificationToken, agent.Email, PortalUserType.Agent);
        var agentEmailSent = false;
        for (var attempt = 1; attempt <= 3 && !agentEmailSent; attempt++)
        {
            try
            {
                await _emailService.SendPortalVerifyWithPasswordEmailAsync(agent.Email, agent.FirstName, verificationLink, dto.Password);
                agentEmailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification email to agent {Email} (attempt {Attempt}/3)", agent.Email, attempt);
                if (attempt < 3) await Task.Delay(2000);
            }
        }
        if (agentEmailSent)
        {
            agent.VerificationEmailSentAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }

        if (dto.FlatIds != null && dto.FlatIds.Any())
        {
            foreach (var flatId in dto.FlatIds)
            {
                _context.AgentFlats.Add(new AgentFlat
                {
                    AgentId = agent.Id,
                    FlatId = flatId,
                    AssignedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();
        }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[]
                {
                    NotificationAudience.SuperAdmin,
                    NotificationAudience.Admin,
                    NotificationAudience.Secretary,
                    NotificationAudience.Manager,
                    NotificationAudience.Accountant
                },
                $"New agent {agent.FirstName} {agent.LastName} has been registered in {agent.Ward ?? "an unspecified area"}.",
                "user"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for agent creation {Email}", agent.Email);
        }

        _logger.LogInformation("Agent created: {Email}", agent.Email);
        return agent;
    }

    public async Task<Agent?> GetByIdAsync(Guid id)
    {
        return await _unitOfWork.Agents.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Agent>> GetAllAsync()
    {
        return await _unitOfWork.Agents.GetAllAsync();
    }

    public async Task<Agent> UpdateAsync(Guid id, UpdateAgentDto dto)
    {
        var agent = await _unitOfWork.Agents.GetByIdAsync(id);
        if (agent == null)
            throw new InvalidOperationException("Agent not found");

        if (!string.IsNullOrEmpty(dto.Email) && dto.Email.ToLower() != agent.Email)
        {
            var duplicate = await _unitOfWork.Agents.GetByEmailAsync(dto.Email);
            if (duplicate != null)
                throw new InvalidOperationException($"Email {dto.Email} is already in use");
            agent.Email = dto.Email.ToLower().Trim();
        }

        if (!string.IsNullOrEmpty(dto.FirstName)) agent.FirstName = dto.FirstName;
        if (!string.IsNullOrEmpty(dto.LastName)) agent.LastName = dto.LastName;
        if (!string.IsNullOrEmpty(dto.PhoneNumber)) agent.PhoneNumber = dto.PhoneNumber;
        if (dto.IsActive.HasValue) agent.IsActive = dto.IsActive.Value;
        if (!string.IsNullOrEmpty(dto.NationalId)) agent.NationalId = dto.NationalId;
        if (dto.DateOfBirth.HasValue) agent.DateOfBirth = dto.DateOfBirth.Value;
        if (!string.IsNullOrEmpty(dto.LicenseNumber)) agent.LicenseNumber = dto.LicenseNumber;
        if (dto.County != null) agent.County = dto.County;
        if (dto.Constituency != null) agent.Constituency = dto.Constituency;
        if (dto.Ward != null) agent.Ward = dto.Ward;

        agent.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Agents.UpdateAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Agent updated: {Id}", id);
        return agent;
    }

    public async Task AssignFlatsAsync(Guid agentId, AgentFlatAssignmentDto dto)
    {
        var existing = await _context.AgentFlats
            .Where(af => af.AgentId == agentId)
            .ToListAsync();
        _context.AgentFlats.RemoveRange(existing);

        foreach (var flatId in dto.FlatIds)
        {
            _context.AgentFlats.Add(new AgentFlat
            {
                AgentId = agentId,
                FlatId = flatId,
                AssignedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
        _logger.LogInformation("Flats assigned to agent {AgentId}: {Count}", agentId, dto.FlatIds.Count);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var agent = await _unitOfWork.Agents.GetByIdAsync(id);
        if (agent == null) return false;

        agent.IsDeleted = true;
        agent.DeletedAt = DateTime.UtcNow;
        agent.IsActive = false;
        await _unitOfWork.Agents.UpdateAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Agent deleted: {Id}", id);
        return true;
    }

    public async Task<bool> ToggleStatusAsync(Guid id)
    {
        var agent = await _unitOfWork.Agents.GetByIdAsync(id);
        if (agent == null) return false;

        agent.IsActive = !agent.IsActive;
        agent.UpdatedAt = DateTime.UtcNow;

        if (!agent.IsActive)
        {
            if (!string.IsNullOrEmpty(agent.RefreshToken))
                await _tokenBlacklistService.BlacklistTokenAsync(agent.RefreshToken, TimeSpan.FromDays(30));

            try
            {
                await _emailService.SendAccountDeactivatedEmailAsync(agent.Email, agent.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send deactivation email to {Email}", agent.Email);
            }
        }
        else
        {
            try
            {
                await _emailService.SendAccountReactivatedEmailAsync(agent.Email, agent.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reactivation email to {Email}", agent.Email);
            }
        }

        try
        {
            await _notificationService.SendToUserAsync(
                agent.Id.ToString(),
                agent.IsActive
                    ? "Your account has been reactivated. You can now log in."
                    : "Your account has been deactivated. Please contact your administrator.",
                "account");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send status change notification for agent {Id}", agent.Id);
        }

        await _unitOfWork.Agents.UpdateAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Agent status toggled: {Id}, IsActive: {IsActive}", id, agent.IsActive);
        return true;
    }
}
