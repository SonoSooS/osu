using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Osu.Replays;
using osuTK;


using OsuCircle = osu.Game.Rulesets.Osu.Objects.HitCircle;
using OsuSlider = osu.Game.Rulesets.Osu.Objects.Slider;
using OsuSpinner = osu.Game.Rulesets.Osu.Objects.Spinner;

using Log = osu.Framework.Logging.Logger;


namespace osu.Game.Rulesets.Osu.Replays
{
    public class OsuAutoGeneratorATE : OsuAutoGeneratorBase
    {
        #region Constants
        /// <summary>
        /// Reaction time to unfollow a slider if a hit object comes on during a slider slide
        /// </summary>
        protected const double SLIDER_UNFOLLOW_TIMEOUT = 150;
        
        /// <summary>
        /// Slider follow point generation sampling rate.
        /// High sampling rate increase generation time, but
        ///  low sampling rate reduces following accuracy (makes jagged lines instead of smooth curves)
        /// </summary>
        protected const double SLIDER_FOLLOW_TICKS = 1000.0 / 60;
        #endregion
        
        #region Config
        /// <summary>
        /// Enforce strict slider following if the slider is not interrupted during its slide.
        /// If not enabled, only the necessary slider points are hit.
        /// </summary>
        public bool ConfigFollowSlider = true;
        
        /// <summary>
        /// Only release the held button after this much time has elapsed,
        ///  and there are no hit objects within that timeframe
        /// </summary>
        public double ConfigReleaseWait = 350;
        /// <summary>
        /// If releasing the held button is possible, keep holding it for this much before it's released
        /// </summary>
        public double ConfigReleaseDelay = 120;
        /// <summary>
        /// Amount of time before the next hit object is "noticed"
        /// </summary>
        public double ConfigReactionTime = 150;
        /// <summary>
        /// If the waiting time between the last object and the next one is less than this value, then
        ///  special control points are added to prevent any interpolation from freaking out
        /// </summary>
        public double ConfigReactionAntiRebindTime = 180;
        /// <summary>
        /// The amount of time between two control points.
        /// Smaller values stop the cursor faster.
        /// </summary>
        public double ConfigReactionAntiRebindOffset = 50;
        #endregion
        
