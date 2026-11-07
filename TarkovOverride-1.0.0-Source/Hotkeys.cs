using System;
using System.Collections.Generic;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed class HotkeyConfig
    {
        public string OpenSettings = "Insert";
        public string AimHold = "Mouse4";
        public string ToggleEnemyESP = "F6";
        public string ToggleLootESP = "F7";
        public string ToggleAimbot = "F8";
        public string Panic = "F10";
        public string ReloadConfig = "F9";
        public string HeadAimHold = "None";
        public string IgnoreTarget = "None";
        public string ClearIgnoredTargets = "None";
        public string ToggleNightVision = "None";
        public string ToggleThermalVision = "None";
        public string ToggleNoFlash = "None";
        public string ToggleNoVisor = "None";
        public string ToggleNoClip = "None";
        public string NoClipForward = "W";
        public string NoClipBack = "S";
        public string NoClipLeft = "A";
        public string NoClipRight = "D";
        public string NoClipUp = "Space";
        public string NoClipDown = "C";
        public string NoClipBoost = "LeftShift";
        public string ToggleInfiniteMedical = "None";
        public string ToggleNoWeight = "None";
        public string ToggleInteractionRange = "None";
        public string ToggleFireRate = "None";
        public string ToggleGrenadePreview = "None"; // Accepted only for older JSON files; no action is registered.
        public string ToggleEnemyAlerts = "None";
        public string ToggleAvailableExtractions = "None";
        public string RepairEquippedGear = "None";
        public Dictionary<string, KeyChord> Parse()
        {
            var result = new Dictionary<string, KeyChord>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var flightUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in typeof(HotkeyConfig).GetFields())
            {
                if(field.Name=="ToggleGrenadePreview")continue; // Removed feature: ignore even an old conflicting binding.
                var chord = KeyChord.Parse((string)field.GetValue(this),field.Name.StartsWith("NoClip"));
                var context=field.Name.StartsWith("NoClip")?flightUsed:used;
                if (chord.Main != KeyCode.None && !context.Add(chord.Identity))
                    throw new FormatException("Duplicate hotkey: " + field.Name + ". Use None to disable a binding.");
                result.Add(field.Name, chord);
            }
            return result;
        }
    }
    public sealed class KeyChord
    {
        private static readonly KeyCode[] ModifierKeys = { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift,
            KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand };
        public KeyCode Main;
        public KeyCode[] Modifiers;
        public string Identity;
        public static KeyChord Parse(string value,bool allowModifierMain=false)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("Hotkeys cannot be blank; use None.");
            var parsed = new List<KeyCode>();
            foreach (string part in value.Split('+'))
            {
                KeyCode code;
                string token = part.Trim(); int numeric;
                if (token.Length == 0 || int.TryParse(token, out numeric) ||
                    !Enum.TryParse<KeyCode>(token, true, out code) || !Enum.IsDefined(typeof(KeyCode), code))
                    throw new FormatException("Unknown Unity key name: " + token);
                if (parsed.Contains(code)) throw new FormatException("Repeated key in chord: " + value);
                parsed.Add(code);
            }
            KeyCode main = parsed[parsed.Count - 1]; parsed.RemoveAt(parsed.Count - 1);
            foreach (KeyCode modifier in parsed)
                if (!IsModifier(modifier)) throw new FormatException("Only Control, Shift, Alt or Command keys can precede the final key: " + value);
            if ((main == KeyCode.None && parsed.Count != 0) || (IsModifier(main)&&!allowModifierMain))
                throw new FormatException("Use a regular key or mouse button as the final key: " + value);
            parsed.Sort(); string identity = main.ToString();
            foreach (KeyCode modifier in parsed) identity += "+" + modifier;
            return new KeyChord { Main = main, Modifiers = parsed.ToArray(), Identity = identity };
        }
        private static bool IsModifier(KeyCode key)
        {
            return key == KeyCode.LeftControl || key == KeyCode.RightControl || key == KeyCode.LeftShift ||
                key == KeyCode.RightShift || key == KeyCode.LeftAlt || key == KeyCode.RightAlt ||
                key == KeyCode.LeftCommand || key == KeyCode.RightCommand;
        }
        private bool ModifiersHeld(bool exact)
        {
            foreach (KeyCode key in Modifiers) if (!Input.GetKey(key)) return false;
            // Exact modifiers prevent F6 and LeftControl+F6 from firing together.
            foreach (KeyCode key in ModifierKeys)
                if (exact && Input.GetKey(key) && Array.IndexOf(Modifiers, key) < 0) return false;
            return true;
        }
        public bool Down() { return Main != KeyCode.None && Input.GetKeyDown(Main) && ModifiersHeld(true); }
        public bool Held() { return Main != KeyCode.None && Input.GetKey(Main) && ModifiersHeld(false); }
    }
}

