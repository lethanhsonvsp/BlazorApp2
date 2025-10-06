namespace BlazorApp2;

public struct TPDO1Data
{
    public ushort StatusWord;
    public int ActualPosition;
    public short ActualTorque;

    public TPDO1Data(byte[] data)
    {
        StatusWord = 0; ActualPosition = 0; ActualTorque = 0;
        if (data.Length >= 2) StatusWord = (ushort)(data[0] | (data[1] << 8));
        if (data.Length >= 6) ActualPosition = BitConverter.ToInt32(data, 2);
        if (data.Length >= 8) ActualTorque = BitConverter.ToInt16(data, 6);
    }
}
