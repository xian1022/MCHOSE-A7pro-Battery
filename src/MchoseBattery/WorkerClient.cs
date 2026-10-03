using System.Diagnostics;
using System.Text.Json;
using MchoseBattery.Core;

namespace MchoseBattery;

internal static class WorkerClient
{
    static readonly ProbeSupervisor supervisor=new();
    public static void Shutdown()=>supervisor.Dispose();
    public static async Task<BatteryReading> ReadAsync(DeviceKind kind, CancellationToken stopping)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
        };
        start.ArgumentList.Add("--probe");start.ArgumentList.Add(kind.ToString());
        try {
            var result=await supervisor.RunAsync(start,TimeSpan.FromSeconds(5),stopping);
            var reading=JsonSerializer.Deserialize<BatteryReading>(result.Output);
            var details=result.Error;
            if(result.ExitCode!=0 || reading==null || reading.Device!=kind || reading.Percent is <0 or >100)
                throw new InvalidDataException("Invalid worker result");
            AppStore.Log(kind+": "+reading.Tooltip+"; "+reading.Detail+"; "+details.Trim());
            return reading;
        } catch(Exception e) {
            AppStore.Log(kind+": "+e.GetType().Name+" "+e.Message);
            return new(kind,Detail:e is OperationCanceledException?"讀取逾時":"讀取失敗");
        }
    }
}
