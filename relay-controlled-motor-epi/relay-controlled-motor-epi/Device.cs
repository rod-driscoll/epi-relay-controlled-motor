using Crestron.SimplSharp;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.CrestronIO;
using PepperDash.Essentials.Core.Shades;
using System;
using System.Collections.Generic;
using System.Linq;

namespace relay_controlled_motor_epi
{
    public enum PositionStates
    {
        unknown = 0,
        closed = 1,
        open = 2,
        opening = 3,
        closing = 4,
        stop = 5,
        toggle = 6,
        preset = 7,
        error = 8
    }

    public class PositionEventArgs : EventArgs
    {
        public PositionStates Status { get; set; } // must declare get; set; so it can be used in S+
        public uint RemainingMillisecs { get; set; } // must declare get; set; so it can be used in S+
        public uint CurrentPercent { get; set; } // must declare get; set; so it can be used in S+
        public uint PendingPercent { get; set; } // must declare get; set; so it can be used in S+
        public uint id { get; set; }
        //[System.Obsolete("Empty ctor only exists to allow class to be used in S+", true)]
        public PositionEventArgs() { } // must declare an empty ctor so it can be used in S+
        public PositionEventArgs(PositionStates status, uint currentPercent, uint pendingPercent, uint remainingMillisecs)
        {
            this.Status = status;
            this.CurrentPercent = currentPercent;
            this.PendingPercent = pendingPercent;
            this.RemainingMillisecs = remainingMillisecs;
        }
    }

    // can't derive from RelayControlledShade
    public class Device : ShadeBase, IShadesFeedback, IShadesOpenClosedFeedback, IShadesRaiseLowerFeedback, IDisposable
    {
        #region variables
        public uint LogLevel { get; set; }

        public Config config { get; private set; }

        ISwitchedOutput OpenRelay;
        List<ISwitchedOutput> StopRelays;
        ISwitchedOutput CloseRelay;


        CTimer positionTimer;
        public event EventHandler<PositionEventArgs> PositionChange;

        int RelayPulseTime = 200;
        public uint sampleMillisecs = 100;
        public uint DirectionChangeMilliseconds = 500;
        private uint directionChangeMillisecondsRemaining = 0;
        public uint TravelMilliseconds { get; private set; } // full travel time
        public uint StartDelayMilliseconds { get; private set; } // ms from not moving to moving
        public uint PositionMilliseconds { get; private set; } // current ms from closed
        public uint RemainingMilliseconds { get; private set; }

        public uint PercentOpenCurrent { get; private set; } // 0% == closed, 100% == open
        public uint PercentOpenPending { get; private set; }

        public PositionStates StatusCurrent { get; private set; }
        public FeedbackCollection<Feedback> Feedbacks { get; private set; }
        public BoolFeedback ShadeIsOpenFeedback { get; private set; } // IShadesOpenClosedFeedback
        public BoolFeedback ShadeIsClosedFeedback { get; private set; } // IShadesOpenClosedFeedback
        public BoolFeedback ShadeIsLoweringFeedback { get; private set; } // IShadesRaiseLowerFeedback
        public BoolFeedback ShadeIsRaisingFeedback { get; private set; } // IShadesRaiseLowerFeedback
        public BoolFeedback IsStoppedFeedback { get; private set; } // IShadesStopFeedback
        public IntFeedback PositionFeedback { get; private set; } // IShadesFeedback
        public IntFeedback RemainingFeedback { get; private set; }
        public StringFeedback StatusFeedback { get; private set; }
        
        #endregion variables

        public Device(string key, string name, Config config)
            : base(key, name)
        {
            Debug.Console(1, this, "Constructor starting");
            this.config = config;
            if (TravelMilliseconds == 0) TravelMilliseconds = 10000;
            OpenRelay = GetSwitchedOutputFromDevice(config.Relays.Open);
            CloseRelay = GetSwitchedOutputFromDevice(config.Relays.Close);

            ShadeIsOpenFeedback = new BoolFeedback("IsOpen", () => StatusCurrent == PositionStates.open);
            ShadeIsClosedFeedback = new BoolFeedback("IsClosed", () => StatusCurrent == PositionStates.closed);
            ShadeIsLoweringFeedback = new BoolFeedback("IsOpening", () => StatusCurrent == PositionStates.opening);
            ShadeIsRaisingFeedback = new BoolFeedback("IsClosing", () => StatusCurrent == PositionStates.closing);
            IsStoppedFeedback = new BoolFeedback("IsStopped", () => (bool)(StatusCurrent != PositionStates.closing && StatusCurrent != PositionStates.opening));
            PositionFeedback = new IntFeedback("PercentOpen", () => (int)PercentOpenCurrent);
            RemainingFeedback = new IntFeedback("Remaining", () => (int)RemainingMilliseconds);
            StatusFeedback = new StringFeedback("Status", () => StatusCurrent.ToString());

            Feedbacks = new FeedbackCollection<Feedback>
            {
                ShadeIsOpenFeedback,
                ShadeIsClosedFeedback,
                ShadeIsLoweringFeedback,
                ShadeIsRaisingFeedback,
                IsStoppedFeedback,
                PositionFeedback,
                RemainingFeedback,
                StatusFeedback
            };
            //e.g. ShadeIsOpenFeedback.FireUpdate();
        }

