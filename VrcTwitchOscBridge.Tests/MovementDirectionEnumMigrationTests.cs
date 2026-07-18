using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class MovementDirectionEnumMigrationTests
{
    [Fact]
    public void ExistingEnumValues_KeepIntegerPositions()
    {
        Assert.Equal(0, (int)PlayerMovementDirection.Forward);
        Assert.Equal(1, (int)PlayerMovementDirection.Backward);
        Assert.Equal(2, (int)PlayerMovementDirection.Left);
        Assert.Equal(3, (int)PlayerMovementDirection.Right);
        Assert.Equal(4, (int)PlayerMovementDirection.Jump);
        Assert.Equal(5, (int)PlayerMovementDirection.SpinLeft);
        Assert.Equal(6, (int)PlayerMovementDirection.SpinRight);
        Assert.Equal(7, (int)PlayerMovementDirection.StopMovement);
        Assert.Equal(8, (int)PlayerMovementDirection.StopTurning);
        Assert.Equal(9, (int)PlayerMovementDirection.StopAll);
        Assert.Equal(10, (int)PlayerMovementDirection.RandomMovement);
        Assert.Equal(11, (int)PlayerMovementDirection.GlitchyMovement);
    }

    [Fact]
    public void NewEnumValues_HaveCorrectPositions()
    {
        Assert.Equal(12, (int)PlayerMovementDirection.Run);
        Assert.Equal(13, (int)PlayerMovementDirection.LookHorizontal);
        Assert.Equal(14, (int)PlayerMovementDirection.LookLeft);
        Assert.Equal(15, (int)PlayerMovementDirection.LookRight);
        Assert.Equal(16, (int)PlayerMovementDirection.ComfortLeft);
        Assert.Equal(17, (int)PlayerMovementDirection.ComfortRight);
    }

    [Fact]
    public void NewEnumValues_SnapTurnPositions()
    {
        Assert.Equal(36, (int)PlayerMovementDirection.SnapTurnLeft);
        Assert.Equal(37, (int)PlayerMovementDirection.SnapTurnRight);
    }

    [Fact]
    public void MovementTypeClassifier_CategorizesForwardAsMovement()
    {
        Assert.Equal(MovementCategory.Movement, MovementTypeClassifier.GetCategory(PlayerMovementDirection.Forward));
    }

    [Fact]
    public void MovementTypeClassifier_CategorizesComfortLeftAsTurning()
    {
        Assert.Equal(MovementCategory.Turning, MovementTypeClassifier.GetCategory(PlayerMovementDirection.ComfortLeft));
    }

    [Fact]
    public void MovementTypeClassifier_ComfortLeftIsVrOnly()
    {
        Assert.True(MovementTypeClassifier.IsVrOnly(PlayerMovementDirection.ComfortLeft));
    }

    [Fact]
    public void MovementTypeClassifier_MoveForwardIsNotVrOnly()
    {
        Assert.False(MovementTypeClassifier.IsVrOnly(PlayerMovementDirection.Forward));
    }

    [Fact]
    public void MovementTypeClassifier_LookHorizontalIsAxis()
    {
        Assert.True(MovementTypeClassifier.IsAxisType(PlayerMovementDirection.LookHorizontal));
    }

    [Fact]
    public void MovementTypeClassifier_JumpIsNotAxis()
    {
        Assert.False(MovementTypeClassifier.IsAxisType(PlayerMovementDirection.Jump));
    }
}
