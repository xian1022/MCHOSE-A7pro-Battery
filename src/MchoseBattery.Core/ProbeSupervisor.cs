using System.Diagnostics;

namespace MchoseBattery.Core;
public sealed record ProbeResult(string Output,string Error,int ExitCode);
public sealed class ProbeSupervisor : IDisposable
{
    readonly object gate=new();
    readonly HashSet<Process> children=new();
    readonly CancellationTokenSource shutdown=new();
    bool disposed;
    public async Task<ProbeResult> RunAsync(ProcessStartInfo start,TimeSpan timeout,CancellationToken cancellation=default)
    {
        using var process=new Process{StartInfo=start};
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation,shutdown.Token);
        start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        lock(gate) {
            ObjectDisposedException.ThrowIf(disposed,this);
            deadline.Token.ThrowIfCancellationRequested();
            process.Start();children.Add(process);
        }
        deadline.CancelAfter(timeout);
        using var cancel=deadline.Token.Register(()=>Kill(process));
        try {
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return new(await output.ConfigureAwait(false),await error.ConfigureAwait(false),process.ExitCode);
        }finally {lock(gate){Kill(process);children.Remove(process);}}
    }
    static void Kill(Process process)
    {
        try {if(!process.HasExited)process.Kill(entireProcessTree:true);}
        catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
    }
    public void Dispose()
    {
        lock(gate) {
            if(disposed)return;disposed=true;
            foreach(var process in children)Kill(process);
        }
        shutdown.Cancel();
        // Tasks can still finish disposing linked tokens after shutdown. Keep the source alive.
    }
}
