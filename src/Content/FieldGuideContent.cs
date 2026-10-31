// UNNAMED Content - the Field Guide's own entries (SYS-166): what the world does differently, in the world's voice
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Guidance;

namespace UNNAMED.Content;

/// <summary>
/// The Field Guide's entries that are not hints. A hint teaches the moment once; these are what a player comes back to when the
/// moment has passed and the question remains. Written in the voice the owner asked for: plain, unhurried, and allowed a dry joke -
/// a guide written by someone who lives here, not a manual written by someone who does not.
/// </summary>
public static class FieldGuideContent
{
    /// <summary>Every entry, in the order the sheet reads them. Sections group the index; titles are what a search sorts by.</summary>
    public static readonly ImmutableArray<GuideEntry> Entries = ImmutableArray.Create(
        new GuideEntry("Getting started", "F1 opens this guide",
            "You are reading it. Type a word to narrow the list - every word you type must appear somewhere, so 'guard noise' " +
            "finds only the entry that mentions both. Escape closes it, and nothing in here pauses the world."),

        new GuideEntry("Getting started", "The pack holds what your back holds",
            "Weight, not slots. Go over the limit and you slow down, and the game will tell you so in the same flat tone it uses " +
            "for rain: this is a fact about your back, not a judgement about your character."),

        new GuideEntry("Fighting", "Guard, and the cost of guarding",
            "Hold the guard and blows land on your arms instead of your ribs. Guard drains stamina; guard with nothing left in the " +
            "tank and it drops, which is the world's way of saying you should have moved."),

        new GuideEntry("Fighting", "XP debt: what happens when you go down",
            "A death is not a restart. Your skills lose a little ground and you carry the debt until it is worked off by doing the " +
            "thing that got you killed, better. Skills remember effort, so being carried out of a barrow is not the same as " +
            "learning nothing in it."),

        new GuideEntry("Fighting", "Noise is a real thing you make",
            "Metal on metal is loud. So is running in boots on stone, and a dropped pot, and a conversation held at a volume its " +
            "listeners did not choose. Things that hunt you have ears; the quiet option usually exists and is usually slower."),

        new GuideEntry("Fighting", "Bleeding, and what stops it",
            "A wound keeps taking what it takes until it is seen to. Cloth and a moment of standing still will do; a salve does it " +
            "faster and costs more. It is entirely normal to lose a fight you were winning because you did not stop to look down."),

        new GuideEntry("Surviving", "Hunger is slow and it is not a timer",
            "Eat before the number reaches the bottom, not after. Game, roots and anything a settlement will sell you all count; " +
            "raw meat is food with a consequence attached, and the consequence is named on the item."),

        new GuideEntry("Surviving", "Night, dark and a light",
            "Dark is not an effect. Carry a light into a hole, or wait for daylight, or do not go. The things that live in the dark " +
            "see a torch from a long way off, so a lamp is both a tool and an announcement."),

        new GuideEntry("Surviving", "Wet and cold take it out of you",
            "Wet clothes in a wind are worse than no clothes in still air. Shelter, a fire or simply leaving the rain will stop the " +
            "drain; the weather in this world is a simulation and it does not care whether you were about to swing a pick."),

        new GuideEntry("Making", "Skills go up by being used",
            "There is no experience for something you have not done. Chop wood at a stump long enough and wood asks less of you; the " +
            "practice is the progression, and the level is only the receipt."),

        new GuideEntry("Making", "Stations are places, not menus",
            "A forge, a bench, a kiln: walk to it and use it. The difference between a good tool and a bad one is often which of " +
            "these you were standing at, and some recipes will not hear of being made on a rock."),

        new GuideEntry("Travelling", "Waystones are how the map works",
            "Find one and it remembers you; you can return to any stone you have touched. They are tall, they hum, and standing " +
            "near one at night is the closest this world offers to a safe answer."),

        new GuideEntry("Travelling", "Talking to people",
            "Every settlement assumes you are busy, so nobody will explain themselves twice. Ask again if you need it: the dialogue " +
            "keeps what it told you, and the Field Guide keeps what it taught you."),

        new GuideEntry("Travelling", "Quitting, and coming back",
            "Saving happens where you sleep and where the game says so. Quit anywhere else and the world keeps what it last " +
            "recorded, which may be your last bed and not your last fight. The save name carries the place so you know what you lost."),

        new GuideEntry("Companions", "Companions are people, with their own schedule",
            "They fight beside you, get tired, get hurt, and will not follow you off a cliff they can see. If one of them goes down " +
            "it is a wound and not a death; stand over them long enough and they will stand up again."),

        new GuideEntry("Companions", "Being downed does not end the run",
            "You are downed, not dead. Time, a hand or a salve brings you back, and being downed costs XP debt instead of a save " +
            "load, which is the world's opinion that an unlucky fight is not a mistake worth starting over for."),

        new GuideEntry("The world", "Everything places the same day as you",
            "The people, the weather, the fire, the market: they all advance whether or not you are looking. Come back to a hearth " +
            "at midnight and it is midnight there too, and the person who was carrying a bucket at noon is somewhere else now."),

        new GuideEntry("The world", "This world does three things differently",
            "Debt instead of levels lost outright, a world that is simulated rather than scripted around you, and no tutorial that " +
            "stops the game. If one of those is the thing you came here for, it is not a shortcut: it is the design."),

        new GuideEntry("Reading this", "Nothing here spoils anything",
            "The guide only holds what you have already met. If a question has no entry, the answer is somewhere out there, in a " +
            "place whose name you have heard and whose road you have not walked."),

        new GuideEntry("Reading this", "Text too small",
            "Text scale is in the display settings and goes to 200 per cent, which this sheet was measured at. Nothing in the guide " +
            "is a tooltip: it does not fade, it does not chase the cursor, and it does not need to be read before a fight."));
}
