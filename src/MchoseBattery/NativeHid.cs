using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MchoseBattery;

// Enumerate interface paths without asking unrelated peripherals for USB strings.
// This is important when a sleeping/stalled receiver holds its control endpoint.
internal sealed class NativeHid : IDisposable
{
    public string Path {get;}
    public int ProductId {get;}
    public int InputLength {get;}
    readonly SafeFileHandle handle;
    readonly FileStream stream;
    NativeHid(string path,int pid,HidCaps caps)
    {
        Path=path;ProductId=pid;InputLength=caps.Input;
        handle=CreateFileW(path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(handle.IsInvalid){var code=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(code);}
        try{stream=new FileStream(handle,FileAccess.ReadWrite,1,true);}catch{handle.Dispose();throw;}
    }
    public static NativeHid? Open(int vid,int pid,int page,int inputLength)
    {
        var matches=Paths().Where(p=>p.Contains($"vid_{vid:x4}&pid_{pid:x4}",StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach(var path in matches) {
            using var info=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if(info.IsInvalid)continue;
            if(!HidD_GetPreparsedData(info,out var data))continue;
            try {
                if(HidP_GetCaps(data,out var caps)!=0x00110000)continue;
                if(caps.UsagePage==page && caps.Input==inputLength)return new(path,pid,caps);
            }finally {HidD_FreePreparsedData(data);}
        }
        if(matches.Length>0)throw new IOException("裝置存在，但電量 HID 介面無法開啟");
        return null;
    }
    public static bool IsPresent(int vid,params int[] products) => Paths().Any(path=>
        products.Any(pid=>path.Contains($"vid_{vid:x4}&pid_{pid:x4}",StringComparison.OrdinalIgnoreCase)));
    static IEnumerable<string> Paths()
    {
        var cls=new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        for(int attempt=0;attempt<3;attempt++) {
            uint result=CM_Get_Device_Interface_List_SizeW(out var size,ref cls,null,0);
            if(result!=0)throw new IOException($"HID 列舉失敗 ({result})");
            var buffer=new char[size];result=CM_Get_Device_Interface_ListW(ref cls,null,buffer,size,0);
            if(result==0)return new string(buffer).Split('\0',StringSplitOptions.RemoveEmptyEntries);
            if(result!=0x1a)throw new IOException($"HID 列舉失敗 ({result})");
        }
        throw new IOException("裝置正在重新連線");
    }
    public void SetFeature(byte[] report)
    {
        if(!HidD_SetFeature(handle,report,report.Length))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public byte[] GetFeature(byte id,int length)
    {
        var report=new byte[length];report[0]=id;
        if(!HidD_GetFeature(handle,report,report.Length))throw new Win32Exception(Marshal.GetLastWin32Error());
        return report;
    }
    public void Write(byte[] report)
    {
        using var timeout=new CancellationTokenSource(600);
        try{stream.WriteAsync(report,timeout.Token).AsTask().GetAwaiter().GetResult();}
        catch(OperationCanceledException){throw new TimeoutException("HID 寫入逾時");}
    }
    public int Read(byte[] report)
    {
        using var timeout=new CancellationTokenSource(600);
        try{return stream.ReadAsync(report,timeout.Token).AsTask().GetAwaiter().GetResult();}
        catch(OperationCanceledException){throw new TimeoutException("HID 讀取逾時");}
    }
    public void Dispose(){stream.Dispose();handle.Dispose();}

    [StructLayout(LayoutKind.Sequential)]
    struct HidCaps {
        public ushort Usage,UsagePage,Input,Output,Feature;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
        public ushort LinkNodes,InputButtons,InputValues,InputIndices,OutputButtons,OutputValues,OutputIndices,FeatureButtons,FeatureValues,FeatureIndices;
    }
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_Interface_List_SizeW(out uint length,ref Guid cls,string? device,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_Interface_ListW(ref Guid cls,string? device,[Out] char[] buffer,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle file,out IntPtr data);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out HidCaps caps);
    [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_SetFeature(SafeFileHandle file,byte[] report,int length);
    [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetFeature(SafeFileHandle file,[In,Out] byte[] report,int length);
}
