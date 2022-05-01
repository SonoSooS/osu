using System;
using System.Collections.Generic;
using System.ComponentModel;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Osu.Replays
{
    public static class OsuAutoPresetFactory
    {
        public static OsuAutoGeneratorBase CreateAutoplayEngine(
            IBeatmap beatmap, IReadOnlyList<Mod> mods,
            Preset selectedPreset = Preset.Default)
        {
            switch(selectedPreset)
            {
                case Preset.AbstractTimingDebug:
                    return new OsuAutoGeneratorATE(beatmap, mods)
                    {
                        ConfigFollowSlider = false
                    };
                
                case Preset.CubicSplineConstrained:
                case Preset.Playerlike:
                case Preset.PlayerlikeCheater:
                default:
                    return new OsuAutoGenerator(beatmap, mods);
            }
        }
        
        public enum Preset
        {
            /// <summary>
            /// Default osu! autoplay engine
            /// </summary>
            [Description("Default")]
            Default,
            
            /// <summary>
            /// Used for debugging Abstract Timing Event generation
            /// </summary>
            [Description("Stiff")]
            AbstractTimingDebug,
            
            /// <summary>
            /// Simple cubic spline interpolation with border bounce
            /// </summary>
            [Description("Playful")]
            CubicSplineConstrained,
            
            /// <summary>
            /// Player-like behavior, with hit circle bundling, lazy spam sliders, otherwise slider follow
            /// </summary>
            [Description("Player-like")]
            Playerlike,
            
            /// <summary>
            /// Player-like behavior, with *some* hit circle bundling, no lazy sliders, and slider unfollow
            /// </summary>
            [Description("Rule breaker")]
            PlayerlikeCheater
        }
    }
}
