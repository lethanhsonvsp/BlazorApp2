namespace BlazorApp2;

public struct TPDO2Data
{
    public int ActualVelocity;
    public byte ModesOfOperationDisplay;

    public TPDO2Data(byte[] data)
    {
        if (data.Length >= 5)
        {
            ActualVelocity = BitConverter.ToInt32(data, 0);
            ModesOfOperationDisplay = data[4];
        }
        else { ActualVelocity = 0; ModesOfOperationDisplay = 0; }
    }
}
