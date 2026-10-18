namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The body of <c>POST /Kids/Parent/Verify</c>.
/// </summary>
public class VerifyRequest
{
    /// <summary>
    /// Gets or sets the code to check.
    /// </summary>
    public string? Code { get; set; }

    /// <inheritdoc />
    public override string ToString() => "VerifyRequest { Code = *** }";
}
