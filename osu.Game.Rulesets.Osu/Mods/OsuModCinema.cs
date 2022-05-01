// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;

namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModCinema : ModCinema<OsuHitObject>
    {
        public override Type[] IncompatibleMods => base.IncompatibleMods.Concat(new[] { typeof(OsuModMagnetised), typeof(OsuModAutopilot), typeof(OsuModSpunOut) }).ToArray();
        
        [SettingSource("Autoplay preset", "Influences how the cursor moves in general, and how it behaves around circles and sliders.")]
        public Bindable<OsuAutoPresetFactory.Preset> AutoplayPreset { get; } = new Bindable<OsuAutoPresetFactory.Preset>();
        
        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new ModReplayData(
                    OsuAutoPresetFactory.CreateAutoplayEngine(beatmap, mods, AutoplayPreset.Value).Generate(),
                    new ModCreatedUser { Username = "Autoplay" }
                );
    }
}
