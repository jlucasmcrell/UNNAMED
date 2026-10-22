using Godot;
using System.Text.Json;
using System.Text.RegularExpressions;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in review of actual World.Advance news. Never used by a normal launch.
    private async Task RunAwayReview(string directory,bool complete=true)
    {
        var simulation=_session.Simulation!;
        long span=9*_session.Setup.Clock.TicksPerDay+_session.Setup.Clock.TicksPerDay/4;
        var log=simulation.Advance(span);
        var summary=AwayOf(log.FromTick,log.ToTick,log.Lines,false);
        if(!log.Lines.Any(l=>l.Kind==World.Runtime.AdventurerAdvanceKinds.Claimed))
            throw new InvalidDataException("Away gate saw no real contract news");
        ShowAway(summary);
        var rows=new List<object>();
        foreach(var size in new[]{new Vector2I(1920,1080),new Vector2I(1280,720)})
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
            _awaySheet.Show(summary);
            await Rcd3Wait(.5);
            if(DisplayServer.WindowGetSize()!=size)
                throw new InvalidDataException("Away window size did not match requested client pixels: "+DisplayServer.WindowGetSize());
            await Rcd3Shot(directory,"away_"+size.Y+"_summary");
            var expand=_awaySheet.FindChildren("*",nameof(Button),true,false).OfType<Button>()
                .SingleOrDefault(b=>b.Text.StartsWith("Show all events",StringComparison.Ordinal));
            if(expand is null)throw new InvalidDataException("Away digest has no detail action");
            expand.EmitSignal(Button.SignalName.Pressed);
            await Rcd3Wait(.3);
            var labels=_awaySheet.FindChildren("*",nameof(Label),true,false).OfType<Label>().Select(l=>l.Text).ToArray();
            foreach(string line in labels)
                if(Regex.IsMatch(line,@"(?i)\bslot\s+\w|\b[a-z_]+\.[a-z0-9_.]+"))
                    throw new InvalidDataException("Raw ID in rendered away sheet: "+line);
            await Rcd3Shot(directory,"away_"+size.Y+"_all");
            rows.Add(new{width=size.X,height=size.Y,summary.Head,preview_lines=summary.Groups.Sum(g=>g.Lines.Length),
                full_lines=summary.DetailGroups.Sum(g=>g.Lines.Length),labels});
        }
        File.WriteAllText(Path.Combine(directory,"away_news.json"),JsonSerializer.Serialize(new{log.FromTick,log.ToTick,log.Lines,views=rows},new JsonSerializerOptions{WriteIndented=true}));
        if(complete)File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"status\":\"AWAY_UI_REQUIRES_REVIEW\"}");
    }
}
