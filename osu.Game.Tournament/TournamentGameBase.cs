// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input;
using osu.Framework.Platform;
using osu.Framework.IO.Stores;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.IO;
using osu.Game.Tournament.Models;
using osu.Game.Users;
using osuTK.Input;

namespace osu.Game.Tournament
{
    [Cached(typeof(TournamentGameBase))]
    public class TournamentGameBase : OsuGameBase
    {
        private const string bracket_filename = "bracket.json";
        private LadderInfo ladder;
        private TournamentStorage storage;
        private DependencyContainer dependencies;
        private FileBasedIPC ipc;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            return dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        }

        [BackgroundDependencyLoader]
        private void load(Storage baseStorage)
        {
            Resources.AddStore(new DllResourceStore(typeof(TournamentGameBase).Assembly));

            dependencies.CacheAs(storage = new TournamentStorage(baseStorage));

            Textures.AddStore(new TextureLoaderStore(storage.VideoStore));

            readBracket();

            ladder.CurrentMatch.Value = ladder.Matches.FirstOrDefault(p => p.Current.Value);

            dependencies.CacheAs<MatchIPCInfo>(ipc = new FileBasedIPC());
            Add(ipc);
        }

        private void readBracket()
        {
            if (storage.Exists(bracket_filename))
            {
                using (Stream stream = storage.GetStream(bracket_filename, FileAccess.Read, FileMode.Open))
                using (var sr = new StreamReader(stream))
                    ladder = JsonConvert.DeserializeObject<LadderInfo>(sr.ReadToEnd());
            }

            ladder ??= new LadderInfo();
            ladder.Ruleset.Value ??= RulesetStore.AvailableRulesets.First();

            Ruleset.BindTo(ladder.Ruleset);

            dependencies.Cache(ladder);

            bool addedInfo = false;

            // assign teams
            foreach (var match in ladder.Matches)
            {
                match.Team1.Value = ladder.Teams.FirstOrDefault(t => t.Acronym.Value == match.Team1Acronym);
                match.Team2.Value = ladder.Teams.FirstOrDefault(t => t.Acronym.Value == match.Team2Acronym);

                foreach (var conditional in match.ConditionalMatches)
                {
                    conditional.Team1.Value = ladder.Teams.FirstOrDefault(t => t.Acronym.Value == conditional.Team1Acronym);
                    conditional.Team2.Value = ladder.Teams.FirstOrDefault(t => t.Acronym.Value == conditional.Team2Acronym);
                    conditional.Round.Value = match.Round.Value;
                }
            }

            // assign progressions
            foreach (var pair in ladder.Progressions)
            {
                var src = ladder.Matches.FirstOrDefault(p => p.ID == pair.SourceID);
                var dest = ladder.Matches.FirstOrDefault(p => p.ID == pair.TargetID);

                if (src == null)
                    continue;

                if (dest != null)
                {
                    if (pair.Losers)
                        src.LosersProgression.Value = dest;
                    else
                        src.Progression.Value = dest;
                }
            }

            // link matches to rounds
            foreach (var round in ladder.Rounds)
            {
                foreach (var id in round.Matches)
                {
                    var found = ladder.Matches.FirstOrDefault(p => p.ID == id);

                    if (found != null)
                    {
                        found.Round.Value = round;
                        if (round.StartDate.Value > found.Date.Value)
                            found.Date.Value = round.StartDate.Value;
                    }
                }
            }

            addedInfo |= addPlayers();
            addedInfo |= addBeatmaps();

            if (addedInfo)
                SaveChanges();
        }

        /// <summary>
        /// Add missing player info based on user IDs.
        /// </summary>
        /// <returns></returns>
        private bool addPlayers()
        {
            bool addedInfo = false;

            foreach (var t in ladder.Teams)
            {
                foreach (var p in t.Players)
                {
                    if (string.IsNullOrEmpty(p.Username) || p.Statistics == null)
                    {
                        PopulateUser(p);
                        addedInfo = true;
                    }
                }
            }

            return addedInfo;
        }

