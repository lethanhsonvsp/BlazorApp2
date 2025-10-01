namespace BlazorApp2
{
    public enum CiA402State
    {
        NotReadyToSwitchOn = 0,
        SwitchOnDisabled = 1,
        ReadyToSwitchOn = 2,
        SwitchedOn = 3,
        OperationEnabled = 4,
        QuickStopActive = 5,
        FaultReactionActive = 6,
        Fault = 7
    }

    // Enum cho các chế độ hoạt động
    public enum OperationMode : sbyte
    {
        ProfilePosition = 1,
        VelocityMode = 2,
        ProfileVelocity = 3,
        Homing = 6,
        CyclicSynchronousPosition = 8,
        CyclicSynchronousVelocity = 9,
        CyclicSynchronousTorque = 10
    }

}
