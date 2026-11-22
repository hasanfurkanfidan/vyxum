namespace Vyxum.Platform.Abstractions;

/// <summary>
/// An operation that needs an electronic signature (see <see cref="PermissionDefinition.RequiresSignature"/>), e.g.
/// approving a recipe version.
/// </summary>
/// <param name="Permission">The permission the signer must have, e.g. <c>formulation.recipe.approve</c>.</param>
/// <param name="Meaning">What the signature means, written to the record, e.g. <c>Recipe approval, R-014 version 4</c>.</param>
/// <param name="Password">The signer's own password, entered again for this signature.</param>
public sealed record SignatureRequest(Permission Permission, string Meaning, string? Password);

/// <summary>The outcome of a <see cref="SignatureRequest"/>.</summary>
/// <param name="Signed">Whether the operation may continue.</param>
/// <param name="Signer">Who signed: the signed-in person; null when signed without a users module.</param>
/// <param name="SignedAtUtc">UTC time of the signature.</param>
/// <param name="RejectionCode">An error code for the UI when not signed, e.g. <c>signature.NotSigned</c>.</param>
public sealed record SignatureResult(bool Signed, Actor? Signer, DateTime? SignedAtUtc, string? RejectionCode)
{
    /// <summary>A refused signature with <paramref name="code"/>.</summary>
    public static SignatureResult Rejected(string code) => new(false, null, null, code);
}

/// <summary>Checks an electronic signature: the signed-in person enters their own password again.</summary>
/// <remarks>
/// Without an active users module there is nobody to sign: the operation continues without a signer, and the module
/// shows no signature step. With one, the signer is the signed-in person; they must have the permission and enter
/// their own password. A PIN is not a signature.
/// </remarks>
public interface IElectronicSignature
{
    /// <summary>Checks <paramref name="request"/> and records the signature when it is valid.</summary>
    Task<SignatureResult> SignAsync(SignatureRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Error codes of <see cref="SignatureResult.RejectionCode"/> that every implementation uses.</summary>
public static class SignatureErrors
{
    /// <summary>The password is empty.</summary>
    public const string PasswordRequired = "signature.PasswordRequired";

    /// <summary>The password is wrong, or the signer does not have the permission.</summary>
    public const string NotSigned = "signature.NotSigned";
}
