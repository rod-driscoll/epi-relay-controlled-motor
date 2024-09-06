using Crestron.SimplSharp;
using Crestron.SimplSharpProInternal;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.CrestronIO;
using PepperDash.Essentials.Core.Shades; // for interfaces, I hope they don't remove them and if they do I hope they put them into .Devices.Common.Shades
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using ShadeBase = PepperDash.Essentials.Devices.Common.Shades.ShadeBase;

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
        public int RemainingMillisecs { get; set; } // must declare get; set; so it can be used in S+
        public int CurrentPercent { get; set; } // must declare get; set; so it can be used in S+
        public int PendingPercent { get; set; } // must declare get; set; so it can be used in S+
        public int id { get; set; }
        //[System.Obsolete("Empty ctor only exists to allow class to be used in S+", true)]
        public PositionEventArgs() { } // must declare an empty ctor so it can be used in S+
        public PositionEventArgs(PositionStates status, int currentPercent, int pendingPercent, int remainingMillisecs)
        {
            this.Status = status;
            this.CurrentPercent = currentPercent;
            this.PendingPercent = pendingPercent;
            this.RemainingMillisecs = remainingMillisecs;
        }
    }

    // can't derive from RelayControlledShade
    public class Device : ShadeBase, IHasFeedback, IDisposable,
        IShadesFeedback, IShadesOpenClosedFeedback, IShadesRaiseLowerFeedback 
    {
        #region variables
        public LogEventLevel LogLevel { get; set; }

        public Config config { get; private set; }

        ISwitchedOutput OpenRelay;
        List<ISwitchedOutput> StopRelays;
        ISwitchedOutput CloseRelay;

        CTimer positionTimer;
        public event EventHandler<PositionEventArgs> PositionChange;

        public int DirectionChangeMilliseconds = 500;
        private int directionChangeMillisecondsRemaining = 0;
        public int SampleMillisecs {  get; private set; }
        public int RelayPulseTime { get; private set; }
        public int TravelMilliseconds { get; private set; } // full travel time
        public int StartDelayMilliseconds { get; private set; } // ms from not moving to moving
        public int PositionMilliseconds { get; private set; } // current ms from closed
        public int RemainingMilliseconds { get; private set; }

        public int PercentOpenCurrent { get; private set; } // 0% == closed, 100% == open
        public int PercentOpenPending { get; private set; }

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
            Debug.LogMessage(LogEventLevel.Debug, this, "Constructor starting");
            this.config = config;
            if (TravelMilliseconds == 0) TravelMilliseconds = 10000;
            RelayPulseTime = config.RelayPulseTime == 0 ? 500: config.RelayPulseTime;
            SampleMillisecs = config.Relays.Open.MinimumChange == 0 ? 100 : config.Relays.Open.MinimumChange;
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
            CrestronEnvironment.ProgramStatusEventHandler += type =>
            {
                if (type != eProgramStatusEventType.Stopping) return;
                Dispose();
            };
        }

        #region methods

        private int PositionMillisecondsToPercent(int ms)
        {
            int percent_ = 100 * ms / TravelMilliseconds;
            return (percent_);
        }
        private int PositionPercentToMilliseconds(int percent)
        {
            int ms_ = TravelMilliseconds * percent/100;
            return (ms_);
        }
        private void OnPositionChange(PositionEventArgs args)
        {
            //Debug.LogMessage(LogEventLevel.Debug, this, "OnPositionChange");
            if (PositionChange != null)
                PositionChange(this, args);
            //Debug.LogMessage(LogEventLevel.Debug, this, "OnPositionChange Pending: {0}, Current: {1}, PositionMilliseconds:{2}, RemainingMilliseconds:{3} FireUpdates {4}", PercentOpenPending, PercentOpenCurrent, PositionMilliseconds, RemainingMilliseconds, Feedbacks == null ? "== null" : "exist");
            foreach (var feedback in Feedbacks.Where(x => x != null))
            {
                try
                {
                    if(feedback != null)
                        feedback.FireUpdate();
                }
                catch (Exception e)
                {
                    Debug.LogMessage(LogLevel,this, "OnPositionChange FireUpdate {0} ERROR: {1}", feedback.Key, e.Message);
                    //[14:11:08.863]App 1:[screen-1] OnPositionChange FireUpdate IsClosed ERROR: Object reference not set to an instance of an obj
                }
            }
        }

        void PulseOutput(ISwitchedOutput output, int pulseTime)
        {
            //Debug.LogMessage(LogEventLevel.Information, this, "PulseOutput");
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
        /// Attempts to get the port on the specified device from config
        /// </summary>
        /// <param name="relayConfig"></param>
        /// <returns></returns>
        ISwitchedOutput GetSwitchedOutputFromDevice(IOPortConfig relayConfig)
        {
            //Debug.LogMessage(LogEventLevel.Debug, this, "GetSwitchedOutputFromDevice: relay on port '{0}' from device with key '{1}'", relayConfig.PortNumber, relayConfig.PortDeviceKey);
            var portDevice = DeviceManager.GetDeviceForKey(relayConfig.PortDeviceKey);

            if (portDevice != null)
            {
                Debug.LogMessage(LogEventLevel.Debug, this, "GetSwitchedOutputFromDevice {0} port:{1}", portDevice == null ? "== null" : portDevice.Key, relayConfig.PortNumber);
                var ports_ = portDevice as ISwitchedOutputCollection;
                if (ports_ == null)
                    Debug.LogMessage(LogEventLevel.Debug, this, "Error: device is NOT ISwitchedOutputCollection");
                else
                {
                    if(ports_.SwitchedOutputs.ContainsKey(relayConfig.PortNumber))
                    {
                        ISwitchedOutput relay_ = ports_.SwitchedOutputs[relayConfig.PortNumber];
                        if (relay_ == null)
                            Debug.LogMessage(LogEventLevel.Debug, this, "Error: relay[{0}] is null", relayConfig.PortNumber);
                        else
                            return relay_; 
                    }
                    else
                        Debug.LogMessage(LogEventLevel.Debug, this, "Error: no port with Key {0}", relayConfig.PortNumber);
                }
            }
            Debug.LogMessage(LogEventLevel.Debug, this, "Error: Unable to get relay on port '{0}' from device with key '{1}'", relayConfig.PortNumber, relayConfig.PortDeviceKey);
            return null;
        }

        void PositionTimerExpired(object obj)
        {
            //Debug.LogMessage(LogLevel,this, "PositionTimerExpired, statusCurrent: {0}, PercentOpenCurrent: {1}, PercentOpenPending: {2}, remaining ms: {3}",
            //    StatusCurrent.ToString(), PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds);
            try
            {
                if (positionTimer != null)
                {
                    //Debug.LogMessage(LogLevel,"{0} PositionTimerExpired {1}", ClassName, RemainingMilliseconds);
                    // changing direction
                    if (directionChangeMillisecondsRemaining > 0)
                    {
                        if (directionChangeMillisecondsRemaining < SampleMillisecs)
                            directionChangeMillisecondsRemaining = 0;
                        else
                            directionChangeMillisecondsRemaining -= SampleMillisecs;
                    }
                    // closing
                    else if (StatusCurrent == PositionStates.closing)
                    {
                        int samplePercent_ = PositionMillisecondsToPercent(SampleMillisecs);
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
                            RemainingMilliseconds = PositionPercentToMilliseconds(PercentOpenCurrent - PercentOpenPending);
                    }
                    // opening
                    else if (StatusCurrent == PositionStates.opening)
                    {
                        int samplePercent_ = PositionMillisecondsToPercent(SampleMillisecs);
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
                            //Debug.LogMessage(LogLevel,this, "PositionTimerExpired statusCurrent: {0}, RemainingMilliseconds: {1}, PercentOpenCurrent: {2}, PercentOpenPending: {3}", 
                            //    StatusCurrent.ToString(), RemainingMilliseconds, PercentOpenCurrent, PercentOpenPending);
                        }
                    }
                    else
                    {
                        Debug.LogMessage(LogLevel,this, "PositionTimerExpired, Unhandled statusCurrent: {0}, PercentOpenCurrent: {1}, PercentOpenPending: {2}", StatusCurrent.ToString(), PercentOpenCurrent, PercentOpenPending);
                        Dispose();
                    }
                    OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                    //Debug.LogMessage(LogLevel,this, "PositionTimerExpired, OnPower done");
                }
                else
                    Debug.LogMessage(LogLevel,this, "PositionTimerExpired, PositionTimer == null");
                //Debug.LogMessage(LogLevel,this, "PositionTimerExpired done");
            }
            catch (Exception e)
            {
                Debug.LogMessage(LogLevel,this, "PositionTimer ERROR: {1}", e.Message);
            }
        }

        private void StartPositionTimer()
        {
            if (positionTimer != null)
            {
                Debug.LogMessage(LogLevel,this, "StartPositionTimer resetting");
                positionTimer.Reset(SampleMillisecs, SampleMillisecs);
            }
            else
            {
                Debug.LogMessage(LogLevel,this, "StartPositionTimer creating new PositionTimer, SampleMillisecs: {0}", SampleMillisecs);
                positionTimer = new CTimer(PositionTimerExpired, this, SampleMillisecs, SampleMillisecs);
            }
            //Debug.LogMessage(LogLevel,this, "StartPositionTimer done");
        }
        public void Dispose()
        {
            Debug.LogMessage(LogEventLevel.Debug, this, "Dispose");
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
                    Debug.LogMessage(LogEventLevel.Information, this, "PulseOutput CloseRelay");
                    if (config.Relays.Stop.StopMethod == eRelayControlledMotorStopMethod.OppositeDirection)
                        DoublePulseOutput(CloseRelay, RelayPulseTime);
                    else
                        PulseOutput(CloseRelay, RelayPulseTime);
                    StartPositionTimer();
                    Debug.LogMessage(LogEventLevel.Debug, this, "Close starting, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
                    OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                }
                else
                {
                    Debug.LogMessage(LogEventLevel.Information, this, "Close already running, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
                    if (PercentOpenPending == 0 && PercentOpenCurrent == 0 && StatusCurrent != PositionStates.closed)
                    {
                        StatusCurrent = PositionStates.closed;
                        OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                        Dispose();
                    }
                }
            }
            // open
            else if (PercentOpenPending == 100 || PercentOpenPending > PercentOpenCurrent) 
            {
                if (StatusCurrent != PositionStates.opening)
                {
                    directionChangeMillisecondsRemaining =
                        StatusCurrent == PositionStates.closing ? 2 * DirectionChangeMilliseconds : DirectionChangeMilliseconds;
                    StatusCurrent = PositionStates.opening;
                    Debug.LogMessage(LogEventLevel.Information, this, "PulseOutput OpenRelay");
                    if (config.Relays.Stop.StopMethod == eRelayControlledMotorStopMethod.OppositeDirection)
                        DoublePulseOutput(OpenRelay, RelayPulseTime);
                    else
                        PulseOutput(OpenRelay, RelayPulseTime);
                    StartPositionTimer();
                    Debug.LogMessage(LogEventLevel.Debug, this, "Open starting, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
                    OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                }
                else
                {
                    Debug.LogMessage(LogEventLevel.Information, this, "Open already running, PercentOpenCurrent: {0}, PercentOpenPending: {1}", PercentOpenCurrent, PercentOpenPending);
                    if (PercentOpenPending == 100 && PercentOpenCurrent == 100 && StatusCurrent != PositionStates.open)
                    {
                        StatusCurrent = PositionStates.open;
                        OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                        Dispose();
                    }
                }
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

                if (PositionMilliseconds <= SampleMillisecs)
                    StatusCurrent = PositionStates.closed;
                else if (PositionMilliseconds >= TravelMilliseconds-SampleMillisecs)
                    StatusCurrent = PositionStates.open;
                else
                    StatusCurrent = PositionStates.stop;

                if (StopRelays != null)
                {
                    Debug.LogMessage(LogEventLevel.Information, this, "PulseOutput StopRelays");
                    foreach (var item_ in StopRelays)
                        PulseOutput(item_, RelayPulseTime);
                }
                OnPositionChange(new PositionEventArgs(StatusCurrent, PercentOpenCurrent, PercentOpenPending, RemainingMilliseconds));
                Dispose();
            }
        }
        public void SetPosition(PositionStates state)
        {
            Debug.LogMessage(LogLevel,"this SetPosition {0}", state);
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
            Debug.LogMessage(LogEventLevel.Information, this, "Open, current: {0}", StatusCurrent.ToString());
            SetPosition((ushort)100);
        }
        public override void Close()
        {
            Debug.LogMessage(LogEventLevel.Information, this, "Close, current: {0}", StatusCurrent.ToString());
            SetPosition((ushort)0);
        }
        public override void Stop()
        {
            Debug.LogMessage(LogEventLevel.Information, this, "Stopping motor: '{0}'", this.Name);
            SetPosition((ushort)PercentOpenCurrent);
        }
    }
       
    #endregion methods
}

