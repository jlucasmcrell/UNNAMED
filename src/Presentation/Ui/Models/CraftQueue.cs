// UNNAMED Presentation - the crafting panel's queue, as a view model (the owner's note 12)
// Godot-free: Presentation.Tests links this file and tests the queue on its own (D-11).

namespace UNNAMED.Presentation.Ui;

/// <summary>What the queue asks the panel to do next.</summary>
public enum CraftStep
{
    /// <summary>Nothing to do: no queue, or it has all been made.</summary>
    Idle,

    /// <summary>Submit one craft, then come back for the next.</summary>
    MakeOne,

    /// <summary>The last piece has been submitted.</summary>
    Done,

    /// <summary>The materials no longer cover another piece, so it stopped where it stood.</summary>
    StoppedShort,
}

/// <summary>
/// A recipe worked a number of times (the owner's note 12): the quantity the player asked for, how many are left, and the words for what
/// happened. Each piece goes through the ordinary craft command, so it keeps its own quality roll, skill practice and first-time XP. The queue
/// stops when the materials run out rather than making part of a piece, and can be stopped by hand at any point.
/// </summary>
public sealed class CraftQueue
{
    public string RecipeId { get; private set; } = string.Empty;

    public int Asked { get; private set; }

    public int Left { get; private set; }

    public bool Running => Left > 0;

    /// <summary>How many pieces have gone to the world's command queue so far.</summary>
    public int Made { get; private set; }

    /// <summary>What the queue line says once it is over, or null while it runs.</summary>
    public string? Note { get; private set; }

    public void Start(string recipeId, int asked)
    {
        RecipeId = recipeId;
        Asked = Math.Max(0, asked);
        Left = Asked;
        Made = 0;
        Note = null;
    }

    /// <summary>Stop where it stands; what has already been submitted stays made.</summary>
    public void Cancel()
    {
        if (Running)
            Note = $"Stopped after {Made} of {Asked}.";
        Left = 0;
    }

    /// <summary>
    /// The next step, given how many whole pieces the supply still covers. A queue that cannot make one more piece stops itself and says so.
    /// </summary>
    public CraftStep Next(int canMake)
    {
        if (!Running)
            return CraftStep.Idle;
        if (canMake <= 0)
        {
            Note = $"Stopped after {Made} of {Asked}: the materials ran out.";
            Left = 0;
            return CraftStep.StoppedShort;
        }
        Left--;
        Made++;
        if (Left > 0)
            return CraftStep.MakeOne;
        Note = $"Made {Made} of {Asked}.";
        return CraftStep.Done;
    }
}