        #region methods

        private uint PositionMillisecondsToPercent(uint ms)
        {
            uint percent_ = 100 * ms / TravelMilliseconds;
            return (percent_);
        }
        private uint PositionPercentToMilliseconds(uint percent)
        {
            uint ms_ = TravelMilliseconds * percent/100;
            return (ms_);
        }
        private void OnPositionChange(PositionEventArgs args)
        {
            //Debug.Console(1, this, "OnPositionChange");
            if (PositionChange != null)
                PositionChange(this, args);
            //Debug.Console(1, this, "OnPositionChange Pending: {0}, Current: {1}, PositionMilliseconds:{2}, RemainingMilliseconds:{3} FireUpdates {4}", PercentOpenPending, PercentOpenCurrent, PositionMilliseconds, RemainingMilliseconds, Feedbacks == null ? "== null" : "exist");
            foreach (var feedback in Feedbacks.Where(x => x != null))
            {
                try
                {
                    if(feedback != null)
                        feedback.FireUpdate();
                }
                catch (Exception e)
                {
                    Debug.Console(LogLevel, this, "OnPositionChange FireUpdate {0} ERROR: {1}", feedback.Key, e.Message);
                    //[14:11:08.863]App 1:[screen-1] OnPositionChange FireUpdate IsClosed ERROR: Object reference not set to an instance of an obj
                }
            }
        }

        void PulseOutput(ISwitchedOutput output, int pulseTime)
        {
            output.On();
            CTimer pulseTimer = new CTimer(new CTimerCallbackFunction((o) => output.Off()), pulseTime);
        }

        void DoublePulseOutput(ISwitchedOutput output, int pulseTime)
        {
            output.On();
            CTimer pulseTimer1 = new CTimer(new CTimerCallbackFunction((o) => output.Off()), pulseTime * 1);
            CTimer pulseTimer2 = new CTimer(new CTimerCallbackFunction((o) => output.On()), pulseTime * 2);
            CTimer pulseTimer3 = new CTimer(new CTimerCallbackFunction((o) => output.Off()), pulseTime * 3);
        }

        /// <summary>
        /// Attempts to get the port on teh specified device from config
        /// </summary>
        /// <param name="relayConfig"></param>
        /// <returns></returns>
        ISwitchedOutput GetSwitchedOutputFromDevice(IOPortConfig relayConfig)
        {
            //Debug.Console(1, this, "GetSwitchedOutputFromDevice: relay on port '{0}' from device with key '{1}'", relayConfig.PortNumber, relayConfig.PortDeviceKey);
            var portDevice = DeviceManager.GetDeviceForKey(relayConfig.PortDeviceKey);

            if (portDevice != null)
            {
                Debug.Console(1, this, "GetSwitchedOutputFromDevice:portDevice {0}", portDevice == null ? "== null" : "exists");
                ISwitchedOutput relay_ = (portDevice as ISwitchedOutputCollection).SwitchedOutputs[relayConfig.PortNumber];
                if (relay_ == null)
                    Debug.Console(1, this, "Error: relay is null");
                return relay_;
            }
            else
            {
                Debug.Console(1, this, "Error: Unable to get relay on port '{0}' from device with key '{1}'", relayConfig.PortNumber, relayConfig.PortDeviceKey);
                return null;
            }
        }

