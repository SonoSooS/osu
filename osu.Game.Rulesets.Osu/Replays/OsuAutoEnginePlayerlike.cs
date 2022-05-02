using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Osu.Replays;
using osuTK;

using SplineInterpolator = osu.Game.Rulesets.Osu.Utils.SplineInterpolator;

using Log = osu.Framework.Logging.Logger;

namespace osu.Game.Rulesets.Osu.Replays
{
    internal class OsuAutoEnginePlayerlike : OsuAutoGeneratorATE
    {
        /// <summary>
        /// Determines the treshold below which the interpolator would break down.
        /// If this value is nonzero, then it will be attempted to
        ///  "train" the interpolator with all the position values, so
        ///  the interpolation angles come out properly after the short period has ended.
        /// </summary>
        const double DIFF_SMALL = 5;
        /// <summary>
        /// Sets interpolation rendering time step.
        /// Setting this to a high frequency timestep will greatly slow down generation, as
        ///  the interpolator used is much slower than a simple tweener.
        /// </summary>
        const double DIFF_STEP = 25; // Not 60FPS, but good enough compromise for speed
        /// <summary>
        /// Sets the treshold below which no interpolation is done, to
        ///  prevent the cursor from becoming a light-speed rubber band.
        /// Setting this value too high will prevent interpolation where there should be, but
        ///  setting it too low will cause random jumps/stutters/glitches in the cursor movement.
        /// </summary>
        const double DIFF_TRESHOLD = 80; // Prevents the cursor from jittering across the screen at light speed
        
        
        public bool ConfigDoSpline = true;
        public bool ConfigDoSplineBounce = true;
        public bool ConfigCoerceCircles = true;
        public bool ConfigCoerceSliders = true;
        
        
        public OsuAutoEnginePlayerlike(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            : base(beatmap, mods)
        {
            
        }

        protected override void TreeToReplay(List<AbstractEventFrame> frames)
        {
            List<OsuReplayFrame> originalReplay = BackupAndClearReplay();
            
            base.TreeToReplay(frames);
            
            List<OsuReplayFrame> generatedSimpleReplay = BackupAndClearReplay();
            
            RestoreBaseReplay(originalReplay);
            
            if(ConfigDoSpline)
            {
                GenerateSpline(generatedSimpleReplay);
            }
            else
            {
                RestoreGeneratedReplay(generatedSimpleReplay);
            }
            
        }
        
        private void GenerateSpline(List<OsuReplayFrame> frames)
        {
            Log.Log("Starting spline generation");
            
            SortedDictionary<double, double> cursorX = new SortedDictionary<double, double>();
            SortedDictionary<double, double> cursorY = new SortedDictionary<double, double>();
            
            List<OsuReplayFrame> sameTimeFrames = new List<OsuReplayFrame>();
            double sameTime = frames[0].Time;
            
            cursorX[sameTime - 1000] = frames[0].Position.X;
            cursorY[sameTime - 1000] = frames[0].Position.Y;
            
            int frameCount = frames.Count;
            for(int i = 0; i < frameCount; i++)
            {
                OsuReplayFrame currentFrame = frames[i];
                
                if(currentFrame.Time == sameTime)
                {
                    sameTimeFrames.Add(currentFrame);
                    continue;
                }
                
                int previousFrameCount = sameTimeFrames.Count;
                if(previousFrameCount == 1)
                {
                    OsuReplayFrame previousFrame = sameTimeFrames[0];
                    
                    Vector2 framePos = previousFrame.Position;
                    double frameTime = previousFrame.Time;
                    
                    cursorX[frameTime] = framePos.X;
                    cursorY[frameTime] = framePos.Y;
                }
                else
                {
                    /*
                    if(DIFF_SMALL > 0)
                    {
                        double ratioMulti = 1.0 / previousFrameCount;
                        double totalDistance = currentFrame.Time - sameTime;
                        if(totalDistance > DIFF_SMALL)
                            totalDistance = DIFF_SMALL;
                        
                        for(int j = 0; j < previousFrameCount; j++)
                        {
                            OsuReplayFrame previousFrame = sameTimeFrames[j];
                            
                            Vector2 framePos = previousFrame.Position;
                            double frameTime = sameTime + ((j * ratioMulti) * totalDistance);
                            
                            cursorX[frameTime] = framePos.X;
                            cursorY[frameTime] = framePos.Y;
                        }
                    }
                    else*/ // To prevent light speed cursor flying, just use a single position instead
                    {
                        OsuReplayFrame previousFrame = sameTimeFrames[sameTimeFrames.Count - 1];
                        
                        Vector2 framePos = previousFrame.Position;
                        double frameTime = previousFrame.Time;
                        
                        cursorX[frameTime] = framePos.X;
                        cursorY[frameTime] = framePos.Y;
                    }
                }
                
                sameTimeFrames.Clear();
                
                sameTime = currentFrame.Time;
                sameTimeFrames.Add(currentFrame);
            }
            
            {
                OsuReplayFrame previousFrame = sameTimeFrames[sameTimeFrames.Count - 1];
                
                Vector2 framePos = previousFrame.Position;
                double frameTime = previousFrame.Time;
                
                cursorX[frameTime] = framePos.X;
                cursorY[frameTime] = framePos.Y;
                
                cursorX[frameTime + 1000] = framePos.X;
                cursorY[frameTime + 1000] = framePos.Y;
                
                cursorX[frameTime + 2000] = framePos.X;
                cursorY[frameTime + 2000] = framePos.Y;
                
                sameTimeFrames.Clear();
            }
            
            {
                double stepTime = DIFF_STEP;
                double stepTreshold = Math.Max(DIFF_TRESHOLD, stepTime);
                
                int lightspeedCounter = 0;
                
                Log.Log("Generating spline lookup tables...");
                
                SplineInterpolator interpX = new SplineInterpolator(cursorX);
                SplineInterpolator interpY = new SplineInterpolator(cursorY);
                
                Log.Log("Interpolating...");
                
                AddFrameToReplay(frames[0]);
                
                OsuReplayFrame previousFrame = frames[0];
                for(int i = 1; i < frameCount; i++)
                {
                    OsuReplayFrame currentFrame = frames[i];
                    
                    if((i + 1) < frameCount && (frames[i + 1].Time - currentFrame.Time) < stepTreshold)
                        lightspeedCounter = 3; // Frame before lightspeed + lightspeed + exit from light speed
                    
                    if(lightspeedCounter == 0)
                    {
                        double interpTime = previousFrame.Time;
                        
                        OsuAction[] actions = previousFrame.Actions.ToArray();
                        
                        do
                        {
                            AddFrameToReplay(new OsuReplayFrame
                            (
                                interpTime,
                                new Vector2
                                (
                                    (float)interpX.GetValue(interpTime),
                                    (float)interpY.GetValue(interpTime)
                                ),
                                actions
                            ));
                            
                            interpTime += stepTime;
                        }
                        while(interpTime < currentFrame.Time);
                    }
                    else
                    {
                        --lightspeedCounter;
                    }
                    
                    AddFrameToReplay(currentFrame);
                    
                    previousFrame = currentFrame;
                }
                
                AddFrameToReplay(new OsuReplayFrame
                (
                    previousFrame.Time + 1000,
                    previousFrame.Position
                ));
                
                Log.Log("Interpolation done!");
            }
        }
    }
}
