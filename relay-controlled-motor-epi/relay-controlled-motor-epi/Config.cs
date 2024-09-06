using Newtonsoft.Json;
using PepperDash.Essentials.Core.CrestronIO;

namespace relay_controlled_motor_epi
{
    public class MotorRelaysConfig
    {
        [JsonProperty("Open")]
        public IOPortConfig Open { get; set; }
        [JsonProperty("Stop")]
        public StopRelayMethod Stop { get; set; }
        [JsonProperty("Close")]
        public IOPortConfig Close { get; set; }
    }

    public enum eRelayControlledMotorStopMethod
    {
        Stop = 0,
        OppositeDirection = 1,
        OpenAndClose = 2
    }
    public class StopRelayMethod//: IOPortConfig
    {
        [JsonProperty("method")]
        public eRelayControlledMotorStopMethod StopMethod { get; set; }
    }

    /// <summary>
    /// Not currently used
    /// </summary>
    public class DelayTimesConfig
    {
        public DelayTimesConfig()
        {
            OpenMs = OpenMs == 0 ? 20000 : OpenMs;
            closeMs = closeMs == 0 ? 20000 : closeMs;
            stopMs = stopMs == 0 ? 500 : stopMs;
            StartMs = StartMs == 0 ? 500 : StartMs;
        }

        [JsonProperty("openMs")]
        public uint OpenMs { get; set; }
        [JsonProperty("closeMs")]
        public uint closeMs { get; set; }
        [JsonProperty("openMs")]
        public uint stopMs { get; set; }
        [JsonProperty("startMs")]
        public uint StartMs { get; set; }
    }
    
    /// <summary>
    /// based on RelayControlledShadeConfigProperties,
    /// adding capability to trigger multiple relays on stop
    /// </summary>
    public class Config // this is properties
    {

        [JsonProperty("RelayPulseTime")]
        public int RelayPulseTime { get; set; }

        [JsonProperty("StopOrPresetLabel")]
        public string StopOrPresetLabel { get; set; }

        [JsonProperty("Relays")]
        public MotorRelaysConfig Relays { get; set; }

        /// <summary>
        /// JSON control object: device.properties
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <example>
        /// <code>
        /// 
        /// "control": {
        ///		"method": "tcpIp",
        ///		"tcpSshProperties": {
        ///			"address": "172.22.0.101",
        ///			"port": 4998,
        ///			"username": "admin",
        ///			"password": "password",
        ///			"autoReconnect": true,
        ///			"autoReconnectIntervalMs": 10000
        ///		}
        ///	}
        ///	
        /// "control": {
        ///		"method": "relay", // "relay" is not included of eControlMethod Method
        ///		"relayProperties": { // "relayProperties" is not included in ControlPropertiesConfig
        ///			"RelayPulseTime": 500,
        ///			"Relays": {
        ///			    "Open": {
        ///			        "portDeviceKey": "processor",
        ///			        "portNumber": 1,
        ///			        "disablePullUpResistor": false,
        ///			        "minimumChange": 100
        ///			    },
        ///			    "Close": {
        ///			        "portDeviceKey": "processor",
        ///			        "portNumber": 2,
        ///			        "disablePullUpResistor": false,
        ///			        "minimumChange": 100
        ///			    ,
        ///			    "Stop": {
        ///			        "method": "OpenAndClose"
        ///			    }
        ///			}
        ///		}
        ///	}
        /// </code>
        /// </example>
        //[JsonProperty("control")]
        //public MotorControllerPropertiesConfig Control { get; set; }

        /// <summary>
        /// JSON control object: device.properties
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <example>
        /// <code>
        /// 
        /// delayTimes: {
        ///     "openMs": 20000,
        ///     "closeMs": 20000,
        ///     "stopMs": 500,
        ///     "startMs": 500
        /// }
        /// 
        /// </code>
        /// </example>
        //[JsonProperty("delayTimes")]
        //public DelayTimesConfig DelayTimes { get; set; }

        ///	"brand": "Global Cache",
        //[JsonProperty("brand")]
        //public string Brand { get; set; }
        ///	"model": "iTachIP2CC",
        //[JsonProperty("model")]
        //public string Model { get; set; }

        /// <summary>
        /// Constuctor
        /// </summary>
        /// <remarks>
        /// If using a collection you must instantiate the collection in the constructor
        /// to avoid exceptions when reading the configuration file 
        /// </remarks>
        public Config()
        {
            //DelayTimes = new DelayTimesConfig();
        }
    }
}
