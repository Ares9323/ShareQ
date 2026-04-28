namespace ShareQ.PluginContracts;

/// <summary>
/// Content types an uploader supports. The host's settings UI groups uploaders by capability so
/// the user can pick separately the destination for screenshots, file shares, and text snippets.
/// Mirrors the ShareX split between image / file / text uploaders.
/// </summary>
[Flags]
public enum UploaderCapabilities
{
    None  = 0,
    Image = 1 << 0,
    File  = 1 << 1,
    Text  = 1 << 2,
    Video = 1 << 3,

    /// <summary>Generic file host — accepts any binary including images, video, and text.</summary>
    AnyFile = Image | File | Text | Video,
}
