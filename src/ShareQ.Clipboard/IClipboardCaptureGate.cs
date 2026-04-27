namespace ShareQ.Clipboard;

public interface IClipboardCaptureGate
{
    /// <summary>Returns true if the current clipboard event should be ingested.</summary>
    GateDecision Evaluate();
}

public sealed record GateDecision(bool Allow, string? Reason);
