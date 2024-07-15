using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Shades;

namespace relay_controlled_motor_epi.Interfaces
{
    public interface IOpenClosedFeedback // not used because we'd need to put this in a common library like PepperDash.Core
    {
        BoolFeedback IsOpenFeedback { get; }
        BoolFeedback IsClosedFeedback { get; }
    }

    public interface IRelayControlledMotor: IOpenClosedFeedback // not used because we'd need to put this in a common library like PepperDash.Core
    {
        StringFeedback StatusFeedback { get; }
        IntFeedback PercentOpenFeedback { get; }
        IntFeedback RemainingFeedback { get; }
        BoolFeedback IsStoppedFeedback { get; }
        BoolFeedback IsLoweringFeedback { get; }
        BoolFeedback IsRaisingFeedback { get; }
    }
}