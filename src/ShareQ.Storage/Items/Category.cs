namespace ShareQ.Storage.Items;

/// <summary>One user-defined clipboard category (CopyQ-style "tab"). The default <c>"Clipboard"</c>
/// bucket exists in every install; users add/rename/remove others through the Categories
/// settings page. <see cref="MaxItems"/> = 0 disables the cap; <see cref="AutoCleanupDays"/>
/// = 0 disables auto-purge.</summary>
public sealed record Category(
    string Name,
    string? Icon,
    int SortOrder,
    int MaxItems = 0,
    int AutoCleanupDays = 0)
{
    public const string Default = "Clipboard";
}
