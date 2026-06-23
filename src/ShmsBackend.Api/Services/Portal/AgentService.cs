using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Agent;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
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

    public AgentService(IUnitOfWork unitOfWork, ILogger<AgentService> logger, IEmailService emailService, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _emailService = emailService;
        _notificationService = notificationService;
    }

    public async Task<Agent> CreateAsync(CreateAgentDto dto)
    {
        var existing = await _unitOfWork.Agents.GetByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException($"Agent with email {dto.Email} already exists");

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Email = dto.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            AgencyName = dto.AgencyName,
            LicenseNumber = dto.LicenseNumber,
            IsActive = true,
            IsEmailVerified = true,  // Admin-created accounts are email-trusted
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Agents.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        try
        {
            await _emailService.SendWelcomeEmailAsync(
                agent.Email,
                agent.FirstName,
                dto.Password
            );
            _logger.LogInformation("Welcome email sent to agent {Email}", agent.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to agent {Email}", agent.Email);
        }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[]
                {
                    NotificationAudience.SuperAdmin,
                    NotificationAudience.Admin,
                    NotificationAudience.Secretary
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
        if (!string.IsNullOrEmpty(dto.AgencyName)) agent.AgencyName = dto.AgencyName;
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

    public async Task<bool> DeleteAsync(Guid id)
    {
        var agent = await _unitOfWork.Agents.GetByIdAsync(id);
        if (agent == null) return false;

        await _unitOfWork.Agents.DeleteAsync(agent);
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

        await _unitOfWork.Agents.UpdateAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Agent status toggled: {Id}, IsActive: {IsActive}", id, agent.IsActive);
        return true;
    }
}
