using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Without a users module nobody signs in (decision V-03).</summary>
internal sealed class NoUsersCurrentActor : ICurrentActor
{
    public Actor? Person => null;
}

/// <summary>Without a users module everything is allowed, as in the free version.</summary>
internal sealed class NoUsersAuthorization : IAuthorization
{
    public bool IsAllowed(Permission permission) => true;
}

/// <summary>Without a users module nobody signs: the operation continues without a signer.</summary>
internal sealed class NoUsersElectronicSignature(TimeProvider time) : IElectronicSignature
{
    public Task<SignatureResult> SignAsync(SignatureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new SignatureResult(true, null, time.GetUtcNow().UtcDateTime, null));
    }
}

/// <summary>Without a users module an approval needs only a reason and has no approver.</summary>
internal sealed class NoUsersSupervisorApproval : ISupervisorApproval
{
    public Task<ApprovalResult> ApproveAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = request.Reason?.Trim();
        return Task.FromResult(string.IsNullOrEmpty(reason)
            ? ApprovalResult.Rejected(ApprovalErrors.ReasonRequired)
            : new ApprovalResult(true, null, reason, null));
    }
}