        #region Constructor and base class stuff
        public OsuAutoGeneratorATE(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            : base(beatmap, mods)
        {
            
        }
        
        public override Replay Generate()
        {
            if (Beatmap.HitObjects.Count == 0)
                return Replay;
            
            //HACK: there has to be a better way to get scaled difficulty values
            {
                OsuHitObject firstRealObject = Beatmap.HitObjects.Cast<OsuHitObject>().FirstOrDefault(obj => obj is OsuCircle || obj is OsuSlider);
                
                if(firstRealObject != null)
                {
                    if(ConfigReactionTime < 0)
                        ConfigReactionTime = -ConfigReactionTime * firstRealObject.TimePreempt;
                }
                else
                {
                    if(ConfigReactionTime < 0)
                        ConfigReactionTime = 100;
                }
            }
            
            List<AbstractEventFrame> frames = GenerateTree();
            if(frames.Count == 0)
                return Replay;
            
            TreeToReplay(frames);
            
            return Replay;
        }
        #endregion
        
        #region Child class helpers
        public List<OsuReplayFrame> BackupAndClearReplay()
        {
            List<OsuReplayFrame> originalReplay = new List<OsuReplayFrame>(Frames.Count);
            originalReplay.AddRange(Frames.Cast<OsuReplayFrame>());
            
            Frames.Clear();
            
            return originalReplay;
        }
        
        public void RestoreBaseReplay(List<OsuReplayFrame> frames)
        {
            Frames.Clear();
            
            Frames.AddRange(frames);
        }
        
        public void RestoreGeneratedReplay(List<OsuReplayFrame> frames)
        {
            int frameCount = frames.Count;
            
            for(int i = 0; i < frameCount; i++)
                AddFrameToReplay(frames[i]);
        }
        #endregion
        
        #region Basic replay generator
        protected virtual void TreeToReplay(List<AbstractEventFrame> frames)
        {
            bool? isLeft = null;
            bool canRelease = false;
            
            bool isSpinning = false;
            
            AbstractEventFrame lastFrame = frames[0];
            AddFrameToReplay(new OsuReplayFrame(lastFrame.Time, lastFrame.Position));
            
            Vector2 lastNonSpinnerPos = SPINNER_CENTRE;
            
            for(int i = 1; i < frames.Count; i++)
            {
                AbstractEventFrame frame = frames[i];
                    
                if(isSpinning)
                {
                    isLeft = isLeft ?? true;
                    
                    double timeStart = lastFrame.Time;
                    double timeEnd = frame.Time;
                    
                    while(timeStart < timeEnd)
                    {
                        Vector2 spinnerPos = CirclePosition(timeStart, SPIN_RADIUS) + SPINNER_CENTRE;
                        lastNonSpinnerPos = spinnerPos;
                        
                        AddFrameToReplay(new OsuReplayFrame(timeStart, spinnerPos, ToAction(isLeft)));
                        
                        timeStart += 15;
                    }
                }
                
                if(canRelease && (frame.Time - lastFrame.Time) > (ConfigReleaseWait + ConfigReactionTime/* + ConfigReactionAntiRebindTime*/))
                {
                    double time0 = lastFrame.Time + ConfigReleaseDelay;
                    
                    AddFrameToReplay(new OsuReplayFrame(time0, lastNonSpinnerPos));
                    isLeft = null;
                    canRelease = false;
                    
                    if(ConfigReactionTime > 0)
                    {
                        double timeUntilNextFrame = frame.Time - lastFrame.Time;
                        double timeUntilNextFrameAfterRelease = timeUntilNextFrame - ConfigReleaseDelay;
                        
                        if(timeUntilNextFrame > ConfigReactionTime &&
                            timeUntilNextFrameAfterRelease > ConfigReactionTime)
                        {
                            double time1 = lastFrame.Time + ConfigReleaseDelay + ConfigReactionAntiRebindOffset;
                            double time2 = frame.Time - ConfigReactionTime - ConfigReactionAntiRebindOffset;
                            double time3 = frame.Time - ConfigReactionTime;
                            
                            if(time0 < time3)
                            {
                                if(time1 < time3)
                                {
                                    AddFrameToReplay(new OsuReplayFrame(time1, lastNonSpinnerPos));
                                    
                                    if(time1 < time2)
                                        AddFrameToReplay(new OsuReplayFrame(time2, lastNonSpinnerPos));
                                }
                                
                                AddFrameToReplay(new OsuReplayFrame(time3, lastNonSpinnerPos));
                            }
                        }
                    }
                }
                
                if(frame.IsSpinnerStart)
                {
                    isLeft = isLeft ?? true;
                    isSpinning = true;
                    canRelease = false;
                    
                    // Do not process spinner start event any further, as
                    //  the position field is invalid
                    lastFrame = frame;
                    continue;
                }
                
                if(frame.IsSpinnerEnd)
                {
                    // Do not process the spinner end event any further, as
                    //  the position field is invalid
                    isSpinning = false;
                    canRelease = true;
                    lastFrame = frame;
                    continue;
                }
                
                if(frame.IsEngage || (!isLeft.HasValue && frame.IsHold))
                {
                    isLeft = !isLeft ?? true;
                    canRelease = false;
                }
                
                if(frame.IsRelease || (frame.IsCircleHit && !frame.IsHold))
                {
                    canRelease = true;
                }
                
                AddFrameToReplay(new OsuReplayFrame(frame.Time, frame.Position, ToAction(isLeft)));
                
                
                lastFrame = frame;
                lastNonSpinnerPos = frame.Position;
            }
        }
        
        private static OsuAction[] ToAction(bool? isLeft)
        {
            if(!isLeft.HasValue)
                return new OsuAction[0];
            
            if(isLeft.Value)
                return new[] { OsuAction.LeftButton };
            else
                return new[] { OsuAction.RightButton };
        }
        #endregion
        
        #region HitObject to Abstract Timing Event calculation
        private List<AbstractEventFrame> GenerateTree()
        {
            List<AbstractEventFrame> frames = new List<AbstractEventFrame>();

            var hitObjects = Beatmap.HitObjects;
            int homCount = hitObjects.Count;
            
            if(homCount == 0)
                return frames;
            
            // Add dummy frame
            frames.Add(new AbstractEventFrame()
            {
                Time = hitObjects[0].StartTime - 1000,
                Position = new Vector2(256, 384)
            });
            
            //TODO: make sure hit objects are sorted, as the syntax tree builder's quirk workarounds need the hit objects to come in order!
            //TODO: move local variables outside of this function, so copypaste code snippets can be extruded into a function
            //TODO: implement slider following
            
            
            List<SliderTracker> sliders = new List<SliderTracker>();
            
            // Keeps track of when the last spinner ends
            double? spinnerEnd = null;
            // Last time the sliders were updated
            double? lastSliderTime = null;
            // Whether slider following is currently engage-able or not
            bool isSliderFollowing = ConfigFollowSlider;
            
            //HACK: remove the extra loop after the refactor
            for(int _homIndex = 0; _homIndex <= homCount; _homIndex++)
            {
                OsuHitObject hitObject;
                double time;
                
                if(_homIndex != homCount)
                {
                    hitObject = (OsuHitObject)hitObjects[_homIndex];
                    time = hitObject.StartTime;
                }
                else
                {
                    hitObject = (OsuHitObject)hitObjects[homCount - 1];
                    
                    if(hitObject is OsuSlider endSlider)
                        time = endSlider.EndTime + 1;
                    else if(hitObject is OsuSpinner endSpinner)
                        time = endSpinner.EndTime + 1;
                    else
                        time = hitObjects[homCount - 1].GetEndTime() + 1; //HACK: why stupid hardcoded offset...
                    
                    hitObject = null;
                }
                
                
                
                // We spinned out the last spinner in the spinner stack
                if(spinnerEnd.HasValue && (time > spinnerEnd.Value || hitObject == null))
                {
                    AddFrame(frames, new AbstractEventFrame()
                    {
                        Time = spinnerEnd.Value,
                        IsSpinnerEnd = true
                    });
                    
                    // Set no spinner in action
                    spinnerEnd = null;
                }
                
                // Update sliders as needed
                if(lastSliderTime.HasValue && (time > lastSliderTime.Value || hitObject == null))
                {
                    double lastUpdateTime = time;
                    
                    if(hitObject == null)
                    {
                        // The last hit object's end time might be smaller than the true last hit object time
                        lastUpdateTime = sliders.Select(tracker => tracker.Slider.EndTime).Max() + 1;
                    }
                    
                    // Note: it doesn't matter in which order we iterate the sliders, as
                    //  due to the strict frame sorting, the events will
                    //  end up in the correct place anyways...
                    
                    // Note: this loop modifies the list it's iterating, so
                    //  index incrementation is handled manually inside
                    //  the function body
                    for(int i = 0; i < sliders.Count; /* !!! do not increment here !!! */)
                    {
                        SliderTracker tracker = sliders[i];
                        
                        // Disable slider following if the current hit object
                        //  were to happen during slider slide (that is, before the slider end)
                        if(isSliderFollowing && time < tracker.Slider.EndTime)
                            isSliderFollowing = false;
                        
                        while(tracker.NextUpdateTime < lastUpdateTime)
                        {
                            double lastTime = tracker.NextUpdateTime;
                            Vector2 lastPos = tracker.NextPosition;
                            
                            // Note: it's valid to place slider following code here, as
                            //  there are checks to prevent hit objects interrupting slider following
                            if(isSliderFollowing)
                            {
                                double sliderStart = tracker.Slider.StartTime;
                                double sliderLength = tracker.Slider.EndTime - sliderStart;
                                
                                double followingTime = Math.Max(lastSliderTime.Value, sliderStart) + SLIDER_FOLLOW_TICKS;
                                
                                
                                while(followingTime < lastTime)
                                {
                                    double progress = (followingTime - sliderStart)  / sliderLength;
                                    
                                    Vector2 posAtProgress = tracker.Slider.StackedPositionAt(progress);
                                    
                                    AddFrame(frames, new AbstractEventFrame()
                                    {
                                        Time = followingTime,
                                        Position = posAtProgress,
                                        
                                        IsSliderSlide = true
                                    });
                                    
                                    followingTime += SLIDER_FOLLOW_TICKS; //TODO: adjust framerate based on mods
                                }
                            }
                            
                            AddFrame(frames, new AbstractEventFrame()
                            {
                                Time = lastTime,
                                Position = lastPos,
                                
                                IsSliderTick = true,
                                IsSliderSlide = true
                            });
                            
                            if(tracker.UpdateNextTime()) // Slider still has remaining ticks
                            {
                                //TODO: add slider following if this is the only slider
                            }
                            else // Slider is out of ticks, end it
                            {
                                if(lastTime < tracker.NextUpdateTime)
                                {
                                    AddFrame(frames, new AbstractEventFrame()
                                    {
                                        Time = tracker.Slider.EndTime,
                                        Position = tracker.Slider.StackedEndPosition,
                                        
                                        IsSliderEnd = true,
                                        IsSliderTick = true // Workaround for weird misses
                                    });
                                }
                                
                                // Signal slider end
                                tracker = null;
                                break;
                            }
                        }
                        
                        if(tracker == null) // Signal that the current slider has finished
                        {
                            sliders.RemoveAt(i);
                            // Do not increment, next index became current index
                            continue;
                        }
                        else
                        {
                            // Increment to next index, as the collection is unmodified
                            ++i;
                            continue;
                        }
                    }
                    
                    if(sliders.Count != 0)
                    {
                        lastSliderTime = lastUpdateTime;
                    }
                    else
                    {
                        lastSliderTime = null;
                        
                        // Reset slider followability
                        isSliderFollowing = ConfigFollowSlider;
                    }
                }
                
                if(hitObject == null)
                    break;
                
                if(hitObject is OsuSpinner spinner)
                {
                    if(!spinnerEnd.HasValue) // Empty spinner stack
                    {
                        // Create spinner state
                        spinnerEnd = spinner.EndTime;
                        
                        AddFrame(frames, new AbstractEventFrame()
                        {
                            Time = spinner.StartTime,
                            IsSpinnerStart = true
                        });
                    }
                    else // Spinner stack not empty, let's extend the duration!
                    {
                        //ASSERT: time <= spinnerEnd.Value
                        
                        // spinnerEnd = max(spinnerEnd, spinner.EndTime)
                        if(spinner.EndTime > spinnerEnd.Value)
                            spinnerEnd = spinner.EndTime;
                    }
                    
                    continue;
                }
                
                
                if(hitObject is OsuSlider slider)
                {
                    sliders.Add(new SliderTracker(slider));
                    
                    AddFrame(frames, new AbstractEventFrame()
                    {
                        Time = slider.StartTime,
                        Position = slider.StackedPosition,
                        
                        IsCircleHit = true,
                        IsSliderSlide = true,
                        IsSliderTick = true, // Entry tick,
                        
                        Combo = _homIndex//hitObject.IndexInCurrentCombo + 1
                    });
                    
                    if(!lastSliderTime.HasValue)
                    {
                        // Enable slider follow checking
                        lastSliderTime = slider.StartTime;
                    }
                    else
                    {
                        // Can't follow multiple sliders at once
                        isSliderFollowing = false;
                    }
                    
                    continue;
                }
                
                AddFrame(frames, new AbstractEventFrame()
                {
                    Time = hitObject.StartTime,
                    Position = hitObject.StackedPosition,
                    
                    IsCircleHit = true,
                    
                    Combo = _homIndex//hitObject.IndexInCurrentCombo + 1
                });
            }
            
            FixupFrames(frames);
            
            return frames;
        }
        
        private static void AddFrame(List<AbstractEventFrame> frames, AbstractEventFrame frame)
        {
            int index = frames.BinarySearch(frame, AbstractEventFrame.Comparer);
            if(index < 0)
                index = ~index;
            
#if DEBUG
            //Log.Log("HOM " + index.ToString("000") + ": " + frame);
#endif
            
            frames.Insert(index, frame);
        }
        
        private static void FixupFrames(List<AbstractEventFrame> frames)
        {
            AbstractEventFrame lastFrame = frames[0];
            
            uint sliderCount = 0; // Can't overflow due to C# array index limit
            
            for(int i = 1; i < frames.Count; i++)
            {
                AbstractEventFrame currentFrameRef = frames[i];
                
#if DEBUG
                Log.Log("ROM " + i.ToString("00000") + ": " + currentFrameRef);
#endif
                
                // On slider entry
                if(currentFrameRef.IsSliderTick && currentFrameRef.IsSliderSlide && currentFrameRef.IsCircleHit)
                    ++sliderCount;
                
                if(currentFrameRef.IsSliderEnd)
                    --sliderCount;
                
                if(sliderCount != 0)
                {
                    currentFrameRef.IsSliderSlide = true;
                }
            }
        }
        
        
        //HACK: this class is awful, and can break in any update, especially if LegacyLastTick gets deprecated in the slider code
        private class SliderTracker
        {
            public readonly OsuSlider Slider;
            
            public double NextUpdateTime;
            public Vector2 NextPosition;
            
            private readonly double? LegacyScoringTime;
            private readonly Vector2? LegacyScoringPos;
            private bool HasSentLegacyScoringPos = false;
            
            private int UpdateIndex;
            private readonly Vector2 PositionOffset;
            
            
            
            public SliderTracker(OsuSlider slider)
            {
                this.Slider = slider;
                
                this.UpdateIndex = 0;
                this.NextUpdateTime = slider.StartTime;
                this.NextPosition = slider.StackedPosition;
                
                // Slider ticks are stored as Position, but for gameplay we need StackedPosition
                this.PositionOffset = slider.StackedPosition - slider.Position;
                
                if(Slider.LegacyLastTickOffset.HasValue && Slider.LegacyLastTickOffset.Value > 0)
                {
                    double timeLength = Slider.EndTime - Slider.StartTime;
                    double progress = (timeLength - Slider.LegacyLastTickOffset.Value) / timeLength;
                    
                    if(progress < 0.5)
                        progress = 0.5; // Max(timeLength / 2, timeLength - 36)
                    
                    Vector2 posAtTime;
                    /*
                    if(Slider.RepeatCount % 2 == 0)
                        posAtTime = Slider.Path.PositionAt(progress) + Slider.StackedPosition;
                    else
                        posAtTime = Slider.Path.PositionAt(1.0 - progress) + Slider.StackedPosition;
                    */
                    
                    posAtTime = Slider.StackedPositionAt(progress);
                    
                    
                    LegacyScoringTime = Slider.StartTime + (timeLength * progress);
                    LegacyScoringPos = posAtTime;
                }
                else
                {
                    LegacyScoringTime = null;
                    LegacyScoringPos = null;
                }
            }
            
            public bool UpdateNextTime()
            {
                while(true)
                {
                    if(++UpdateIndex >= Slider.NestedHitObjects.Count)
                    {
                        if(LegacyScoringTime.HasValue && !HasSentLegacyScoringPos)
                        {
                            // Prevent infinite looping
                            HasSentLegacyScoringPos = true;
                        }
                        else
                        {
                            NextUpdateTime = Slider.EndTime;
                            NextPosition = Slider.StackedEndPosition;
                            
                            return false;
                        }
                    }
                    
                    HitObject childHom;
                    double homTime;
                    Vector2 homPos;
                    
                    if(UpdateIndex < Slider.NestedHitObjects.Count)
                    {
                        childHom = Slider.NestedHitObjects[UpdateIndex];
                        
                        if(childHom is SliderTick || childHom is SliderRepeat)
                        {
                            homTime = childHom.StartTime;
                            homPos = ((OsuHitObject)childHom).Position + PositionOffset; //HACK: this is nasty
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        childHom = null;
                        
                        homTime = Slider.EndTime;
                        homPos = Slider.StackedEndPosition;
                    }
                    
                    
                    if(LegacyScoringTime.HasValue)
                    {
                        if(LegacyScoringTime.Value > NextUpdateTime &&
                            homTime > LegacyScoringTime.Value)
                        {
                            --UpdateIndex;
                            
                            NextUpdateTime = LegacyScoringTime.Value;
                            NextPosition = LegacyScoringPos.Value;
                            
                            HasSentLegacyScoringPos = true;
                            
                            return true;
                        }
                    }
                    
                    if(childHom != null)
                    {
                        NextUpdateTime = homTime;
                        NextPosition = homPos;
                        
                        return true;
                    }
                }
            }
        }
        #endregion
        
        protected class AbstractEventFrame : IComparable<AbstractEventFrame>
        {
            public static readonly IComparer<AbstractEventFrame> Comparer = Comparer<AbstractEventFrame>.Default;
            
            public double Time = double.MinValue;
            public Vector2 Position = default; // Not valid if only IsSpinner or IsSpinnerEnd is set
            
            public bool IsEngage => IsCircleHit;
            public bool IsHold => IsSliderSlide /*|| IsSliderTick*/ || IsSpinnerStart;
            public bool IsRelease => (IsCircleHit || IsSliderEnd || IsSpinnerEnd) && !IsHold;
            
            public bool IsCircleHit = false;
            public bool IsSliderSlide = false;
            public bool IsSliderTick = false;
            public bool IsSliderEnd = false;
            public bool IsSpinnerStart = false;
            public bool IsSpinnerEnd = false;
            
            public int Combo = 0;
            
            //FIXME: there is a slider end miss when same-frame hit circle happens
            int IComparable<AbstractEventFrame>.CompareTo(AbstractEventFrame other)
            {
                // Frames are sorted by time first
                
                if(this.Time > other.Time)
                    return 1;
                
                if(this.Time < other.Time)
                    return -1;
                
                // Spinners have the highest priority, as we want them to be
                //  on the first frame of the time collision, so
                //  the spinner is actually getting spinned
                if(this.IsSpinnerStart != other.IsSpinnerStart)
                    return this.IsSpinnerStart ? -1 : 1; // [!] Inverted [!] (start events come first)
                
                // Circle hit should come first, as
                //  holding is more important than a zero-frame click
                if(this.IsCircleHit != other.IsCircleHit)
                    return this.IsCircleHit ? -1 : 1; // [!] Inverted [!] (click events are lower prio than slider hold)
                
                // Workaround for Classic mode: same-frame clicks must be sorted by combo due to note lock
                
                if(this.Combo != other.Combo)
                {
                    // Combo index of zero should come last in the chain.
                    // Due to the check to this if case,
                    //  it's guaranteed that only one of the compared frames could contain a zero
                    
                    if(this.Combo == 0) // Other frame is non-zero index
                        return 1;       // Zero combo comes last, we're "bigger"
                    
                    if(other.Combo == 0) // We have non-zero combo index, we come first
                        return -1; // Zero comes last, so we come first instead
                    
                    // Neither combos are zero, so just order them normally
                    
                    if(this.Combo > other.Combo)
                        return 1;
                    
                    if(this.Combo < other.Combo)
                        return -1;
                }
                
                // Slider tick and slider end have the same priority
                
                if(this.IsSliderTick != other.IsSliderTick)
                    return this.IsSliderTick ? 1 : -1;
                
                if(this.IsSliderEnd != other.IsSliderEnd)
                    return this.IsSliderEnd ? 1 : -1;
                
                // Prefer sliding the slider, and popping out to hit troll circles
                if(this.IsSliderSlide != other.IsSliderSlide)
                    return this.IsSliderSlide ? 1 : -1;
                
                // Spinner end has the lowest priority, as
                //  everything before it requires a key hold anyway.
                // Its priority is also normal, so a spinner end gets placed last.
                if(this.IsSpinnerEnd != other.IsSpinnerEnd)
                    return this.IsSpinnerEnd ? 1 : -1;
                
                // Should only happen on circle spam
                return 0;
            }

            public override string ToString()
            {
                int action = 0;
                
                if(IsCircleHit)
                    action |= 1;
                if(IsSliderSlide)
                    action |= 2;
                if(IsSliderTick)
                    action |= 4;
                if(IsSliderEnd)
                    action |= 8;
                if(IsSpinnerStart)
                    action |= 16;
                if(IsSpinnerEnd)
                    action |= 32;
                
                string actionStr = null;
                
                switch(action)
                {
                    case 0: actionStr = "NOP"; break;
                    case 1: actionStr = "CircleHit"; break;
                    case 7: actionStr = "SliderStart"; break;
                    case 2: actionStr = "SliderSlide"; break;
                    case 6: actionStr = "SliderTick"; break;
                    case 12: actionStr = "SliderEnd"; break;
                    case 16: actionStr = "SpinnerStart"; break;
                    case 32: actionStr = "SpinnerEnd"; break;
                    
                    default:
                        return "{" + Time + ",\t" + Position + "\t" + Combo + "\t|\t" + string.Join(",\t", new[] {IsCircleHit, IsSliderSlide, IsSliderTick, IsSliderEnd, IsSpinnerStart, IsSpinnerEnd}.Select(b => b.ToString())) + "}";
                }
                
                return "{" + Time + ", \t" + Position + "\t" + Combo + "\t|\t" + actionStr + "}";
            }
        }
    }
}
