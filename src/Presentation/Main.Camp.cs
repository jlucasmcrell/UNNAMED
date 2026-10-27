using Godot;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private CanvasLayer? _campReturnLayer;
    private Label? _campReturnWords;
    private void DrawCampReturn(Simulation simulation,CombatView combat)
    {
        bool show=combat.Defeated && !combat.Downed && combat.RespawnTicksLeft>0 && simulation.Camp is not null;
        if(show && _campReturnLayer is null)
        {
            _campReturnLayer=new CanvasLayer { Layer=120,Name="CampReturnChoice" };
            var root=new Control { MouseFilter=Control.MouseFilterEnum.Ignore };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var panel=new PanelContainer { AnchorLeft=.5f,AnchorRight=.5f,AnchorTop=.72f,AnchorBottom=.72f,
                OffsetLeft=-260,OffsetRight=260,OffsetTop=0,OffsetBottom=100,Theme=UiKit.Theme };
            var column=new VBoxContainer();
            _campReturnWords=UiKit.Words("",18,HudStyle.Bone);
            column.AddChild(_campReturnWords);
            var row=new HBoxContainer();
            var camp=new Button { Text="Return to camp" };
            camp.Pressed+=()=>_session.Submit(new CampReturnCommand(_session.Simulation!.PlayerId,true));
            var stone=new Button { Text="Bound Waystone" };
            stone.Pressed+=()=>_session.Submit(new CampReturnCommand(_session.Simulation!.PlayerId,false));
            row.AddChild(camp);row.AddChild(stone);column.AddChild(row);panel.AddChild(column);root.AddChild(panel);
            _campReturnLayer.AddChild(root);AddChild(_campReturnLayer);
        }
        if(_campReturnLayer is not null) _campReturnLayer.Visible=show;
        if(show) _campReturnWords!.Text=simulation.ReturnToCamp?"Returning to your camp":"Returning to your bound Waystone";
    }
}
