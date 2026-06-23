namespace Unlimotion;

/// <summary>
/// Hotkey combination strings for display in the hotkey help panel.
/// </summary>
public static class HotkeyHints
{
    public static string Ctrl => "Ctrl";
    public static string Alt => "Alt";
    public static string Shift => "Shift";

    public static string OpenHotkeyHelp => "F1";
    public static string CloseHotkeyHelp => "Esc";

    // Current task shortcuts
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

    // Relation editor shortcuts
    public static string ConfirmRelation => "Enter";
    public static string CancelRelation => "Esc";

    // Task drag shortcuts
    public static string DragCopyInto => "Drag";
    public static string DragMoveInto => $"{Shift}+drag";
    public static string DragCloneInto => $"{Ctrl}+{Shift}+drag";
    public static string DragSourcesBlockTarget => $"{Ctrl}+drag";
    public static string DragTargetBlocksSources => $"{Alt}+drag";

    // Roadmap shortcuts
    public static string RoadmapFitToScreen => "F / U / T";
    public static string RoadmapResetViewport => "R";
    public static string RoadmapRenameTask => "F2";
    public static string RoadmapCreateSibling => $"{Ctrl}+Enter";
    public static string RoadmapCreateBlockedSibling => $"{Shift}+Enter";
    public static string RoadmapCreateInner => $"{Ctrl}+Tab";
    public static string RoadmapToggleSelection => $"{Ctrl}+click / {Ctrl}+box";
    public static string RoadmapAddSelection => $"{Shift}+click / {Shift}+box";
    public static string RoadmapRemoveSelection => $"{Alt}+click / {Alt}+box";
    public static string RoadmapPan => "Right-drag";
}
