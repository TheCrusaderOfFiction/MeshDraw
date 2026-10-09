using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshDraw;

public sealed class FilterDefinition
{
    public string Key {get;set;}="";
    public string Name {get;set;}="";
    public string Category {get;set;}="";
    public string Note {get;set;}="";
    public List<FilterControl> Controls {get;set;}=[];
    public override string ToString()=>Name;
}
public sealed class FilterControl
{
    public string Key {get;set;}="";
    public string Label {get;set;}="";
    public string Type {get;set;}="number";
    public double Min {get;set;}
    public double Max {get;set;}
    public double Value {get;set;}
    public double Step {get;set;}
    public string Text {get;set;}="";
}
public static class FilterEngine
{
    public static List<FilterDefinition> Catalog(string root)=>JsonSerializer.Deserialize<List<FilterDefinition>>(File.ReadAllText(Path.Combine(root,"backend","filter_catalog.json")),new JsonSerializerOptions {PropertyNameCaseInsensitive=true}) ?? [];
    public static BitmapSource Bitmap(byte[] pixels,int width,int height)
    {
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,width*4);bitmap.Freeze();return bitmap;
    }
    public static void BlendSelection(byte[] original,byte[] filtered,bool[]? selection,double amount)
    {
        if(filtered.Length!=original.Length || (selection!=null && selection.Length!=original.Length/4)) throw new ArgumentException("Filter dimensions changed.");
        amount=Math.Clamp(amount,0,1);
        for(int i=0;i<original.Length;i+=4)
            for(int c=0;c<4;c++) filtered[i+c]=selection!=null && !selection[i/4] ? original[i+c] : (byte)Math.Round(original[i+c]*(1-amount)+filtered[i+c]*amount);
    }
    public static async Task<byte[]> Run(string root,byte[] pixels,int width,int height,FilterDefinition filter,Dictionary<string,object> parameters,CancellationToken token,double scale=1)
    {
        var python=Path.Combine(root,"local-ai","venv","Scripts","python.exe");
        if(!File.Exists(python)) throw new InvalidOperationException("Install the local engine with setup-local.ps1 to use filters.");
        var folder=Path.Combine(root,"outputs",".filter-cache",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var input=Path.Combine(folder,"input.png");var output=Path.Combine(folder,"output.png");var job=Path.Combine(folder,"job.json");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Bitmap(pixels,width,height)));
            using(var stream=File.Create(input)) encoder.Save(stream);
            File.WriteAllText(job,JsonSerializer.Serialize(new {input,output,filter=filter.Key,@params=parameters,scale}));
            var start=new ProcessStartInfo(python) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=root};
            start.ArgumentList.Add(Path.Combine(root,"backend","filters.py"));start.ArgumentList.Add("--job");start.ArgumentList.Add(job);
            start.Environment["PYTHONIOENCODING"]="utf-8";
            using var process=new Process {StartInfo=start};token.ThrowIfCancellationRequested();process.Start();
            var stderr=process.StandardError.ReadToEndAsync();var stdout=process.StandardOutput.ReadToEndAsync();
            using var registration=token.Register(()=> {try {if(!process.HasExited) process.Kill(true);} catch(InvalidOperationException) {} });
            await process.WaitForExitAsync();token.ThrowIfCancellationRequested();
            var error=await stderr;await stdout;
            if(process.ExitCode!=0) throw new InvalidOperationException("Filter failed: "+error.Trim());
            using var result=File.OpenRead(output);
            var decoder=BitmapDecoder.Create(result,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            var converted=new FormatConvertedBitmap(decoder.Frames[0],PixelFormats.Pbgra32,null,0);
            if(converted.PixelWidth!=width || converted.PixelHeight!=height) throw new InvalidOperationException("Filter returned the wrong canvas size.");
            var data=new byte[pixels.Length];converted.CopyPixels(data,width*4,0);return data;
        }
        finally
        {
            // Only these fixed files in our own unique job folder are removed.
            foreach(var name in new[]{"input.png","output.png","job.json"}) try {File.Delete(Path.Combine(folder,name));} catch(IOException) {}
            try {Directory.Delete(folder);} catch(IOException) {}
        }
    }
}
