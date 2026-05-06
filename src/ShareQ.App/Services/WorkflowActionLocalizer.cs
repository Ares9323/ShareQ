using System.Globalization;
using ShareQ.App.Resources;

namespace ShareQ.App.Services;

/// <summary>UI translator for the <see cref="ViewModels.WorkflowActionCatalog"/> entries —
/// step titles, descriptions, categories, and per-parameter labels. The catalog itself stays
/// in English (the data model identity uses raw task ids + property keys), the UI fetches
/// localised strings through this helper at render time. Misses fall back to whatever string
/// the descriptor shipped with — same fallback chain the rest of the i18n stack uses.</summary>
public static class WorkflowActionLocalizer
{
    private static CultureInfo Culture =>
        Markup.LocalizedStrings.Instance.Culture ?? CultureInfo.CurrentUICulture;

    /// <summary>Sanitise a task id ("shareq.capture-region") into a resx-safe key suffix
    /// ("shareq_capture_region"). Both '.' and '-' aren't allowed in resx names.</summary>
    private static string Sanitize(string taskId) => taskId.Replace('.', '_').Replace('-', '_');

    public static string LocalizeTitle(string taskId, string fallback, string? localizationKey = null)
    {
        if (string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(localizationKey)) return fallback;
        var suffix = !string.IsNullOrEmpty(localizationKey) ? localizationKey : Sanitize(taskId);
        var key = "WorkflowAction_" + suffix;
        return Strings.ResourceManager.GetString(key, Culture) ?? fallback;
    }

    public static string LocalizeDescription(string taskId, string fallback, string? localizationKey = null)
    {
        if (string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(localizationKey)) return fallback;
        var suffix = !string.IsNullOrEmpty(localizationKey) ? localizationKey : Sanitize(taskId);
        var key = "WorkflowActionDesc_" + suffix;
        return Strings.ResourceManager.GetString(key, Culture) ?? fallback;
    }

    public static string LocalizeCategory(string category, string fallback)
    {
        if (string.IsNullOrEmpty(category)) return fallback;
        var key = "WorkflowActionCategory_" + category;
        return Strings.ResourceManager.GetString(key, Culture) ?? fallback;
    }

    public static string LocalizeParameter(string taskId, string paramKey, string fallback)
    {
        if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(paramKey)) return fallback;
        var key = "WorkflowActionParam_" + Sanitize(taskId) + "_" + paramKey;
        return Strings.ResourceManager.GetString(key, Culture) ?? fallback;
    }
}
