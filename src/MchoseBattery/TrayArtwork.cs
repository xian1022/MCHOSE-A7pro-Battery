using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MchoseBattery.Core;

namespace MchoseBattery;

internal static class TrayArtwork
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    public static Icon Create(BatteryReading reading,bool light,int size)
    {
        using var bitmap=Draw(reading,light,size);
        var handle=bitmap.GetHicon();
        try {using var borrowed=Icon.FromHandle(handle);return (Icon)borrowed.Clone();}
        finally {DestroyIcon(handle);}
    }
    public static Bitmap Draw(BatteryReading r,bool light,int size)
    {
        var b=new Bitmap(size,size);using var g=Graphics.FromImage(b);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/32f,size/32f);
        var foreground=r.Link==LinkState.Disconnected?Color.FromArgb(140,140,140):light?Color.FromArgb(35,40,47):Color.White;
        using var symbol=new Pen(foreground,2.5f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        using var track=new Pen(light?Color.FromArgb(200,203,207):Color.FromArgb(83,89,97),3.4f);
        const float ringInset=1.9f, ringDiameter=28.2f;
        g.DrawEllipse(track,ringInset,ringInset,ringDiameter,ringDiameter);
        if(r.Link==LinkState.Connected && (r.Percent!=null || r.Level!=null)) {
            var low=r.Percent is <=20 || r.Level is BatteryLevel.Low or BatteryLevel.Empty;
            var mid=r.Percent is <=50 || r.Level==BatteryLevel.Medium;
            using var ring=new Pen(low?Color.FromArgb(240,80,90):mid?Color.FromArgb(242,184,68):Color.FromArgb(73,200,127),3.4f){StartCap=LineCap.Round,EndCap=LineCap.Round};
            if(r.Percent is int percent && percent>0)g.DrawArc(ring,ringInset,ringInset,ringDiameter,ringDiameter,-90,percent*3.6f);
            else if(r.Level is BatteryLevel level) {
                int count=level switch{BatteryLevel.High=>3,BatteryLevel.Medium=>2,BatteryLevel.Low=>1,_=>0};
                for(int i=0;i<count;i++)g.DrawArc(ring,ringInset,ringInset,ringDiameter,ringDiameter,-88+i*120,110);
            }
        }
        var symbolState=g.Save();
        // Enlarge the artwork inside the fixed system-tray slot, keeping ring/badges separate.
        g.TranslateTransform(16,16);g.ScaleTransform(1.2f,1.2f);g.TranslateTransform(-16,-16);
        g.DrawEllipse(symbol,11,8,10,17);g.DrawLine(symbol,16,9,16,14);g.DrawLine(symbol,12,16,20,16);
        g.Restore(symbolState);
        if(r.Charge==ChargeState.Charging && r.Link==LinkState.Connected) {
            using var backdrop=new SolidBrush(light?Color.White:Color.FromArgb(30,34,40));g.FillEllipse(backdrop,16.5f,9.5f,15.5f,22.5f);
            using var bolt=new SolidBrush(Color.FromArgb(255,204,63));
            var boltPoints=new PointF[]{new(27.5f,11),new(18,23),new(23,23),new(20.5f,31),new(31,19),new(25.5f,19)};
            g.FillPolygon(bolt,boltPoints);
            using var edge=new Pen(light?Color.FromArgb(133,88,12):Color.FromArgb(30,34,40),0.8f){LineJoin=LineJoin.Round};
            g.DrawPolygon(edge,boltPoints);
        } else if(r.Link==LinkState.Unknown) {
            using var back=new SolidBrush(light?Color.White:Color.FromArgb(30,34,40));g.FillEllipse(back,21,20,10,11);
            using var font=new Font("Segoe UI",9,FontStyle.Bold,GraphicsUnit.Pixel);using var ink=new SolidBrush(foreground);g.DrawString("?",font,ink,22,20);
        }
        return b;
    }
}
