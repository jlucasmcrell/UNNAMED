namespace UNNAMED.Presentation.Ui.Models;

/// <summary>The water sound contract, observed from movement without changing simulation state.</summary>
public sealed class WaterMovement
{
    public string? State { get; private set; }
    public string? Transition { get; private set; }

    public void Observe(bool wet, bool swimming, float speed)
    {
        string? next = swimming ? speed > .25f ? "swim" : "tread" : wet ? "wade" : null;
        Transition = State is null && next is not null ? "enter_water"
            : State is not null && next is null ? "exit_water" : null;
        State = next;
    }
}
