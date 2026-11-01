using System.Numerics;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Capture;

namespace UNNAMED.Presentation.Tests;

public class CaptureReviewRegressionTests
{
    [Fact]
    public void PendingWriterKeepsWaitingAndSuccessfulWriterPreservesItsResult()
    {
        var pending = new TaskCompletionSource<ClipRecorder.Result>();
        Assert.Null(ClipRecorder.Result.Poll(pending.Task, "clip.avi"));
        var saved = new ClipRecorder.Result("clip.avi", 30, 44100, true, null);
        pending.SetResult(saved);
        Assert.Same(saved, ClipRecorder.Result.Poll(pending.Task, "clip.avi"));
    }

    [Fact]
    public void FaultedWriterReturnsAnErrorInsteadOfWaitingForever()
    {
        var fault = Task.FromException<ClipRecorder.Result>(new UnauthorizedAccessException("cleanup denied"));
        var result = ClipRecorder.Result.Poll(fault, "clip.avi");
        Assert.NotNull(result); Assert.False(result.Finalised); Assert.Equal("cleanup denied", result.Problem);
    }

    [Fact]
    public void CanceledWriterAlsoReleasesTheUi()
    {
        var canceled = Task.FromCanceled<ClipRecorder.Result>(new CancellationToken(true));
        var result = ClipRecorder.Result.Poll(canceled, "clip.avi");
        Assert.NotNull(result); Assert.False(result.Finalised); Assert.Contains("canceled", result.Problem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void StraightVerticalAimHasAFiniteOrthogonalUp(float y)
    {
        var direction = new Vector3(0, y, 0);
        var up = FilmOptions.CameraUp(direction);
        Assert.Equal(0, Vector3.Dot(up, direction)); Assert.Equal(1, up.Length());
        Assert.True(Vector3.Cross(direction, up).LengthSquared() > .9f);
    }

    [Fact]
    public void AShotKeepsItsUpAxisAcrossTheNearVerticalThreshold()
    {
        var up = FilmOptions.CameraUp(new Vector3(0, -1, 0));
        Assert.Equal(up, FilmOptions.CameraUp(new Vector3(.2f, -1, 0), up));
        Assert.Throws<ArgumentException>(() => FilmOptions.CameraUp(Vector3.Zero));
        Assert.Throws<ArgumentException>(() => FilmOptions.CameraUp(-Vector3.UnitY, Vector3.UnitY));
        Assert.Equal(Vector3.UnitX, FilmOptions.CameraUp(-Vector3.UnitY, Vector3.UnitX));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnsetCharacterUsesTheNormalSpec(string? csv)
    {
        var normal = CharacterSpec.Quick("Normal", null);
        Assert.Same(normal, FilmOptions.Character(csv, normal));
    }

    [Fact]
    public void ExplicitCharacterUsesAllSevenFields()
    {
        var spec = FilmOptions.Character("Neris,people.veth,body.veth_f.average,She,face.veth.f_marsh,hair.veth.f_marsh,1680", CharacterSpec.Quick("Normal", null));
        Assert.Equal("Neris", spec.Name); Assert.Equal(Pronouns.She, spec.Pronouns);
        Assert.Equal("people.veth", spec.PeopleId); Assert.Equal("body.veth_f.average", spec.BodyPresetId);
        Assert.Equal(1680, spec.Appearance!.StatureMm); Assert.Equal("face.veth.f_marsh", spec.Appearance.Face);
        Assert.Equal("hair.veth.f_marsh", spec.Appearance.Hair);
    }

    [Theory]
    [InlineData("Neris,people.veth,body.veth_f.average,She")]
    [InlineData("Neris,people.veth,body.veth_f.average,99,face.x,hair.x,1680")]
    [InlineData("Neris,people.veth,body.veth_f.average,She,face.x,hair.x,invalid")]
    [InlineData("Neris,people.veth,body.veth_f.average,She,face.x,hair.x,0")]
    public void MalformedExplicitCharacterCannotSilentlySwitchTheActor(string csv) =>
        Assert.Throws<ArgumentException>(() => FilmOptions.Character(csv, CharacterSpec.Quick("Normal", null)));
}