        void PositionTimerExpired(object obj)
        {
            //Debug.Console(LogLevel, this, "PositionTimerExpired, statusCurrent: {0}, PercentOpenCurrent: {1}, PercentOpenPending: {2}, remaining ms: {3}",
            //    StatusCurrent.ToString(), PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds);
            try
            {
                if (positionTimer != null)
                {
                    //Debug.Console(LogLevel, "{0} PositionTimerExpired {1}", ClassName, RemainingMilliseconds);
                    // changing direction
                    if (directionChangeMillisecondsRemaining > 0)
                    {
                        if (directionChangeMillisecondsRemaining < sampleMillisecs)
                            directionChangeMillisecondsRemaining = 0;
                        else
                            directionChangeMillisecondsRemaining -= sampleMillisecs;
                    }
                    // closing
                    else if (StatusCurrent == PositionStates.closing)
                    {
                        uint samplePercent_ = PositionMillisecondsToPercent(sampleMillisecs);
                        if (PercentOpenCurrent >= samplePercent_)
                            PercentOpenCurrent -= samplePercent_;
                        else
                            PercentOpenCurrent = 0;
                        if (PercentOpenCurrent == 0) // don't send stop
                        {
                            RemainingMilliseconds = 0;
                            StatusCurrent = PositionStates.closed;
                            //PercentOpenPending = PercentOpenCurrent;
                            Dispose();
                        }
                        else if (PercentOpenCurrent <= PercentOpenPending) // stop mid
                        {
                            RemainingMilliseconds = 0;
                            StatusCurrent = PositionStates.stop;
                            //PercentOpenPending = PercentOpenCurrent;
                            Dispose();
                        }
                        else // keep going
                            RemainingMilliseconds = PositionMillisecondsToPercent(PercentOpenCurrent - PercentOpenPending);
                    }
                    // opening
                    else if (StatusCurrent == PositionStates.opening)
                    {
                        uint samplePercent_ = PositionMillisecondsToPercent(sampleMillisecs);
                        if (PercentOpenCurrent <= 100 - samplePercent_)
                            PercentOpenCurrent += samplePercent_;
                        else
                            PercentOpenCurrent = 100;
                        if (PercentOpenCurrent == 100) // don't send stop
                        {
                            RemainingMilliseconds = 0;
                            StatusCurrent = PositionStates.open;
                            //PercentOpenPending = PercentOpenCurrent;
                            Dispose();
                        }
                        else if (PercentOpenCurrent >= PercentOpenPending) // stop mid
                        {
                            RemainingMilliseconds = 0;
                            StatusCurrent = PositionStates.stop;
                            //PercentOpenPending = PercentOpenCurrent;
                            Dispose();
                        }
                        else // keep going
                        {
                            RemainingMilliseconds = PositionPercentToMilliseconds(PercentOpenPending - PercentOpenCurrent);
                            //Debug.Console(LogLevel, this, "PositionTimerExpired statusCurrent: {0}, RemainingMilliseconds: {1}, PercentOpenCurrent: {2}, PercentOpenPending: {3}", 
                            //    StatusCurrent.ToString(), RemainingMilliseconds, PercentOpenCurrent, PercentOpenPending);
                        }
                    }
                    else
                    {
                        Debug.Console(LogLevel, this, "PositionTimerExpired, Unhandled statusCurrent: {0}, PercentOpenCurrent: {1}, PercentOpenPending: {2}", StatusCurrent.ToString(), PercentOpenCurrent, PercentOpenPending);
                        Dispose();
                    }
                    OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                    //Debug.Console(LogLevel, this, "PositionTimerExpired, OnPower done");
                }
                else
                    Debug.Console(LogLevel, this, "PositionTimerExpired, PositionTimer == null");
                //Debug.Console(LogLevel, this, "PositionTimerExpired done");
            }
            catch (Exception e)
            {
                Debug.Console(LogLevel, this, "PositionTimer ERROR: {1}", e.Message);
            }
        }

        private void StartPositionTimer()
        {
            if (positionTimer != null)
            {
                Debug.Console(LogLevel, this, "StartPositionTimer resetting");
                positionTimer.Reset(sampleMillisecs, sampleMillisecs);
            }
            else
            {
                Debug.Console(LogLevel, this, "StartPositionTimer creating new PositionTimer, sampleMillisecs: {0}", sampleMillisecs);
                positionTimer = new CTimer(PositionTimerExpired, this, sampleMillisecs, sampleMillisecs);
            }
            //Debug.Console(LogLevel, this, "StartPositionTimer done");
        }
        public void Dispose()
        {
            Debug.Console(1, this, "Dispose");
            if (positionTimer != null)
            {
                positionTimer.Stop();
                positionTimer.Dispose();
                positionTimer = null;
            }
        }

