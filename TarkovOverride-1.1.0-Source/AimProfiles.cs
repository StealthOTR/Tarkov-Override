using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<AimMode> activeAimMode;
        private ConfigEntry<bool> rageSnap,rageHeadOnly;
        private readonly Dictionary<AimMode,Dictionary<string,ConfigEntryBase>> aimProfiles = new Dictionary<AimMode,Dictionary<string,ConfigEntryBase>>();
        private readonly Dictionary<ConfigEntryBase,int> profileBuckets = new Dictionary<ConfigEntryBase,int>();
        private readonly Dictionary<ConfigEntryBase,string> profileFields = new Dictionary<ConfigEntryBase,string>();
        private readonly Dictionary<string,FieldInfo> aimFields = new Dictionary<string,FieldInfo>();
        private sealed class LegacyAimUiHint {public bool? Browsable=false;}
        private static ConfigDescription AimDescription(string section,string key,string description,AcceptableValueBase limits)
        {
            bool legacy=(section.StartsWith("03 ")&&key!="Enabled")||section.StartsWith("09 ");
            return legacy?new ConfigDescription(description,limits,new LegacyAimUiHint()):new ConfigDescription(description,limits);
        }
        private void BindAimProfiles()
        {
            activeAimMode=Setting("11 Aim modes","Active mode",AimMode.Legit,"Only this profile runs. F8 toggles the active profile; enabling a mode in its own tab selects it.");
            string[] fields={"visibility","adsOnly","sticky","circle","excludeGroup","pmcs","scavs","others","drawTarget","aimRange","fov","smoothing","maxTurn","bone","priority","activation","showAimStatus","switchDelay","fallback","correction","switchSeconds","correctionAngle","ignoredTypes"};
            MethodInfo bind=null;
            foreach(var method in typeof(ConfigFile).GetMethods())
            {
                var parameters=method.GetParameters();
                if(method.Name=="Bind"&&method.IsGenericMethodDefinition&&parameters.Length==4&&parameters[0].ParameterType==typeof(string)&&parameters[3].ParameterType==typeof(ConfigDescription)) {bind=method; break;}
            }
            if(bind==null) throw new MissingMethodException("BepInEx generic configuration binding was not found.");
            foreach(AimMode mode in Enum.GetValues(typeof(AimMode)))
            {
                var entries=new Dictionary<string,ConfigEntryBase>(); aimProfiles[mode]=entries;
                foreach(string name in fields)
                {
                    var field=typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance); aimFields[name]=field;
                    var source=(ConfigEntryBase)field.GetValue(this);
                    bool advanced=source.Definition.Section.StartsWith("09 ");
                    string section=mode==AimMode.Legit?(advanced?"13 Legit Aim - Advanced":"12 Legit Aim - Target"):(advanced?"15 Rage Aim - Advanced":"14 Rage Aim - Target");
                    var limits=source.Description.AcceptableValues;
                    if(mode==AimMode.Rage&&name=="fov") limits=new AcceptableValueRange<float>(1,180);
                    if(mode==AimMode.Rage&&name=="maxTurn") limits=new AcceptableValueRange<float>(10,14400);
                    if(mode==AimMode.Rage&&name=="aimRange") limits=new AcceptableValueRange<float>(5,1500);
                    string help=source.Description.Description;
                    if(mode==AimMode.Rage&&name=="fallback")help+=" Strict head targeting overrides body fallback when Head is selected.";
                    var description=new ConfigDescription(help,limits);
                    var entry=(ConfigEntryBase)bind.MakeGenericMethod(source.SettingType).Invoke(Config,new object[]{section,source.Definition.Key,AimProfileDefaults.Value(mode,name,source.BoxedValue),description});
                    entries[name]=entry; profileFields[entry]=name;
                    bool right=advanced?name!="fallback":name=="smoothing"||name=="maxTurn"||name=="sticky"||name=="circle"||name=="drawTarget"||name=="showAimStatus";
                    profileBuckets[entry]=(mode==AimMode.Legit?4:6)+(right?1:0);
                }
                AimMode captured=mode;
                ((ConfigEntry<string>)entries["ignoredTypes"]).SettingChanged+=delegate {if(activeAimMode.Value==captured) ignoredTerms=((ConfigEntry<string>)entries["ignoredTypes"]).Value.Split(',');};
            }
            rageSnap=Setting("15 Rage Aim - Advanced","Instant lock",true,"Immediately rotate onto the selected target while activated. Disable to use the Rage profile's smoothing and turn-speed controls. Does not fire automatically.");
            profileBuckets[rageSnap]=7;
            rageHeadOnly=Setting("15 Rage Aim - Advanced","Strict head targeting",true,"Force Rage Aim to use Head only, overriding Target bone and disabling body fallback. Uses one live model-head transform. Invalid or blocked head data is skipped. Does not block firing.");
            profileBuckets[rageHeadOnly]=6;
            BindRageTracking();
            activeAimMode.SettingChanged+=delegate {ApplyAimProfile();};
            ApplyAimProfile();
        }
        private void ApplyAimProfile()
        {
            var entries=aimProfiles[activeAimMode.Value];
            foreach(var pair in entries) aimFields[pair.Key].SetValue(this,pair.Value);
            ignoredTerms=ignoredTypes.Value.Split(','); locked=null; hasAimPoint=false; switchAt=-1;
        }
        private AimMode WindowAimMode {get {return compactTab==3?AimMode.Rage:AimMode.Legit;}}
        private bool AimTab {get {return compactTab==2||compactTab==3;}}
        private T WindowAimValue<T>(string name)
        {return (T)aimProfiles[WindowAimMode][name].BoxedValue;}
        private bool ProfileField(ConfigEntryBase entry,string field)
        {string name; return profileFields.TryGetValue(entry,out name)&&name==field;}
        private void EnableWindowAim(bool enabled)
        {if(enabled) activeAimMode.Value=WindowAimMode; aim.Value=enabled; locked=null; switchAt=-1;}
    }
}
