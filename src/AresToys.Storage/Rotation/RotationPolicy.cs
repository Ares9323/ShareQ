namespace AresToys.Storage.Rotation;

public sealed record RotationPolicy(
    int MaxItems,
    TimeSpan MaxAge,
    TimeSpan SoftDeleteGracePeriod);
