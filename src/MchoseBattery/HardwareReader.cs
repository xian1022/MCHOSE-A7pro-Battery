using MchoseBattery.Core;

namespace MchoseBattery;

internal static class HardwareReader
{
    public static BatteryReading Read(DeviceKind kind)
    {
        try { return ReadMouse(); }
        catch(Exception e) { Console.Error.WriteLine(e);return new(kind,Detail:$"讀取失敗：{e.GetType().Name}"); }
    }

    static BatteryReading ReadMouse()
    {
        // Query the direct USB connection first when the charging/data cable is attached.
        // Only the A7 Pro vendor collection is opened, never its mouse/keyboard input collection.
        using var device=NativeHid.Open(0x5253,0x0010,0xff01,65)??NativeHid.Open(0x5253,0x1021,0xff01,65);
        if(device==null)return new(DeviceKind.Mouse,LinkState.Disconnected,Detail:"找不到 A7 Pro 接收器或 USB 連線");
        for(int attempt=0;attempt<4;attempt++) {
            var command=Enumerable.Repeat((byte)255,21).ToArray();command[0]=0x11;command[1]=0xf9;
            device.SetFeature(command);Thread.Sleep(120);
            var packet=device.GetFeature(0x11,21);
            Console.Error.WriteLine("MCHOSE="+Convert.ToHexString(packet));
            var reading=PacketParser.Mchose(packet,DateTimeOffset.UtcNow,wired:device.ProductId==0x0010);
            if(reading!=null)return reading;
        }
        return new(DeviceKind.Mouse,Detail:"接收器存在，滑鼠未回應有效電量");
    }
}
