using System.Text.Json;
using MchoseBattery.Core;

namespace MchoseBattery;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--probe" && Enum.TryParse<DeviceKind>(args[1],out var kind)) {
            // Hardware I/O runs in a disposable worker process with a five-second deadline.
            var reading=Task.Run(()=>HardwareReader.Read(kind)).GetAwaiter().GetResult();
            Console.WriteLine(JsonSerializer.Serialize(reading));return 0;
        }
        if(args.Length==2 && args[0]=="--preview") {Preview(args[1]);return 0;}
        if(args.Length==2 && args[0]=="--diagnose") {
            var readings=Task.WhenAll(Enum.GetValues<DeviceKind>().Select(k=>WorkerClient.ReadAsync(k,CancellationToken.None))).GetAwaiter().GetResult();
            File.WriteAllText(args[1],JsonSerializer.Serialize(readings,new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        using var singleton=new Mutex(true,@"Local\MchoseBattery.A7Pro.v1",out bool created);
        if(!created)return 0;
        try {
            ApplicationConfiguration.Initialize();
            AppStore.InitializeAutoStart();
            Application.ThreadException+=(_,e)=>AppStore.Log(e.Exception.ToString());
            AppDomain.CurrentDomain.UnhandledException+=(_,e)=>AppStore.Log(e.ExceptionObject.ToString()!);
            Application.Run(new TrayContext());return 0;
        }catch(Exception e){AppStore.Log(e.ToString());MessageBox.Show("無法啟動MCHOSE A7 Pro 電量，請查看診斷紀錄。","MCHOSE A7 Pro 電量");return 1;}
        finally {singleton.ReleaseMutex();}
    }
    static void Preview(string path)
    {
        using var image=new Bitmap(520,280);using var g=Graphics.FromImage(image);g.Clear(Color.FromArgb(25,29,36));
        using var font=new Font("Segoe UI",12);
        for(int row=0;row<2;row++) {
            bool light=row==1;using var bg=new SolidBrush(light?Color.FromArgb(245,246,248):Color.FromArgb(25,29,36));g.FillRectangle(bg,0,row*140,520,140);
            var examples=new[]{new BatteryReading(DeviceKind.Mouse,LinkState.Connected,96),
                new BatteryReading(DeviceKind.Mouse,LinkState.Connected,65,Charge:ChargeState.Charging),
                new BatteryReading(DeviceKind.Mouse),new BatteryReading(DeviceKind.Mouse,LinkState.Disconnected)};
            string[] labels={"96%","Charging","Unknown","Offline"};
            for(int i=0;i<examples.Length;i++) {
                using var icon=TrayArtwork.Draw(examples[i],light,40);g.DrawImageUnscaled(icon,30+i*130,25+row*140);
                using var tiny=TrayArtwork.Draw(examples[i],light,16);g.DrawImageUnscaled(tiny,85+i*130,40+row*140);
                using var ink=new SolidBrush(light?Color.Black:Color.White);g.DrawString(labels[i],font,ink,24+i*130,90+row*140);
            }
        }
        image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
    }
}
