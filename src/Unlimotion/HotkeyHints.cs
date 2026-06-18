namespace Unlimotion;

/// <summary>
/// Hotkey combination strings for display in the hotkey help panel.
/// </summary>
public static class HotkeyHints
{
    public static string Ctrl => "Ctrl";
    public static string Alt => "Alt";
    public static string Shift => "Shift";

    // Task tree shortcuts
    public static string SelectAll => $"{Ctrl}+A";
    public static string DeleteSelection => $"{Shift}+Del";
    public static string RenameTask => "F2";
    public static string CreateSibling => $"{Ctrl}+Enter";
    public static string CreateBlockedSibling => $"{Shift}+Enter";
    public static string CreateInner => $"{Ctrl}+Tab";
    public static string ExpandCurrent => $"{Ctrl}+{Shift}+→";
    public static string CollapseCurrent => $"{Ctrl}+{Shift}+←";
    public static string ExpandAll => $"{Ctrl}+{Alt}+→";
    public static string CollapseAll => $"{Ctrl}+{Alt}+←";
    public static string CopyOutline => $"{Ctrl}+{Shift}+C";
    public static string PasteOutline => $"{Ctrl}+{Shift}+V";
    public static string CompleteCurrentTask => $"{Ctrl}+D";

    // Roadmap shortcuts
    public static string RoadmapFitToScreen => "F / U / T";
    public static string RoadmapResetViewport => "R";
    public static string RoadmapRenameTask => "F2";
    public static string RoadmapCreateSibling => $"{Ctrl}+Enter";
    public static string RoadmapCreateBlockedSibling => $"{Shift}+Enter";
    public static string RoadmapCreateInner => $"{Ctrl}+Tab";
}
