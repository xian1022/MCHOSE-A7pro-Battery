using MchoseBattery.Core;
using System.Diagnostics;

if(args.Contains("--hang")){Thread.Sleep(30_000);return 0;}
if(args.Contains("--echo")){Console.Write("fixture");return 0;}

var now = DateTimeOffset.UtcNow;
ProcessStartInfo Child(string arg) {var s=new ProcessStartInfo(Environment.ProcessPath!);s.ArgumentList.Add(arg);return s;}
var failures = 0; var total = 0;
void Test(string name, Action action) { total++; try { action(); Console.WriteLine("PASS " + name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Check(bool value, string message) { if(!value) throw new Exception(message); }
var mouse = Convert.FromHexString("11F9ACADEFFFFBFAF8FFF69FFF9EFFFFFFFFFFFFFF");
Test("Captured MCHOSE short feature has 96% and wireless link", () => {
    var r=PacketParser.Mchose(mouse,now);
    Check(r?.Percent==96 && r.Link==LinkState.Connected && r.Charge==ChargeState.NotCharging,"wrong decoded reading");
});
Test("MCHOSE validates response identity, range, and connected bit", () => {
    Check(PacketParser.Mchose(mouse.AsSpan(0,12),now)==null,"truncated accepted");
    var bad=(byte[])mouse.Clone();bad[1]=0xfc;Check(PacketParser.Mchose(bad,now)==null,"other command accepted");
    bad=(byte[])mouse.Clone();bad[11]=(byte)(101^255);Check(PacketParser.Mchose(bad,now)==null,"invalid level accepted");
    bad=(byte[])mouse.Clone();bad[10]=(byte)(1^255);var r=PacketParser.Mchose(bad,now);
    Check(r?.Link==LinkState.Disconnected && r.Percent==null && r.Charge==ChargeState.Unknown,"offline shows stale battery");
});
Test("Stale data clears level and charging but preserves timestamp", () => {
    var r=new BatteryReading(DeviceKind.Mouse,LinkState.Connected,96,null,ChargeState.Charging,now.AddMinutes(-3));
    var stale=r.FreshAt(now);Check(stale.Percent==null && stale.Charge==ChargeState.Unknown && stale.LastSuccess==r.LastSuccess,"stale battery shown");
});
Test("Known levels use words, never fake percentages", () => {
    var r=new BatteryReading(DeviceKind.Mouse,LinkState.Connected,null,BatteryLevel.Medium,ChargeState.Unknown,now);
    Check(r.Tooltip.Contains("中") && !r.Tooltip.Contains("%"),"level incorrectly rendered");
});
Test("Fresh real zero stays zero", () => {
    var r=new BatteryReading(DeviceKind.Mouse,LinkState.Connected,0,null,ChargeState.NotCharging,now).FreshAt(now);
    Check(r.Percent==0 && r.Tooltip.Contains("0%"),"zero discarded");
});
Test("MCHOSE all-zero idle response is unknown, not disconnection", () => {
    var idle=Enumerable.Repeat((byte)255,21).ToArray();idle[0]=0x11;idle[1]=0xf9;
    Check(PacketParser.Mchose(idle,now)==null,"idle echo accepted");
});
Test("Workers capture output without waiting on unrelated devices", () => {
    using var supervisor=new ProbeSupervisor();
    var r=supervisor.RunAsync(Child("--echo"),TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    Check(r.Output=="fixture" && r.ExitCode==0,"output not captured");
});
Test("Hung worker times out and is terminated", () => {
    using var supervisor=new ProbeSupervisor();var timedOut=false;var sw=Stopwatch.StartNew();
    try {supervisor.RunAsync(Child("--hang"),TimeSpan.FromMilliseconds(250)).GetAwaiter().GetResult();}catch(OperationCanceledException){timedOut=true;}
    Check(timedOut && sw.Elapsed<TimeSpan.FromSeconds(3),"deadline not enforced");
});
Test("Shutdown ends outstanding workers and rejects new ones", () => {
    var supervisor=new ProbeSupervisor();var task=supervisor.RunAsync(Child("--hang"),TimeSpan.FromSeconds(30));
    supervisor.Dispose();var sw=Stopwatch.StartNew();
    try{task.GetAwaiter().GetResult();}catch(OperationCanceledException){}
    Check(sw.Elapsed<TimeSpan.FromSeconds(3),"shutdown did not stop child");
    bool rejected=false;try{supervisor.RunAsync(Child("--echo"),TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();}catch(ObjectDisposedException){rejected=true;}
    Check(rejected,"worker started after exit");
});
Test("Resume rejects pre-suspend completion and accepts fresh read", () => {
    var store=new ReadingStore();var epoch=store.Epoch;
    var r=new BatteryReading(DeviceKind.Mouse,LinkState.Connected,96,Charge:ChargeState.Charging,LastSuccess:now);
    Check(store.Apply(r,epoch),"initial result rejected");store.Invalidate("resume");
    Check(!store.Apply(r,epoch),"pre-resume result accepted");
    Check(store.Readings[DeviceKind.Mouse].Percent==null,"old ring restored");
    Check(store.Apply(r with{Percent=95},store.Epoch),"fresh read rejected");
    Check(store.Readings[DeviceKind.Mouse].Percent==95,"fresh data missing");
});
Test("Captured wired MCHOSE charging uses direct USB link", () => {
    var r=PacketParser.Mchose(Convert.FromHexString("11F9ACADEFFFFBFAF8FFFF9FFE9FFFFFFFFFFFFFFF"),now,wired:true);
    Check(r?.Percent==96 && r.Link==LinkState.Connected && r.Charge==ChargeState.Charging,"wired charging packet misread as offline");
});
Test("Mouse unknown charge flag does not light lightning", () => {
    var b=Convert.FromHexString("11F9ACADEFFFFBFAF8FFFF9FFC9FFFFFFFFFFFFFFF");
    var r=PacketParser.Mchose(b,now,wired:true);Check(r?.Charge==ChargeState.Unknown,"unknown charging flag accepted");
});
Test("Unknown battery keeps the actual diagnostic reason", () => {
    var r=new BatteryReading(DeviceKind.Mouse,LinkState.Connected,Detail:"未驗證的 Windows 回報");
    Check(r.FreshAt(now).Detail==r.Detail,"unknown replaced by expired");
    var untrusted=new BatteryReading(DeviceKind.Mouse,LinkState.Connected,96,Charge:ChargeState.Charging);
    Check(untrusted.FreshAt(now).Percent==null,"untimestamped value accepted");
});
Test("Tooltip explicitly distinguishes not charging and unknown charging", () => {
    var r=new BatteryReading(DeviceKind.Mouse,LinkState.Connected,96,Charge:ChargeState.NotCharging,LastSuccess:now);
    Check(r.Tooltip.Contains("未充電"),"missing charging state");
    Check((r with{Charge=ChargeState.Unknown}).Tooltip.Contains("充電狀態未知"),"unknown charge omitted");
    Check(!new BatteryReading(DeviceKind.Mouse,LinkState.Disconnected).Tooltip.Contains("充電"),"offline misleading charging label");
});
Console.WriteLine($"{total-failures}/{total} passed");
return failures == 0 ? 0 : 1;
