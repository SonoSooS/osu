using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Mods;
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
        public bool ConfigFollowSlider = true;
        
        public OsuAutoGeneratorATE(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            : base(beatmap, mods)
        {
            
        }
        
        public override Replay Generate()
        {
            if (Beatmap.HitObjects.Count == 0)
                return Replay;
            
            List<AbstractEventFrame> frames = GenerateTree();
            if(frames.Count == 0)
                return Replay;
            
            bool? isLeft = null;
            bool canRelease = false;
            
            bool isSpinning = false;
            
            AbstractEventFrame lastFrame = frames[0];
            
            for(int i = 1; i < frames.Count; i++)
            {
                AbstractEventFrame frame = frames[i];
                
                if(frame.IsSpinnerStart)
                {
                    isLeft = !isLeft ?? true;
                    isSpinning = true;
                    
                    // Do not process spinner start event any further, as
                    //  the position field is invalid
                    lastFrame = frame;
                    continue;
                }
                    
                if(isSpinning)
                {
                    double timeStart = lastFrame.Time;
                    double timeEnd = frame.Time;
                    
                    while(timeStart < timeEnd)
                    {
                        AddFrameToReplay(new OsuReplayFrame(timeStart, CirclePosition(timeStart, 3) + SPINNER_CENTRE, ToAction(isLeft)));
                        
                        timeStart += 1;
                    }
                }
                
                if(frame.IsSpinnerEnd)
                {
                    // Do not process the spinner end event any further, as
                    //  the position field is invalid
                    isSpinning = false;
                    lastFrame = frame;
                    continue;
                }
                
                if(canRelease && (frame.Time - lastFrame.Time) > 150) //TODO: unhardcode
                {
                    AddFrameToReplay(new OsuReplayFrame(lastFrame.Time + 50, lastFrame.Position));
                    isLeft = null;
                }
                
                
                if(frame.IsEngage || (frame.IsHold && !isLeft.HasValue))
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
            }
            
            return Replay;
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
            
            //HACK: remove the extra loop after the refactor
            for(int _homIndex = 0; _homIndex <= homCount; _homIndex++)
            {
                HitObject hitObject;
                double time;
                
                if(_homIndex != homCount)
                {
                    hitObject = hitObjects[_homIndex];
                    time = hitObject.StartTime;
                }
                else
                {
                    hitObject = hitObjects[homCount - 1];
                    
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
                    double lastUpdateTime = lastSliderTime.Value;
                    
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
                        
                        while(tracker.NextUpdateTime < lastUpdateTime)
                        {
                            double lastTime = tracker.NextUpdateTime;
                            Vector2 lastPos = tracker.NextPosition;
                            
                            if(tracker.UpdateNextTime()) // Slider still has remaining ticks
                            {
                                AddFrame(frames, new AbstractEventFrame()
                                {
                                    Time = lastTime,
                                    Position = lastPos,
                                    
                                    IsSliderTick = true,
                                    IsSliderSlide = true
                                });
                                
                                //TODO: add slider following if this is the only slider
                            }
                            else // Slider is out of ticks, end it
                            {
                                if(tracker.Slider.LegacyLastTickOffset.HasValue && tracker.Slider.LegacyLastTickOffset > 0)
                                {
                                    double timeLength = tracker.Slider.EndTime - tracker.Slider.StartTime;
                                    double progress = (timeLength - tracker.Slider.LegacyLastTickOffset.Value) / timeLength;
                                    
                                    if(progress < 0.5)
                                        progress = 0.5; // Max(timeLength / 2, timeLength - 36)
                                    
                                    {
                                        Vector2 posAtTime;
                                        if(tracker.Slider.RepeatCount % 2 == 0)
                                            posAtTime = tracker.Slider.Path.PositionAt(progress) + tracker.Slider.StackedPosition;
                                        else
                                            posAtTime = tracker.Slider.Path.PositionAt(1.0 - progress) + tracker.Slider.StackedPosition;
                                        
                                        AddFrame(frames, new AbstractEventFrame()
                                        {
                                            Time = tracker.Slider.StartTime + (timeLength * progress),
                                            Position = posAtTime,
                                            
                                            IsSliderTick = true,
                                            IsSliderSlide = true
                                        });
                                    }
                                }
                                
                                /*AddFrame(frames, new AbstractSyntaxFrame()
                                {
                                    Time = lastTime,
                                    Position = lastPos,
                                    
                                    IsSliderEnd = true
                                });*/
                                
                                //if(lastTime != tracker.Slider.EndTime)e
                                {
                                    AddFrame(frames, new AbstractEventFrame()
                                    {
                                        Time = tracker.Slider.EndTime,
                                        Position = tracker.Slider.StackedEndPosition,
                                        
                                        IsSliderEnd = true
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
                        lastSliderTime = time;
                    else
                        lastSliderTime = null;
                }
                
                if(hitObject is null)
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
                        IsSliderTick = true // Entry tick
                    });
                    
                    if(!lastSliderTime.HasValue)
                        lastSliderTime = slider.StartTime;
                    
                    continue;
                }
                
                AddFrame(frames, new AbstractEventFrame()
                {
                    Time = hitObject.StartTime,
                    Position = ((OsuHitObject)hitObject).StackedPosition, //HACK: this is nasty
                    
                    IsCircleHit = true
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
            
            //Log.Log("HOM " + index.ToString("000") + ": " + frame);
            
            frames.Insert(index, frame);
        }
        
        private static void FixupFrames(List<AbstractEventFrame> frames)
        {
            AbstractEventFrame lastFrame = frames[0];
            
            uint sliderCount = 0; // Can't overflow due to C# array index limit
            
            for(int i = 1; i < frames.Count; i++)
            {
                AbstractEventFrame currentFrameRef = frames[i];
                
                Log.Log("ROM " + i.ToString("00000") + ": " + currentFrameRef);
                
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
            
            private int UpdateIndex;
            private readonly Vector2 PositionOffset;
            
            public SliderTracker(OsuSlider slider)
            {
                this.Slider = slider;
                
                this.UpdateIndex = 0;
                this.NextUpdateTime = slider.StartTime;
                this.NextPosition = default;
                
                this.PositionOffset = slider.StackedPosition - slider.Position;
                
                if(!UpdateNextTime())
                    throw new Exception("Slider not initialized");
            }
            
            public bool UpdateNextTime()
            {
                while(true)
                {
                    if(++UpdateIndex >= Slider.NestedHitObjects.Count)
                        return false;
                    
                    HitObject childHom = Slider.NestedHitObjects[UpdateIndex];
                    
                    if
                    (
                        childHom is SliderTick ||
                        childHom is SliderRepeat ||
                        childHom is SliderTailCircle
                    )
                    {
                        NextUpdateTime = childHom.StartTime;
                        NextPosition = ((OsuHitObject)childHom).Position; //HACK: this is nasty
                        
                        return true;
                    }
                    
                }
            }
        }
        
        private class AbstractEventFrame : IComparable<AbstractEventFrame>
        {
            public static readonly IComparer<AbstractEventFrame> Comparer = Comparer<AbstractEventFrame>.Default;
            
            public double Time = double.MinValue;
            public Vector2 Position = default; // Not valid if only IsSpinner or IsSpinnerEnd is set
            
            public bool IsEngage => IsCircleHit;
            public bool IsHold => IsSliderSlide || IsSliderTick || IsSpinnerStart;
            public bool IsRelease => (IsCircleHit || IsSliderEnd || IsSpinnerEnd) && !IsHold;
            
            public bool IsCircleHit = false;
            public bool IsSliderSlide = false;
            public bool IsSliderTick = false;
            public bool IsSliderEnd = false;
            public bool IsSpinnerStart = false;
            public bool IsSpinnerEnd = false;
            
            //FIXME: there is a slider end miss when same-frame hit circle happens
            int IComparable<AbstractEventFrame>.CompareTo(AbstractEventFrame other)
            {
                if(this.Time > other.Time)
                    return 1;
                
                if(this.Time < other.Time)
                    return -1;
                
                // Spinners have the highest priority, as we want them to be
                //  on the last frame of the time collision, so
                //  the spinner is actually getting spinned
                if(this.IsSpinnerStart != other.IsSpinnerStart)
                    return this.IsSpinnerStart ? 1 : -1;
                
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
                // Its priority is also inverted, so a spinner end gets placed last.
                if(this.IsSpinnerEnd != other.IsSpinnerEnd)
                    return this.IsSpinnerEnd ? -1 : 1; // [!] Inverted [!]
                
                // Circle hit order really doesn't matter,
                //  but it has to be implemented for compliance sake
                if(this.IsCircleHit != other.IsCircleHit)
                    return this.IsCircleHit ? 1 : -1;
                
                // Should only happen on circle spam
                return 0;
            }

            public override string ToString()
            {
                return "{" + Time + ",\t" + Position + "\t|\t" + string.Join(",\t", new[] {IsCircleHit, IsSliderSlide, IsSliderTick, IsSliderEnd, IsSpinnerStart, IsSpinnerEnd}.Select(b => b.ToString())) + "}";
            }
        }
    }
}
