using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.User;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.User;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UserController> _logger;

    public UserController(IUserService userService, ILogger<UserController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// CREATE USER - Only SuperAdmin can create Admin and above
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "SuperAdmin")] // ONLY SuperAdmin can create users
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto createUserDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var createdBy = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

            // Verify SuperAdmin is not creating another SuperAdmin (optional)
            if (createUserDto.UserType == UserType.SuperAdmin)
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Cannot create another Super Admin"));
            }

            var user = await _userService.CreateUserAsync(createUserDto, createdBy);

            return CreatedAtAction(nameof(GetUser), new { id = user.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    user.Id,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.UserType,
                    user.IsActive
                }, "User created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to create user");
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while creating the user"));
        }
    }

    /// <summary>
    /// GET USER BY ID - SuperAdmin can get any user, Admin can get Manager/Accountant/Secretary
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Accountant,Secretary")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUserRole = GetCurrentUserRole();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            // Role-based access control
            if (!CanAccessUser(currentUserRole, currentUserId, user))
            {
                return Forbid();
            }

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.UserType,
                user.IsActive,
                user.IsEmailVerified,
                user.CreatedAt,
                user.UpdatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by id: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving the user"));
        }
    }

    /// <summary>
    /// GET ALL USERS - SuperAdmin sees all, Admin sees Manager/Accountant/Secretary only
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetAllUsers()
    {
        try
        {
            var currentUserRole = GetCurrentUserRole();
            var users = await _userService.GetAllUsersAsync();
            var userList = new List<object>();

            foreach (var user in users)
            {
                // Filter based on role
                if (currentUserRole == UserType.SuperAdmin)
                {
                    // SuperAdmin sees all
                    userList.Add(MapUserToResponse(user));
                }
                else if (currentUserRole == UserType.Admin)
                {
                    // Admin sees only Manager, Accountant, Secretary
                    if (user.UserType == UserType.Manager ||
                        user.UserType == UserType.Accountant ||
                        user.UserType == UserType.Secretary)
                    {
                        userList.Add(MapUserToResponse(user));
                    }
                }
            }

            return Ok(ApiResponse<object>.SuccessResponse(userList));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all users");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving users"));
        }
    }

    /// <summary>
    /// UPDATE USER - Only SuperAdmin and Admin can update, but with restrictions
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto updateUserDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var currentUserRole = GetCurrentUserRole();
            var userToUpdate = await _userService.GetUserByIdAsync(id);

            if (userToUpdate == null)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            // Role-based update restrictions
            if (currentUserRole == UserType.Admin)
            {
                // Admin can only update Manager, Accountant, Secretary
                if (userToUpdate.UserType != UserType.Manager &&
                    userToUpdate.UserType != UserType.Accountant &&
                    userToUpdate.UserType != UserType.Secretary)
                {
                    return Forbid();
                }

                // Admin cannot update role or critical fields
                updateUserDto.UserType = null; // Prevent role change
            }

            var user = await _userService.UpdateUserAsync(id, updateUserDto);

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.UserType,
                user.IsActive,
                user.UpdatedAt
            }, "User updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to update user: {Id}", id);
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while updating the user"));
        }
    }

    /// <summary>
    /// DELETE USER - Only SuperAdmin can delete
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin")] // ONLY SuperAdmin can delete
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var userToDelete = await _userService.GetUserByIdAsync(id);
            if (userToDelete == null)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            // Prevent deleting self
            if (userToDelete.Id == GetCurrentUserId())
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Cannot delete yourself"));
            }

            var result = await _userService.DeleteUserAsync(id);
            if (!result)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            return Ok(ApiResponse<object?>.SuccessResponse(null, "User deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to delete user: {Id}", id);
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while deleting the user"));
        }
    }

    /// <summary>
    /// TOGGLE USER STATUS - Only SuperAdmin can toggle
    /// </summary>
    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = "SuperAdmin")] // ONLY SuperAdmin can toggle status
    public async Task<IActionResult> ToggleUserStatus(Guid id)
    {
        try
        {
            var userToToggle = await _userService.GetUserByIdAsync(id);
            if (userToToggle == null)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            // Prevent toggling self
            if (userToToggle.Id == GetCurrentUserId())
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Cannot toggle your own status"));
            }

            var result = await _userService.ToggleUserStatusAsync(id);
            if (!result)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            return Ok(ApiResponse<object?>.SuccessResponse(null, "User status updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to toggle user status: {Id}", id);
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling user status: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while updating user status"));
        }
    }

    /// <summary>
    /// GET USER TYPE - Available to all authenticated users
    /// </summary>
    [HttpGet("{id}/type")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Accountant,Secretary")]
    public async Task<IActionResult> GetUserType(Guid id)
    {
        try
        {
            var userType = await _userService.GetUserTypeAsync(id);
            return Ok(ApiResponse<object>.SuccessResponse(new { UserType = userType }));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user type: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving user type"));
        }
    }

    #region Helper Methods

    private Guid GetCurrentUserId()
    {
        return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());
    }

    private UserType GetCurrentUserRole()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        return Enum.Parse<UserType>(roleClaim ?? "Secretary");
    }

    private bool CanAccessUser(UserType currentUserRole, Guid currentUserId, Data.Models.Entities.Admin targetUser)
    {
        return currentUserRole switch
        {
            UserType.SuperAdmin => true, // SuperAdmin can access all
            UserType.Admin => targetUser.UserType == UserType.Manager ||
                             targetUser.UserType == UserType.Accountant ||
                             targetUser.UserType == UserType.Secretary, // Admin can only access lower roles
            UserType.Manager => targetUser.Id == currentUserId, // Can only access self
            UserType.Accountant => targetUser.Id == currentUserId, // Can only access self
            UserType.Secretary => targetUser.Id == currentUserId, // Can only access self
            _ => false
        };
    }

    private object MapUserToResponse(Data.Models.Entities.Admin user)
    {
        return new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.UserType,
            user.IsActive,
            user.IsEmailVerified,
            user.CreatedAt
        };
    }

    #endregion
}