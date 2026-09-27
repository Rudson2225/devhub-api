using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Users;
using DevHub.Application.Mapping;

namespace DevHub.Application.UseCases;

public interface IUserService
{
    Task<UserResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
}

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUserRepository users, IUnitOfWork unitOfWork)
    {
        _users = users;
        _unitOfWork = unitOfWork;
    }

    public async Task<UserResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        return user.ToResponse();
    }

    public async Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        user.DisplayName = request.DisplayName.Trim();
        user.Bio = request.Bio;
        user.AvatarUrl = request.AvatarUrl;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return user.ToResponse();
    }
}
