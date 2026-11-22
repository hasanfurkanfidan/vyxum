namespace Vyxum.Platform.Abstractions;

/// <summary>
/// How an approval was given: a person picked at the station with their PIN, or an approval that was requested from
/// another screen and decided there (<see cref="RemoteApprovalId"/>).
/// </summary>
/// <param name="ApproverId">The approving person, when approved at the station.</param>
/// <param name="Pin">The approving person's PIN, when approved at the station.</param>
/// <param name="RemoteApprovalId">The id of a remote approval request, when approved from another screen.</param>
public sealed record ApprovalProof(string? ApproverId = null, string? Pin = null, Guid? RemoteApprovalId = null);

/// <summary>An exception that needs a supervisor, e.g. continuing with an out-of-tolerance weight.</summary>
/// <param name="Permission">The permission the approver must have, e.g. <c>formulation.override</c>.</param>
/// <param name="Reason">Why the operator asks; required.</param>
/// <param name="Subject">What is approved, written to the record, e.g. <c>Hazelnut kernels 14 g over</c>.</param>
/// <param name="Proof">How it was approved; null when no users module is active.</param>
public sealed record ApprovalRequest(Permission Permission, string Reason, string Subject, ApprovalProof? Proof);

/// <summary>The outcome of an <see cref="ApprovalRequest"/>.</summary>
/// <param name="Approved">Whether the operation may continue.</param>
/// <param name="Approver">Who approved; null when approved without a users module (reason only).</param>
/// <param name="Reason">The reason as recorded.</param>
/// <param name="RejectionCode">An error code for the UI when not approved, e.g. <c>approval.ReasonRequired</c>.</param>
public sealed record ApprovalResult(bool Approved, Actor? Approver, string? Reason, string? RejectionCode)
{
    /// <summary>A refused approval with <paramref name="code"/>.</summary>
    public static ApprovalResult Rejected(string code) => new(false, null, null, code);
}

/// <summary>Checks a supervisor approval for an exception.</summary>
/// <remarks>
/// Without an active users module an approval needs only a reason and has no approver. With one, the approver is
/// checked: they have the permission, their PIN is right, and they are not the person asking (four eyes).
/// </remarks>
public interface ISupervisorApproval
{
    /// <summary>Checks <paramref name="request"/> and records the approval when it is valid.</summary>
    Task<ApprovalResult> ApproveAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Error codes of <see cref="ApprovalResult.RejectionCode"/> that every implementation uses.</summary>
public static class ApprovalErrors
{
    /// <summary>The reason is empty.</summary>
    public const string ReasonRequired = "approval.ReasonRequired";

    /// <summary>The approver, the PIN or the remote approval is missing or wrong.</summary>
    public const string NotApproved = "approval.NotApproved";
}
