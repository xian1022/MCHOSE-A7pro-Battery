namespace MchoseBattery.Core;
public sealed class ReadingStore
{
    public int Epoch {get;private set;}
    public Dictionary<DeviceKind,BatteryReading> Readings {get;}=Enum.GetValues<DeviceKind>().ToDictionary(k=>k,k=>new BatteryReading(k));
    public void Invalidate(string reason)
    {
        Epoch++;
        foreach(var kind in Readings.Keys.ToArray())Readings[kind]=new(kind,LastSuccess:Readings[kind].LastSuccess,Detail:reason);
    }
    public bool Apply(BatteryReading reading,int epoch)
    {
        if(epoch!=Epoch)return false;
        Readings[reading.Device]=reading with{LastSuccess=reading.LastSuccess??Readings[reading.Device].LastSuccess};return true;
    }
}
