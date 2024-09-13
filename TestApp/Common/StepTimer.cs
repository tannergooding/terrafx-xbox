using System;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace TestApp
{
    public sealed unsafe class StepTimer
    {
        #region Constants
        // Integer format represents time using 10,000,000 ticks per second.
        private const ulong TicksPerSecond = 10_000_000;
        #endregion

        #region Fields
        // Source timing data uses QPC units.
        private LARGE_INTEGER _qpcFrequency;
        private LARGE_INTEGER _qpcLastTime;
        private ulong _qpcMaxDelta;

        // Derived timing data uses a canonical tick format.
        private ulong _elapsedTicks;
        private ulong _totalTicks;
        private ulong _leftOverTicks;

        // Members for tracking the framerate.
        private uint _frameCount;
        private uint _framesPerSecond;
        private uint _framesThisSecond;
        private ulong _qpcSecondCounter;

        // Members for configuring fixed timestep mode.
        private bool _isFixedTimeStep;
        private ulong _targetElapsedTicks;
        #endregion

        #region Constructors
        public StepTimer()
        {
            _elapsedTicks = 0;
            _totalTicks = 0;
            _leftOverTicks = 0;
            _frameCount = 0;
            _framesPerSecond = 0;
            _framesThisSecond = 0;
            _qpcSecondCounter = 0;
            _isFixedTimeStep = false;
            _targetElapsedTicks = TicksPerSecond / 60;

            LARGE_INTEGER qpcFrequency;
            if (QueryPerformanceFrequency(&qpcFrequency) == FALSE)
            {
                throw new PlatformNotSupportedException();
            }
            _qpcFrequency = qpcFrequency;

            LARGE_INTEGER qpcLastTime;
            if (QueryPerformanceCounter(&qpcLastTime) == FALSE)
            {
                throw new PlatformNotSupportedException();
            }
            _qpcLastTime = qpcLastTime;

            // Initialize max delta to 1/10 of a second.
            _qpcMaxDelta = (ulong)(_qpcFrequency.QuadPart / 10);
        }
        #endregion

        #region Properties
        public ulong ElapsedTicks
        {
            get
            {
                return _elapsedTicks;
            }
        }

        public double ElapsedSeconds
        {
            get
            {
                return TicksToSeconds(_elapsedTicks);
            }
        }

        // Get total time since the start of the program.
        public ulong TotalTicks
        {
            get
            {
                return _totalTicks;
            }
        }

        public double TotalSeconds
        {
            get
            {
                return TicksToSeconds(_totalTicks);
            }
        }

        // Get total number of updates since start of the program.
        public uint FrameCount
        {
            get
            {
                return _frameCount;
            }
        }

        // Get the current framerate.
        public uint FramesPerSecond
        {
            get
            {
                return _framesPerSecond;
            }
        }

        // Set whether to use fixed or variable timestep mode.
        public bool IsFixedTimeStep
        {
            get
            {
                return _isFixedTimeStep;
            }

            set
            {
                _isFixedTimeStep = value;
            }
        }

        // Set how often to call Update when in fixed timestep mode.
        public ulong TargetElapsedTicks
        {
            get
            {
                return _targetElapsedTicks;
            }

            set
            {
                _targetElapsedTicks = value;
            }
        }

        public double TargetElapsedSeconds
        {
            get
            {
                return TicksToSeconds(_targetElapsedTicks);
            }

            set
            {
                _targetElapsedTicks = SecondsToTicks(value);
            }
        }
        #endregion

        #region Methods
        public static double TicksToSeconds(ulong ticks)
        {
            return (double)ticks / TicksPerSecond;
        }

        public static ulong SecondsToTicks(double seconds)
        {
            return (ulong)(seconds * TicksPerSecond);
        }

        // After an intentional timing discontinuity (for instance a blocking IO operation)
        // call this to avoid having the fixed timestep logic attempt a set of catch-up 
        // Update calls.

        public void ResetElapsedTime()
        {
            LARGE_INTEGER qpcLastTime;
            if (QueryPerformanceCounter(&qpcLastTime) == FALSE)
            {
                throw new PlatformNotSupportedException();
            }
            _qpcLastTime = qpcLastTime;

            _leftOverTicks = 0;
            _framesPerSecond = 0;
            _framesThisSecond = 0;
            _qpcSecondCounter = 0;
        }

        // Update timer state, calling the specified Update function the appropriate number of times.
        public void Tick(Action update)
        {
            LARGE_INTEGER currentTime;

            // Query the current time.
            if (QueryPerformanceCounter(&currentTime) == FALSE)
            {
                throw new PlatformNotSupportedException();
            }
        
            ulong timeDelta = (ulong)(currentTime.QuadPart - _qpcLastTime.QuadPart);
        
            _qpcLastTime = currentTime;
            _qpcSecondCounter += timeDelta;
        
            // Clamp excessively large time deltas (e.g. after paused in the debugger).
            if (timeDelta > _qpcMaxDelta)
            {
                timeDelta = _qpcMaxDelta;
            }
        
            // Convert QPC units into a canonical tick format. This cannot overflow due to the previous clamp.
            timeDelta *= TicksPerSecond;
            timeDelta /= (ulong)_qpcFrequency.QuadPart;
        
            uint lastFrameCount = _frameCount;
        
            if (_isFixedTimeStep)
            {
                // Fixed timestep update logic
        
                // If the app is running very close to the target elapsed time (within 1/4 of a millisecond) just clamp
                // the clock to exactly match the target value. This prevents tiny and irrelevant errors
                // from accumulating over time. Without this clamping, a game that requested a 60 fps
                // fixed update, running with vsync enabled on a 59.94 NTSC display, would eventually
                // accumulate enough tiny errors that it would drop a frame. It is better to just round 
                // small deviations down to zero to leave things running smoothly.
        
                if (Math.Abs((long)(timeDelta - _targetElapsedTicks)) < (long)(TicksPerSecond / 4000))
                {
                    timeDelta = _targetElapsedTicks;
                }
        
                _leftOverTicks += timeDelta;
        
                while (_leftOverTicks >= _targetElapsedTicks)
                {
                    _elapsedTicks = _targetElapsedTicks;
                    _totalTicks += _targetElapsedTicks;
                    _leftOverTicks -= _targetElapsedTicks;
                    _frameCount++;

                    update();
                }
            }
            else
            {
                // Variable timestep update logic.
                _elapsedTicks = timeDelta;
                _totalTicks += timeDelta;
                _leftOverTicks = 0;
                _frameCount++;
        
                update();
            }
        
            // Track the current framerate.
            if (_frameCount != lastFrameCount)
            {
                _framesThisSecond++;
            }
        
            if (_qpcSecondCounter >= (ulong)_qpcFrequency.QuadPart)
            {
                _framesPerSecond = _framesThisSecond;
                _framesThisSecond = 0;
                _qpcSecondCounter %= (ulong)_qpcFrequency.QuadPart;
            }
        }
        #endregion
    }
}
