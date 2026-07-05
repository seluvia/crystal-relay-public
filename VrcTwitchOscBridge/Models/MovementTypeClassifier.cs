namespace VrcTwitchOscBridge.Models;

public static class MovementTypeClassifier
{
    public static MovementCategory GetCategory(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.Forward or PlayerMovementDirection.Backward
            or PlayerMovementDirection.Left or PlayerMovementDirection.Right
            or PlayerMovementDirection.Jump or PlayerMovementDirection.Run
            or PlayerMovementDirection.RandomMovement or PlayerMovementDirection.GlitchyMovement
            => MovementCategory.Movement,

        PlayerMovementDirection.LookHorizontal or PlayerMovementDirection.LookLeft
            or PlayerMovementDirection.LookRight or PlayerMovementDirection.ComfortLeft
            or PlayerMovementDirection.ComfortRight
            => MovementCategory.Turning,

        PlayerMovementDirection.GrabLeft or PlayerMovementDirection.GrabRight
            or PlayerMovementDirection.UseLeft or PlayerMovementDirection.UseRight
            or PlayerMovementDirection.DropLeft or PlayerMovementDirection.DropRight
            => MovementCategory.HandInteractions,

        PlayerMovementDirection.MoveHoldFB or PlayerMovementDirection.SpinHoldCwCcw
            or PlayerMovementDirection.SpinHoldUD or PlayerMovementDirection.SpinHoldLR
            => MovementCategory.HeldObject,

        PlayerMovementDirection.QuickMenuToggleLeft or PlayerMovementDirection.QuickMenuToggleRight
            or PlayerMovementDirection.PanicButton or PlayerMovementDirection.Voice
            => MovementCategory.UiToggles,

        _ => MovementCategory.Movement,
    };

    public static bool IsVrOnly(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.ComfortLeft or PlayerMovementDirection.ComfortRight
            or PlayerMovementDirection.GrabLeft or PlayerMovementDirection.GrabRight
            or PlayerMovementDirection.UseLeft or PlayerMovementDirection.UseRight
            or PlayerMovementDirection.DropLeft or PlayerMovementDirection.DropRight
            => true,
        _ => false,
    };

    public static bool IsAxisType(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.LookHorizontal
            or PlayerMovementDirection.MoveHoldFB
            or PlayerMovementDirection.SpinHoldCwCcw
            or PlayerMovementDirection.SpinHoldUD
            or PlayerMovementDirection.SpinHoldLR
            => true,
        _ => false,
    };

    public static string GetBehaviorTooltip(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.LookLeft or PlayerMovementDirection.LookRight
            => "Smooth on Desktop. Snap-turn in VR if Comfort Turning is ON.",
        PlayerMovementDirection.LookHorizontal
            => "Smooth on Desktop. Snap-turn in VR if Comfort Turning is ON.",
        PlayerMovementDirection.ComfortLeft or PlayerMovementDirection.ComfortRight
            => "VR-only. Always snap-turn regardless of Comfort Turning setting.",
        PlayerMovementDirection.GrabLeft or PlayerMovementDirection.GrabRight
            or PlayerMovementDirection.UseLeft or PlayerMovementDirection.UseRight
            or PlayerMovementDirection.DropLeft or PlayerMovementDirection.DropRight
            => "VR-only input. No effect on Desktop.",
        PlayerMovementDirection.MoveHoldFB or PlayerMovementDirection.SpinHoldCwCcw
            or PlayerMovementDirection.SpinHoldUD or PlayerMovementDirection.SpinHoldLR
            => "Controls held objects. Axis speed value = speed setting.",
        PlayerMovementDirection.QuickMenuToggleLeft or PlayerMovementDirection.QuickMenuToggleRight
            or PlayerMovementDirection.PanicButton
            => "Triggers UI action. Duration = hold time before reset.",
        PlayerMovementDirection.Voice
            => "Toggles voice. Behavior depends on VRChat 'Toggle Voice' setting.",
        _ => string.Empty,
    };
}
