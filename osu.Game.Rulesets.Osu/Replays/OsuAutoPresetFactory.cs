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
                        ConfigFollowSlider = true
                    };
                
                case Preset.CubicSplineConstrained:
                    return new OsuAutoEnginePlayerlike(beatmap, mods)
                    {
                        ConfigFollowSlider = true,
                        
                        ConfigReactionTime = 450,
                        ConfigReleaseDelay = 120,
                        ConfigReleaseWait = 350,
                        
                        ConfigDoSpline = true,
                        ConfigDoSplineBounce = true,
                        
                        ConfigCoerceCircles = false,
                        ConfigCoerceSliders = false
                    };
                
                case Preset.Playerlike:
                    return new OsuAutoEnginePlayerlike(beatmap, mods)
                    {
                        ConfigFollowSlider = false, // Normal-ish sliders are pretty closely followed by the interpolator anyway
                        
                        ConfigReactionTime = -0.5, // Assume that the player remembers the patterns
                        ConfigReleaseDelay = 120,
                        ConfigReleaseWait = 200,
                        
                        ConfigDoSpline = true,
                        ConfigDoSplineBounce = false,
                        
                        ConfigCoerceCircles = true,
                        ConfigCoerceSliders = true
                    };
                
                case Preset.PlayerlikeCheater:
                    return new OsuAutoEnginePlayerlike(beatmap, mods)
                    {
                        ConfigFollowSlider = true,
                        
                        ConfigDoSpline = true,
                        ConfigDoSplineBounce = true,
                        
                        ConfigCoerceCircles = true,
                        ConfigCoerceSliders = false // Slider coercing is broken with slider following
                    };
                
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
