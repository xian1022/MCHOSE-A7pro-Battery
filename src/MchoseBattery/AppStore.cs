using System.Text.Json;
using Microsoft.Win32;

namespace MchoseBattery;

internal static class AppStore
{
    public static string Folder {get;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MchoseBattery");
    static readonly object Gate=new();
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool AutoStart { get {
        using var key=Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue("MchoseBattery") as string,$"\"{Environment.ProcessPath}\"",StringComparison.OrdinalIgnoreCase);
    } }
    public static void SetAutoStart(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled)key.SetValue("MchoseBattery",$"\"{Environment.ProcessPath}\"");else key.DeleteValue("MchoseBattery",false);
        Save("settings.json",new {AutoStart=enabled,RefreshSeconds=30});
    }
    public static void Save(string name,object value)
    {
        lock(Gate) {
            Directory.CreateDirectory(Folder);
            var path=Path.Combine(Folder,name);var tmp=path+".tmp";
            File.WriteAllText(tmp,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(tmp,path,true);
        }
    }
    public static void Log(string message)
    {
        try {lock(Gate) {
            Directory.CreateDirectory(Folder);var path=Path.Combine(Folder,"battery.log");
            if(File.Exists(path) && new FileInfo(path).Length>1_000_000)File.Move(path,path+".1",true);
            File.AppendAllText(path,$"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
