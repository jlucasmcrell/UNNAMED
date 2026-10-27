using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The crafting panel's quantity and queue (the owner's note 12): an amount the − and + move, Make All counting what the materials allow,
/// a queue that works one piece at a time, cancels where it stands, and stops by itself when the materials run out.
/// </summary>
public class CraftQueueTests
{
    [Fact]
    public void AQueue_WorksOnePieceAtATime_AndReportsTheLast()
    {
        var queue = new CraftQueue();
        queue.Start("recipe.test.spear", 3);
        Assert.True(queue.Running);
        Assert.Equal(0, queue.Made);

        Assert.Equal(CraftStep.MakeOne, queue.Next(10));
        Assert.Equal(1, queue.Made);
        Assert.Equal(CraftStep.MakeOne, queue.Next(10));
        Assert.Equal(2, queue.Made);
        Assert.Equal(CraftStep.Done, queue.Next(10));
        Assert.False(queue.Running);
        Assert.Equal(3, queue.Made);
        Assert.Equal("Made 3 of 3.", queue.Note);

        // And it stays done: nothing more is asked of the panel.
        Assert.Equal(CraftStep.Idle, queue.Next(10));
    }

    [Fact]
    public void ACancel_StopsWhereItStands_AndKeepsWhatWasMade()
    {
        var queue = new CraftQueue();
        queue.Start("recipe.test.spear", 5);
        queue.Next(10);
        queue.Next(10);
        queue.Cancel();
        Assert.False(queue.Running);
        Assert.Equal(2, queue.Made);
        Assert.Equal("Stopped after 2 of 5.", queue.Note);
        Assert.Equal(CraftStep.Idle, queue.Next(10));
    }

    [Fact]
    public void ShortMaterials_StopItCleanly_WithoutMakingPartOfAPiece()
    {
        var queue = new CraftQueue();
        queue.Start("recipe.test.spear", 4);
        queue.Next(1);          // the last whole set
        Assert.Equal(1, queue.Made);
        // The supply is gone: no partial piece is made, and the line says where it stopped.
        Assert.Equal(CraftStep.StoppedShort, queue.Next(0));
        Assert.False(queue.Running);
        Assert.Equal(1, queue.Made);
        Assert.Equal("Stopped after 1 of 4: the materials ran out.", queue.Note);
    }

    [Fact]
    public void NothingAsked_IsNothingToDo()
    {
        var queue = new CraftQueue();
        queue.Start("recipe.test.spear", 0);
        Assert.False(queue.Running);
        Assert.Equal(CraftStep.Idle, queue.Next(5));
        Assert.Null(queue.Note);
    }
}
