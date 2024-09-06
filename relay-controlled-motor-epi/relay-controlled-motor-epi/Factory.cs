using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;
using Serilog.Events;

namespace relay_controlled_motor_epi
{
    public class Factory: EssentialsPluginDeviceFactory<Device>
    {
        public Factory()
        {
            MinimumEssentialsFrameworkVersion = "2.0.0";
            TypeNames = new List<string>() { "relaycontrolledmotor" };
        }
        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogMessage(LogEventLevel.Debug, "[{0}] Factory Attempting to create new device from type: {1}", dc.Key, dc.Type);
            //var props = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(dc.Properties.ToString());
            var props = dc.Properties.ToObject<Config>();
            if (props == null)
            {
                Debug.LogMessage(LogEventLevel.Verbose, "[{0}] Factory: failed to read properties config for {1}", dc.Key, dc.Name);
                return null;
            }
            var device_ = new Device(dc.Key, dc.Name, props);
            //Debug.LogMessage(LogEventLevel.Verbose, "[{0}] Factory {1} {2}", dc.Key, dc.Name, device_== null ? "== null" : "exists");
            return device_;
        }
    }
}
