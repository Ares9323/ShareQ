namespace ShareQ.Storage.Rotation;

public interface IRotationService
{
    /// <summary>Apply the rotation policy. Returns counts of soft-deleted, hard-deleted, and orphan blobs removed.</summary>
    Task<RotationResult> RunAsync(RotationPolicy policy, CancellationToken cancellationToken);
}

public sealed record RotationResult(int SoftDeleted, int HardDeleted, int OrphanBlobsRemoved);