        /// <summary>
        /// Add missing beatmap info based on beatmap IDs
        /// </summary>
        private bool addBeatmaps()
        {
            bool addedInfo = false;

            foreach (var r in ladder.Rounds)
            {
                foreach (var b in r.Beatmaps.ToList())
                {
                    if (b.BeatmapInfo != null)
                        continue;

                    if (b.ID > 0)
                    {
                        var req = new GetBeatmapRequest(new BeatmapInfo { OnlineBeatmapID = b.ID });
                        API.Perform(req);
                        b.BeatmapInfo = req.Result?.ToBeatmap(RulesetStore);

                        addedInfo = true;
                    }

                    if (b.BeatmapInfo == null)
                        // if online population couldn't be performed, ensure we don't leave a null value behind
                        r.Beatmaps.Remove(b);
                }
            }

            foreach (var t in ladder.Teams)
            {
                foreach (var s in t.SeedingResults)
                {
                    foreach (var b in s.Beatmaps)
                    {
                        if (b.BeatmapInfo == null && b.ID > 0)
                        {
                            var req = new GetBeatmapRequest(new BeatmapInfo { OnlineBeatmapID = b.ID });
                            req.Perform(API);
                            b.BeatmapInfo = req.Result?.ToBeatmap(RulesetStore);

                            addedInfo = true;
                        }
                    }
                }
            }

            return addedInfo;
        }

        public void PopulateUser(User user, Action success = null, Action failure = null)
        {
            var req = new GetUserRequest(user.Id, Ruleset.Value);

            req.Success += res =>
            {
                user.Username = res.Username;
                user.Statistics = res.Statistics;
                user.Country = res.Country;
                user.Cover = res.Cover;

                success?.Invoke();
            };

            req.Failure += _ =>
            {
                user.Id = 1;
                failure?.Invoke();
            };

            API.Queue(req);
        }

        protected override void LoadComplete()
        {
            MenuCursorContainer.Cursor.AlwaysPresent = true; // required for tooltip display

            // we don't want to show the menu cursor as it would appear on stream output.
            MenuCursorContainer.Cursor.Alpha = 0;

            base.LoadComplete();
        }

        protected virtual void SaveChanges()
        {
            foreach (var r in ladder.Rounds)
                r.Matches = ladder.Matches.Where(p => p.Round.Value == r).Select(p => p.ID).ToList();

            ladder.Progressions = ladder.Matches.Where(p => p.Progression.Value != null).Select(p => new TournamentProgression(p.ID, p.Progression.Value.ID)).Concat(
                                            ladder.Matches.Where(p => p.LosersProgression.Value != null).Select(p => new TournamentProgression(p.ID, p.LosersProgression.Value.ID, true)))
                                        .ToList();

            using (var stream = storage.GetStream(bracket_filename, FileAccess.Write, FileMode.Create))
            using (var sw = new StreamWriter(stream))
            {
                sw.Write(JsonConvert.SerializeObject(ladder,
                    new JsonSerializerSettings
                    {
                        Formatting = Formatting.Indented,
                        NullValueHandling = NullValueHandling.Ignore,
                        DefaultValueHandling = DefaultValueHandling.Ignore,
                    }));
            }
        }

        protected override UserInputManager CreateUserInputManager() => new TournamentInputManager();

        private class TournamentInputManager : UserInputManager
        {
            protected override MouseButtonEventManager CreateButtonEventManagerFor(MouseButton button)
            {
                switch (button)
                {
                    case MouseButton.Right:
                        return new RightMouseManager(button);
                }

                return base.CreateButtonEventManagerFor(button);
            }

            private class RightMouseManager : MouseButtonEventManager
            {
                public RightMouseManager(MouseButton button)
                    : base(button)
                {
                }

                public override bool EnableDrag => true; // allow right-mouse dragging for absolute scroll in scroll containers.
                public override bool EnableClick => true;
                public override bool ChangeFocusOnClick => false;
            }
        }
    }
}
