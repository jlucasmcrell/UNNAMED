using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

/// <summary>PJ10 §6.2 and §6.3.1: "Saving…" while a save is written, long enough to be seen, and a failed save said until one succeeds.</summary>
public class SaveIndicatorModelTests
{
    [Fact]
    public void NothingIsSaid_UntilASaveStarts()
    {
        var model = new SaveIndicatorModel();
        model.Update(0, saving: false, succeeded: false, failed: null);
        Assert.Null(model.Text);
    }

    [Fact]
    public void AFastSave_IsStillShownForTheMinimum_ThenGoes()
    {
        var model = new SaveIndicatorModel();
        model.Update(10.0, saving: true, succeeded: false, failed: null);
        Assert.Equal(SaveIndicatorModel.Saving, model.Text);
        model.Update(10.1, saving: false, succeeded: true, failed: null);
        Assert.Equal(SaveIndicatorModel.Saving, model.Text);
        model.Update(10.0 + SaveIndicatorModel.MinimumShownSeconds + 0.01, saving: false, succeeded: false, failed: null);
        Assert.Null(model.Text);
    }

    [Fact]
    public void ALongSave_IsShownForAsLongAsItTakes()
    {
        var model = new SaveIndicatorModel();
        model.Update(0, saving: true, succeeded: false, failed: null);
        model.Update(5, saving: true, succeeded: false, failed: null);
        Assert.Equal(SaveIndicatorModel.Saving, model.Text);
        Assert.False(model.Warning);
    }

    [Fact]
    public void AFailedSave_StaysSaid_ThroughLaterSavesBeingWritten_UntilOneSucceeds()
    {
        var model = new SaveIndicatorModel();
        model.Update(0, saving: true, succeeded: false, failed: null);
        model.Update(1, saving: false, succeeded: false, failed: "Not saved: the disk is full.");
        Assert.Equal(("Not saved: the disk is full.", true), (model.Text, model.Warning));
        model.Update(600, saving: false, succeeded: false, failed: null);
        Assert.Equal("Not saved: the disk is full.", model.Text);   // a toast would have faded; this does not
        model.Update(630, saving: true, succeeded: false, failed: null);
        Assert.Equal("Not saved: the disk is full.", model.Text);   // trying again is not yet kept
        model.Update(630.5, saving: false, succeeded: true, failed: null);
        Assert.Equal((SaveIndicatorModel.Saving, false), (model.Text, model.Warning));   // kept: the failure goes, the minimum runs out
        model.Update(640, saving: false, succeeded: false, failed: null);
        Assert.Null(model.Text);
    }
}
