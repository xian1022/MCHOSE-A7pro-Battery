namespace MchoseBattery.Core;

public enum DeviceKind { Mouse }
public enum LinkState { Unknown, Connected, Disconnected }
public enum BatteryLevel { Empty, Low, Medium, High }
public enum ChargeState { Unknown, NotCharging, Charging, Full }
public sealed record BatteryReading(DeviceKind Device, LinkState Link = LinkState.Unknown,
    int? Percent = null, BatteryLevel? Level = null, ChargeState Charge = ChargeState.Unknown,
    DateTimeOffset? LastSuccess = null, string? Detail = null)
{
    public BatteryReading FreshAt(DateTimeOffset now) =>
        Link == LinkState.Connected && (Percent!=null || Level!=null || Charge!=ChargeState.Unknown) &&
        (LastSuccess == null || now - LastSuccess > TimeSpan.FromSeconds(90))
        ? this with { Link = LinkState.Unknown, Percent = null, Level = null, Charge = ChargeState.Unknown, Detail = "資料已過期" } : this;
    public string Name => "MCHOSE A7 Pro";
    public string BatteryText => Link == LinkState.Disconnected ? "未連線" : Percent is int p ? $"{p}%" : Level switch
    { BatteryLevel.Empty => "電量耗盡", BatteryLevel.Low => "低電量", BatteryLevel.Medium => "中電量", BatteryLevel.High => "高電量", _ => "電量未知" };
    public string Tooltip => $"{Name}：{BatteryText}" + (Link==LinkState.Disconnected ? "" : Charge switch {
        ChargeState.Charging => " · 充電中 ⚡", ChargeState.Full => " · 已充滿", ChargeState.NotCharging => " · 未充電", _ => " · 充電狀態未知" });
}
public static class PacketParser
{
    public static BatteryReading? Mchose(ReadOnlySpan<byte> packet, DateTimeOffset now, bool wired=false)
    {
        if(packet.Length < 13 || packet[0]!=0x11 || packet[1]!=0xf9) return null;
        if(packet[2]==255 && packet[3]==255) return null;
        int percent=packet[11]^255, status=packet[10]^255, charge=packet[12]^255;
        if(percent>100) return null;
        if(!wired && (status & 8)==0) return new(DeviceKind.Mouse,LinkState.Disconnected,Detail:"接收器未連上滑鼠");
        // Flag 1 confirmed by physical cable insertion on this A7 Pro, 2026-10-04.
        return new(DeviceKind.Mouse,LinkState.Connected,percent,Charge:charge switch{0=>ChargeState.NotCharging,1=>ChargeState.Charging,_=>ChargeState.Unknown},LastSuccess:now,
            Detail:charge<=1?null:$"未知充電旗標 ({charge})");
    }
}