        public void SetPosition(ushort percent)
        {
            PercentOpenPending = percent;
            // close
            if (PercentOpenPending == 0 || PercentOpenPending < PercentOpenCurrent) 
            {
                if (StatusCurrent != PositionStates.closing)
                {
                    directionChangeMillisecondsRemaining =
                        StatusCurrent == PositionStates.opening ? 2 * DirectionChangeMilliseconds : DirectionChangeMilliseconds;
                    StatusCurrent = PositionStates.closing;
                    if (config.Relays.Stop.StopMethod == eRelayControlledMotorStopMethod.OppositeDirection)
                        DoublePulseOutput(CloseRelay, RelayPulseTime);
                    else
                        PulseOutput(CloseRelay, RelayPulseTime);
                    StartPositionTimer();
                    Debug.Console(1, this, "Close starting, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
                    OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                }
                else
                    Debug.Console(1, this, "Close already running, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
            }
            // open
            else if (PercentOpenPending == 100 || PercentOpenPending > PercentOpenCurrent) 
            {
                if (StatusCurrent != PositionStates.opening)
                {
                    directionChangeMillisecondsRemaining =
                        StatusCurrent == PositionStates.closing ? 2 * DirectionChangeMilliseconds : DirectionChangeMilliseconds;
                    StatusCurrent = PositionStates.opening;
                    if (config.Relays.Stop.StopMethod == eRelayControlledMotorStopMethod.OppositeDirection)
                        DoublePulseOutput(OpenRelay, RelayPulseTime);
                    else
                        PulseOutput(OpenRelay, RelayPulseTime);
                    StartPositionTimer();
                    Debug.Console(1, this, "Open starting, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
                    OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                }
                else
                    Debug.Console(1, this, "Open already running, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
            }
            // stop
            else // PercentOpenPending > PercentOpenCurrent 
            {
                PercentOpenCurrent = PercentOpenPending;
                RemainingMilliseconds = 0;

                switch (config.Relays.Stop.StopMethod)
                {
                    case eRelayControlledMotorStopMethod.Stop:
                        //StopRelays = new List<ISwitchedOutput>() { GetSwitchedOutputFromDevice(config.Relays.Stop) };
                        break;
                    case eRelayControlledMotorStopMethod.OppositeDirection:
                        if (StatusCurrent == PositionStates.opening)
                            StopRelays = new List<ISwitchedOutput>() { CloseRelay };
                        else if (StatusCurrent == PositionStates.closing)
                            StopRelays = new List<ISwitchedOutput>() { OpenRelay };
                        break;
                    //case eRelayControlledMotorStopMethod.OpenAndClose:
                    default:
                        StopRelays = new List<ISwitchedOutput>() { OpenRelay, CloseRelay };
                        break;
                }

                if (PositionMilliseconds <= sampleMillisecs)
                    StatusCurrent = PositionStates.closed;
                else if (PositionMilliseconds >= TravelMilliseconds-sampleMillisecs)
                    StatusCurrent = PositionStates.open;
                else
                    StatusCurrent = PositionStates.stop;

                if (StopRelays != null)
                    foreach (var item_ in StopRelays)
                        PulseOutput(item_, RelayPulseTime);
                OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                Dispose();
            }
        }
        public void SetPosition(PositionStates state)
        {
            Debug.Console(LogLevel, "this SetPosition {0}", state);
            switch (state)
            {
                case PositionStates.closed: SetPosition((ushort)0); break;
                case PositionStates.open: SetPosition((ushort)100); break;
                case PositionStates.stop: SetPosition((ushort)PercentOpenCurrent); break;
                case PositionStates.toggle:
                    if (PercentOpenCurrent == 0)
                        SetPosition(PositionStates.open);
                    else if (PercentOpenCurrent == 100)
                        SetPosition(PositionStates.closed);
                    else if (PercentOpenPending == 0)
                        SetPosition(PositionStates.open);
                    else if (PercentOpenPending == 100)
                        SetPosition(PositionStates.closed);
                    else if (PercentOpenPending < PercentOpenCurrent)
                        SetPosition(PositionStates.open);
                    else if (PercentOpenPending > PercentOpenCurrent)
                        SetPosition(PositionStates.closed);
                    else
                        SetPosition(PositionStates.open);
                    break;
            }
        }

        public override void Open()
        {
            Debug.Console(1, this, "Open, current: {0}", StatusCurrent.ToString());
            SetPosition((ushort)100);
        }
        public override void Close()
        {
            Debug.Console(1, this, "Close, current: {0}", StatusCurrent.ToString());
            SetPosition((ushort)0);
        }
        public override void Stop()
        {
            Debug.Console(1, this, "Stopping motor: '{0}'", this.Name);
            SetPosition((ushort)PercentOpenCurrent);
        }
    }
       
    #endregion methods
}

