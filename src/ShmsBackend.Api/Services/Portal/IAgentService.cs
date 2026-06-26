using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Agent;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public interface IAgentService
{
    Task<Agent> CreateAsync(CreateAgentDto dto);
    Task<Agent?> GetByIdAsync(Guid id);
    Task<IEnumerable<Agent>> GetAllAsync();
    Task<Agent> UpdateAsync(Guid id, UpdateAgentDto dto);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ToggleStatusAsync(Guid id);
    Task AssignFlatsAsync(Guid agentId, AgentFlatAssignmentDto dto);
}
