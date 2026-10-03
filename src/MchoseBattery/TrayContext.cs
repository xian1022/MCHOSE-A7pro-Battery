using MchoseBattery.Core;
using Microsoft.Win32;

namespace MchoseBattery;

internal sealed class TrayContext : ApplicationContext
{
    readonly Control dispatcher=new();
    readonly Dictionary<DeviceKind,NotifyIcon> icons=new();
    readonly ReadingStore store=new();
    Dictionary<DeviceKind,BatteryReading> readings=>store.Readings;
    readonly HashSet<DeviceKind> busy=new();
    readonly CancellationTokenSource stopping=new();
    readonly System.Windows.Forms.Timer refresh=new(){Interval=30_000};
    readonly System.Windows.Forms.Timer freshness=new(){Interval=5000};
    readonly ToolStripMenuItem startup=new("登入時自動啟動");
    readonly ContextMenuStrip menu=new();
    bool exiting;
    public TrayContext()
    {
        _=dispatcher.Handle;
        menu.Items.Add("MCHOSE A7 Pro 電量",null,(_,_)=>ShowStatus());
        menu.Items.Add("立即重新整理",null,(_,_)=>RefreshAll());
        startup.Checked=AppStore.AutoStart;
        startup.Click+=(_,_)=> {
            try {AppStore.SetAutoStart(!AppStore.AutoStart);startup.Checked=AppStore.AutoStart;}
            catch(Exception e){AppStore.Log(e.ToString());MessageBox.Show("無法更新開機啟動設定。","MCHOSE A7 Pro 電量");}
        };
        menu.Items.Add(startup);menu.Items.Add("開啟診斷資料夾",null,(_,_)=> {
            Directory.CreateDirectory(AppStore.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppStore.Folder){UseShellExecute=true});
        });
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("結束",null,(_,_)=>ExitThread());
        foreach(var kind in Enum.GetValues<DeviceKind>()) {
            readings[kind]=new(kind);icons[kind]=new NotifyIcon{ContextMenuStrip=menu,Text=readings[kind].Tooltip};
            icons[kind].MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left)ShowStatus();};
        }
        Render();foreach(var icon in icons.Values)icon.Visible=true;
        refresh.Tick+=(_,_)=>RefreshAll();freshness.Tick+=(_,_)=>Render();refresh.Start();freshness.Start();
        SystemEvents.PowerModeChanged+=PowerChanged;
        SystemEvents.UserPreferenceChanged+=PreferencesChanged;
        HidSharp.DeviceList.Local.Changed+=DevicesChanged;
        AppStore.Log("Started "+Environment.ProcessPath);
        RefreshAll();
    }
    void Dispatch(Action action)
    {
        if(exiting || dispatcher.IsDisposed)return;
        try {dispatcher.BeginInvoke(action);}catch(InvalidOperationException){}
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e)
    {
        if(e.Mode==PowerModes.Resume)Dispatch(()=>{InvalidateReadings("電腦剛恢復，正在更新");RefreshAll();});
        else if(e.Mode==PowerModes.Suspend)Dispatch(()=>InvalidateReadings("電腦休眠"));
    }
    void PreferencesChanged(object sender,UserPreferenceChangedEventArgs e)=>Dispatch(Render);
    void DevicesChanged(object? sender,EventArgs e)=>Dispatch(RefreshAll);
    void InvalidateReadings(string reason)
    {
        store.Invalidate(reason);
        Render();
    }
    void RefreshAll()
    {
        if(exiting)return;
        foreach(var kind in Enum.GetValues<DeviceKind>())if(busy.Add(kind))_ = RefreshOne(kind);
    }
    async Task RefreshOne(DeviceKind kind)
    {
        int epoch=store.Epoch;
        var result=await WorkerClient.ReadAsync(kind,stopping.Token).ConfigureAwait(false);
        Dispatch(()=> {
            busy.Remove(kind);
            if(!store.Apply(result,epoch)){RefreshAll();return;}
            Render();
            try {AppStore.Save("status.json",readings.Values.ToArray());}catch(Exception e){AppStore.Log(e.Message);}
        });
    }
    static bool LightTheme()
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value==1;
    }
    void Render()
    {
        if(exiting)return;
        var light=LightTheme();var size=Math.Max(16,32*dispatcher.DeviceDpi/96);
        foreach(var kind in readings.Keys.ToArray()) {
            var r=readings[kind].FreshAt(DateTimeOffset.UtcNow);
            var old=icons[kind].Icon;icons[kind].Icon=TrayArtwork.Create(r,light,size);old?.Dispose();
            icons[kind].Text=r.Tooltip;
        }
    }
    void ShowStatus()
    {
        var lines=readings.Values.Select(r=>r.FreshAt(DateTimeOffset.UtcNow)).Select(r=>r.Tooltip+
            (r.Detail==null?"":"\n"+r.Detail)+(r.LastSuccess is {} t?$"\n最後取得資料：{t.LocalDateTime:HH:mm:ss}":""));
        MessageBox.Show(string.Join("\n\n",lines),"MCHOSE A7 Pro 電量",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    protected override void ExitThreadCore()
    {
        exiting=true;WorkerClient.Shutdown();stopping.Cancel();refresh.Stop();freshness.Stop();refresh.Dispose();freshness.Dispose();
        SystemEvents.PowerModeChanged-=PowerChanged;SystemEvents.UserPreferenceChanged-=PreferencesChanged;
        HidSharp.DeviceList.Local.Changed-=DevicesChanged;
        foreach(var icon in icons.Values){icon.Visible=false;var image=icon.Icon;icon.Dispose();image?.Dispose();}
        menu.Dispose();dispatcher.Dispose();AppStore.Log("Stopped");base.ExitThreadCore();
    }
}
