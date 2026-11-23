// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RelicMods.Manager
{
    public static class Categorizer
    {
        public static readonly string[] Names = { "Frameworks", "Gameplay", "User Interface", "Items", "Armour", "Weapons", "Spells", "Skills", "Crafting", "Buildings", "Companions", "Visuals", "Audio", "Animations", "Utilities", "Save Files", "Miscellaneous" };

        static readonly Dictionary<string, string[]> Words = new Dictionary<string, string[]>
        {
            { "User Interface", new[] { "ui", "hud", "menu", "menus", "interface", "map", "minimap", "journal", "tooltip", "tooltips", "quest log", "compass", "health bar", "health bars", "hp numbers", "hp", "numbers", "bars", "fonts", "font", "inventory screen", "hotbar", "icons", "window", "windows", "spell wheel", "spell book", "legend", "crosshair", "nameplate", "markers" } },
            { "Gameplay", new[] { "gameplay", "combat", "difficulty", "enemies", "enemy", "boss", "bosses", "perk", "perks", "trait", "traits", "origins", "class", "classes", "rebalance", "balance", "stamina", "damage", "xp", "experience", "raid", "raids", "injury", "survival", "hunger", "world edits", "respawn", "god", "gods", "shrine", "shrines", "prayer", "faith", "blessing", "blessings", "favour", "quest", "quests", "trader" } },
            { "Items", new[] { "item", "items", "storage", "chest", "chests", "bag", "bags", "loot", "pickup", "pick-up", "pick up", "stack", "stacks", "food", "potion", "potions", "inventory", "carry", "slots", "magnetic" } },
            { "Armour", new[] { "armour", "armor", "cape", "capes", "helmet", "outfit", "outfits", "robe", "clothing", "shield", "rings", "amulet" } },
            { "Weapons", new[] { "weapon", "weapons", "sword", "swords", "bow", "bows", "staff", "staves", "axe", "crossbow", "dagger", "mace", "wand", "claws", "spear", "hammer", "warhammer" } },
            { "Spells", new[] { "spell", "spells", "magic", "rune", "runes", "totem", "totems", "cooldown", "cooldowns", "teleport", "lightning" } },
            { "Skills", new[] { "skill", "skills", "mining", "woodcutting", "fishing", "cooking", "thieving", "lockpick", "lockpicking", "pickpocket", "pickpocketing", "steal", "stealing", "farming", "smithing", "levels", "level up" } },
            { "Crafting", new[] { "craft", "crafting", "recipe", "recipes", "workbench", "bench", "forge", "jeweler", "sockets", "gems" } },
            { "Buildings", new[] { "building", "buildings", "buildable", "construction", "base", "blueprint", "blueprints", "structure", "decoration", "decorations", "furniture", "banner", "banners", "walls", "house", "mining nodes" } },
            { "Companions", new[] { "companion", "companions", "pet", "pets", "follower", "followers", "summon", "summons", "workers", "marriage", "wedding", "spouse", "shadow clone" } },
            { "Visuals", new[] { "graphics", "visual", "visuals", "texture", "textures", "lighting", "reshade", "shader", "fog", "photo mode", "photomode", "camera", "fov", "effects", "colour", "color", "display", "fireworks", "celebration" } },
            { "Audio", new[] { "audio", "sound", "sounds", "music", "ambience", "ambient", "footsteps", "voice", "voices", "fanfare", "volume" } },
            { "Animations", new[] { "animation", "animations", "pose", "poses", "emote", "emotes" } },
            { "Utilities", new[] { "tool", "tools", "utility", "utilities", "dump", "debug", "console", "cheat", "cheats", "spawner", "spawn", "editor", "keybind", "keybinds", "library", "framework", "loader", "api", "modder's resource", "resource", "data", "settings", "config", "achievements" } },
            { "Save Files", new[] { "save", "saves", "savegame", "save file", "character save" } },
        };

        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "UI", "User Interface" }, { "HUD", "User Interface" }, { "Interface", "User Interface" }, { "User interface", "User Interface" },
            { "Armor", "Armour" }, { "Armour and Clothing", "Armour" }, { "Clothing", "Armour" }, { "Misc", "Miscellaneous" }, { "Miscellaneous", "Miscellaneous" },
            { "Utilities", "Utilities" }, { "Modders Resources", "Utilities" }, { "Modder's Resources", "Utilities" }, { "Visuals and Graphics", "Visuals" },
            { "Audio", "Audio" }, { "Sound", "Audio" }, { "Magic", "Spells" }, { "Saved Games", "Save Files" }, { "Saves", "Save Files" },
        };

        public static string Normalize(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return null;
            cat = cat.Trim();
            if (Aliases.TryGetValue(cat, out string a)) return a;
            var hit = Names.FirstOrDefault(n => n.Equals(cat, StringComparison.OrdinalIgnoreCase));
            return hit ?? cat;
        }

        public static string Guess(string name, string text, string kind) => Guess(name, text, kind, out _);

        public static string Guess(string name, string text, string kind, out double best)
        {
            best = 0;
            var score = new Dictionary<string, double>();
            void Scan(string s, double w)
            {
                if (string.IsNullOrEmpty(s)) return;
                string l = " " + Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9' \-]+", " ") + " ";
                foreach (var kv in Words)
                    foreach (string word in kv.Value)
                    {
                        int n = Regex.Matches(l, @"(?<=[ \-])" + Regex.Escape(word) + @"(?=[ \-])").Count;
                        if (n > 0) score[kv.Key] = (score.TryGetValue(kv.Key, out double v) ? v : 0) + w * Math.Min(n, 3);
                    }
            }
            Scan(Regex.Replace(name ?? "", @"([a-z])([A-Z])", "$1 $2"), 4);
            Scan(text, 1);
            if ((kind ?? "") == "Pak mod" && !score.Any()) { best = 2; return "Visuals"; }
            if (!score.Any()) return null;
            var top = score.OrderByDescending(kv => kv.Value).ToList();
            best = top[0].Value - (top.Count > 1 ? top[1].Value / 2 : 0);
            return top[0].Value >= 2 ? top[0].Key : null;
        }
    }
}
