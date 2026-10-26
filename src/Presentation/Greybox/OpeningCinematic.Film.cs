using Godot;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Greybox;

public partial class OpeningCinematic
{
    /// <summary>Reviewed real-game footage. Its soundtrack contains music/effects; the approved voices use the normal voice bus.</summary>
    public string? FilmFile { get; init; }
    public double HitSeconds => FilmFile is null ? OpeningWords.Hit : OpeningFilmWords.Hit;
    public double DurationSeconds => FilmFile is null ? OpeningWords.End : OpeningFilmWords.End;
    private VideoStreamPlayer? _film;
    private int _filmWarm, _filmSaid;
    private bool _filmDisabled3D;

    private void ReadyFilm()
    {
        _filmDisabled3D=GetViewport().Disable3D;
        _camera.Attributes=_exposure;AddChild(_camera);_camera.MakeCurrent();StartCamera.Track(this,_camera);
        _layer=new CanvasLayer {Layer=30,Visible=false};AddChild(_layer);
        var root=new ColorRect {Color=Colors.Black,MouseFilter=Control.MouseFilterEnum.Ignore};
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);_layer.AddChild(root);
        _film=new VideoStreamPlayer {Stream=new VideoStreamTheora {File=FilmFile!},Expand=true,
            Bus="Music",MouseFilter=Control.MouseFilterEnum.Ignore};
        root.AddChild(_film);
        void Fit()
        {
            float scale=Math.Min(root.Size.X/1920,root.Size.Y/1080);
            _film.Size=new Vector2(1920,1080)*scale;_film.Position=(root.Size-_film.Size)/2;
        }
        root.Resized+=Fit;Fit();
        _words=HudStyle.Text(HudStyle.Body,34,HudStyle.Bone);
        _words.HorizontalAlignment=HorizontalAlignment.Center;
        _words.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _words.AddThemeColorOverride("font_shadow_color",Colors.Black);
        _words.AddThemeConstantOverride("shadow_outline_size",10);
        _words.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _words.AnchorLeft=.08f;_words.AnchorRight=.92f;_words.AnchorTop=.82f;_words.AnchorBottom=.96f;
        root.AddChild(_words);
        _film.Finished+=()=>Finish(false);
    }

    private void ProcessFilm()
    {
        if(_film is null)return;
        if(_filmWarm==0)
        {
            if(_warm==0)Warming?.Invoke();
            int poses=_shots.Length*WarmAt.Length;
            if(_warm<poses*2)
            {
                var shot=_shots[_warm/2/WarmAt.Length];
                Pose(shot.From+(shot.Until-shot.From)*WarmAt[_warm/2%WarmAt.Length]);_warm++;return;
            }
            SkyView.Show(SkyView.ClockAtWorldStart);
            GetViewport().Disable3D=true;
            _filmWarm=1;return;
        }
        if(_filmWarm==1){_filmWarm=2;Warmed?.Invoke();return;}
        if(Holding)return;
        if(_clock<0)
        {
            _layer.Visible=true;_film.Play();Started?.Invoke();
            GetViewport().GuiReleaseFocus();
        }
        if(_leaving){Finish(true);return;}
        _clock=_film.StreamPosition;
        while(_filmSaid<OpeningFilmWords.Lines.Length&&_clock>=OpeningFilmWords.Lines[_filmSaid].From)
            Say?.Invoke(OpeningFilmWords.Lines[_filmSaid++].Voice);
        Words(_clock);_words.Visible=SettingsStore.Current.Profile.Interface.Subtitles;
        if(!_hit&&_clock>=OpeningFilmWords.Hit)
        {
            _hit=true;Named?.Invoke();GetViewport().Disable3D=_filmDisabled3D;
        }
        if(_clock>=OpeningFilmWords.End)Finish(false);
    }
}
