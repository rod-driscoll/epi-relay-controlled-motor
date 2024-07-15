# Relay controlled motor - PepperDash Essentials Plugin

v1.0.0

## License

Provided under MIT license

## Overview

Language: Crestron SimplSharpPro C# dotnet4.7.3\
Platform: Crestron 4 series control systems

## Pre-requisites

* You need to be able to get a regular demo of PepperDash Essentials working.
  * Instructions for PepperDash Essentials are available at <https://github.com/PepperDash/Essentials/wiki/Get-started#how-to-get-started>
* You need to be able to develop Crestron in SimplSharp.
* You need to know how to load a program onto a Crestron processor and touchpanel on a Crestron touchpanel.

* https://github.com/rod-driscoll/essentials-custom-rooms
* 

### Installation

1. Download a release version of Essentials.cplz <https://github.com/PepperDash/Essentials/releases> and load it into a processor.
   1. Initial load will error out and create a "\\user\\programX\\" folder on the processor for loading plugins and the config.
2. Load "essentials-minimal-room-config.json" into the "\\user\\programX\\" folder.
3. Load the touch panel file and configure it to communicate with the processor on IPID 03 as per the demo config file..
4. Compile and load the plugins from this repo
   1. essentials-intermediate-room-epi.dll
   2. essentials-intermediate-tp-epi.dll
   3. https://github.com/rod-driscoll/epi-pjlink
   4. https://github.com/rod-driscoll/epi-relay-controlled-motor

### Dependencies

Using NuGet package manager install the following:

1. "PepperDashEssentials"
   1. Delete "NewtonSoft.Json.Compact" from the references folder to remove the duplicate library issue.
2. "Crestron.SimplSharp.SDK.ProgramLibrary" to "minimal-tp" project only.
3. 
### Contributors
Rodney Driscoll: <rod@theavitgroup.com.au>

# References
- https://github.com/rod-driscoll/essentials-custom-rooms
- https://github.com/rod-driscoll/epi-relay-controlled-motor
- https://github.com/PepperDash/Essentials