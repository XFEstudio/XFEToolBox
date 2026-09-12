using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using ImageMagick.Drawing;
using XFEToolBox.Core.Tools;
using XFEToolBox.Tools.FormatConverter;

namespace FormatConverter.Validation;
internal static class Program
{
    private static readonly string Workspace=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XFEToolBox","CrossVersion","EditorWorkspaces","FormatConverter");
    private static readonly string Root=Path.Combine(AppContext.BaseDirectory,"test-artifacts","run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    private static readonly List<string> Passed=[];private static int exitCode;
    [STAThread]private static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml")});
        app.Startup+=async(_,_)=>{try{await RunAsync(args);}catch(Exception e){exitCode=1;Console.Error.WriteLine(e);}finally{File.WriteAllText(Path.Combine(Root,"results.json"),JsonSerializer.Serialize(new{success=exitCode==0,checks=Passed},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Artifacts: "+Root);app.Shutdown();}};app.Run();return exitCode;
    }
    private static void Assert(bool condition,string name){if(!condition)throw new InvalidOperationException("FAIL: "+name);Passed.Add(name);Console.WriteLine("PASS: "+name);}
    private static async Task Rejected(Func<Task> action,string name){bool rejected=false;try{await action();}catch{rejected=true;}Assert(rejected,name);}
    private static async Task RunAsync(string[] args)
    {
        ImageEngine.Initialize();var images=FormatCatalog.Images();Console.WriteLine("Image writers: "+string.Join(", ",images.Select(f=>f.Id)));
        var engines=await EngineSetup.FindAsync(default)??throw new InvalidOperationException("No FFmpeg available for real conversion validation");
        Console.WriteLine("FFmpeg: "+engines.Ffmpeg);var encoders=await MediaEngine.EncodersAsync(engines,default);
        var service=new ConversionService(engines);var output=Path.Combine(Root,"outputs");Directory.CreateDirectory(output);
        string png=Path.Combine(Root,"测试 图像 [1].png");
        using(var source=new MagickImage(MagickColors.Transparent,128,128))
        {source.Draw(new Drawables().FillColor(MagickColors.CornflowerBlue).Rectangle(0,0,127,63).FillColor(MagickColors.Orange).Circle(64,80,90,80));using var target=File.Create(png);source.Write(target,MagickFormat.Png);}
        var originalHash=SHA256.HashData(File.ReadAllBytes(png));var generated=new Dictionary<string,string>();
        var imageFailures=new List<string>();
        foreach(var f in images)
        {
            try{
            var result=await service.ConvertAsync(png,new(f,output),null,default);generated[f.Id]=result.Path;
            using var stream=File.OpenRead(result.Path);using var check=new MagickImage();check.Read(stream,new MagickReadSettings{Format=Enum.Parse<MagickFormat>(f.Id,true),FrameCount=1});
            Assert(check.Width>0&&check.Height>0,"real image encode/decode: PNG -> "+f.Id);
            }catch(Exception e){imageFailures.Add(f.Id+": "+e.Message);Console.WriteLine("IMAGE FAILURE: "+f.Id+": "+e.Message);}
        }
        Assert(imageFailures.Count==0,"all advertised image writers round-trip: "+string.Join(" | ",imageFailures));
        Assert(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(png))),"image source bytes unchanged");
        foreach(string code in new[]{"ICO","DDS","JPEG","WEBP","TIFF","TGA","AVIF","HEIC","PSD"}.Where(generated.ContainsKey))
        {
            var back=await service.ConvertAsync(generated[code],new(images.Single(f=>f.Id=="PNG"),output),null,default);
            using var data=File.OpenRead(back.Path);using var image=new MagickImage(data);
            Assert(image.Width==(code=="ICO"?256u:128u),"real "+code+" -> PNG dimensions");
        }
        using(var data=File.OpenRead(generated["ICO"]))using(var icons=new MagickImageCollection())
        {icons.Read(data,new MagickReadSettings{Format=MagickFormat.Ico});Assert(icons.Select(i=>i.Width).Order().SequenceEqual(new uint[]{16,24,32,48,64,128,256}),"ICO contains seven icon sizes");}
        byte[] dds=File.ReadAllBytes(generated["DDS"]);Assert(Encoding.ASCII.GetString(dds,0,4)=="DDS "&&Encoding.ASCII.GetString(dds,84,4)=="DXT5","DDS header and DXT5 compression validated");
        string gif=Path.Combine(Root,"two-frames.gif");using(var frames=new MagickImageCollection())
        {frames.Add(new MagickImage(MagickColors.Red,64,64){AnimationDelay=10});frames.Add(new MagickImage(MagickColors.Blue,64,64){AnimationDelay=20});using var target=File.Create(gif);frames.Write(target,MagickFormat.Gif);}
        var animated=await service.ConvertAsync(gif,new(images.Single(f=>f.Id=="GIF"),output,FirstFrameOnly:false),null,default);
        using(var stream=File.OpenRead(animated.Path))using(var frames=new MagickImageCollection()){frames.Read(stream,new MagickReadSettings{Format=MagickFormat.Gif});Assert(frames.Count==2,"animation frames retained when requested");}
        await Rejected(()=>service.ConvertAsync(gif,new(images.Single(f=>f.Id=="JPEG"),output,FirstFrameOnly:false),null,default),"multi-frame loss rejected for single-frame output");
        string wav=Path.Combine(Root,"测试 音频.wav"), video=Path.Combine(Root,"test-video.mp4");
        await FfmpegAsync(engines,["-f","lavfi","-i","sine=frequency=440:duration=1","-c:a","pcm_s16le",wav]);
        await FfmpegAsync(engines,["-f","lavfi","-i","testsrc2=size=160x120:rate=12:duration=1","-f","lavfi","-i","sine=frequency=880:duration=1","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac","-shortest",video]);
        var audioPaths=new Dictionary<string,string>();
        foreach(var f in FormatCatalog.Audio.Where(f=>f.Id!="original"&&MediaEngine.Available(f,encoders)))
        {
            var result=await service.ConvertAsync(wav,new(f,output),null,default);var probe=await MediaEngine.ProbeAsync(engines,result.Path,default);audioPaths[f.Id]=result.Path;
            Assert(probe.Audio&&probe.Duration>.5,"real audio conversion WAV -> "+f.Label);
            await FfmpegAsync(engines,["-i",result.Path,"-f","null","-"]);Assert(true,"audio fully decoded: "+f.Id);
        }
        foreach(var f in FormatCatalog.Video.Where(f=>MediaEngine.Available(f,encoders)))
        {
            var result=await service.ConvertAsync(video,new(f,output),null,default);var probe=await MediaEngine.ProbeAsync(engines,result.Path,default);
            Assert(probe.Video&&probe.Duration>.5&&(f.Id=="gif-video"||probe.Audio),"real video conversion MP4 -> "+f.Label);
            await FfmpegAsync(engines,["-i",result.Path,"-f","null","-"]);Assert(true,"video fully decoded: "+f.Id);
        }
        var extracted=await service.ConvertAsync(video,new(FormatCatalog.Audio.Single(f=>f.Id=="mp3"),output),null,default);
        Assert((await MediaEngine.ProbeAsync(engines,extracted.Path,default)).Audio,"video audio extraction");
        await NcmTestsAsync(service,engines,audioPaths,output);
        await CancellationTestsAsync(service,engines,video,output);
        await GuardTestsAsync(service,images,png,output);
        await SetupTestsAsync();await ViewTestsAsync(png);
        MakeIcon();if(!args.Contains("--skip-host"))await HostAsync(args);
        Console.WriteLine($"ALL {Passed.Count} CHECKS PASSED");
    }
    private static async Task FfmpegAsync(EnginePaths engines,IEnumerable<string> args)
    {var result=await ChildProcess.RunAsync(engines.Ffmpeg,new[]{"-hide_banner","-v","error","-nostdin","-n"}.Concat(args),default,timeout:TimeSpan.FromSeconds(30));if(result.ExitCode!=0)throw new InvalidOperationException(result.Error);}
    private static async Task NcmTestsAsync(ConversionService service,EnginePaths engines,Dictionary<string,string> sources,string output)
    {
        foreach(string type in new[]{"mp3","flac"})
        {
            string longAudio=Path.Combine(Root,"long."+type);
            await FfmpegAsync(engines,["-f","lavfi","-i","sine=frequency=500:duration=12","-c:a",type=="mp3"?"libmp3lame":"flac",longAudio]);
            string ncm=Path.Combine(Root,"fixture-"+type+".ncm");byte[] audio=File.ReadAllBytes(longAudio);CreateNcm(ncm,audio);
            Assert(audio.Length>65536,"NCM fixture crosses streaming buffer boundary: "+type);
            var original=await service.ConvertAsync(ncm,new(FormatCatalog.Audio[0],output),null,default);
            Assert(Path.GetExtension(original.Path)=="."+type&&audio.SequenceEqual(File.ReadAllBytes(original.Path)),"NCM "+type+" bit-exact original extraction");
            var wav=await service.ConvertAsync(ncm,new(FormatCatalog.Audio.Single(f=>f.Id=="wav"),output),null,default);
            Assert((await MediaEngine.ProbeAsync(engines,wav.Path,default)).Audio,"NCM "+type+" -> WAV real transcode");
            await FfmpegAsync(engines,["-i",wav.Path,"-f","null","-"]);
            var offline=await new ConversionService(null).ConvertAsync(ncm,new(FormatCatalog.Audio[0],output),null,default);
            Assert(audio.SequenceEqual(File.ReadAllBytes(offline.Path)),"NCM original extraction works without FFmpeg: "+type);
        }
        foreach(byte[] bad in new[]{new byte[10],"CTENFDAM\0\0\xFF\xFF\xFF\xFF"u8.ToArray(),File.ReadAllBytes(Path.Combine(Root,"fixture-mp3.ncm"))[..25]})
        {string path=Path.Combine(Root,Guid.NewGuid()+".ncm");File.WriteAllBytes(path,bad);await Rejected(()=>service.ConvertAsync(path,new(FormatCatalog.Audio[0],output),null,default),"corrupt/truncated NCM rejected");}
    }
    private static async Task CancellationTestsAsync(ConversionService service,EnginePaths engines,string video,string output)
    {
        using(var token=new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
        {
            bool cancelled=false;var watch=Stopwatch.StartNew();
            try{await ChildProcess.RunAsync(engines.Ffmpeg,["-nostdin","-v","error","-re","-f","lavfi","-i","sine=duration=30","-f","null","-"],token.Token);}catch(OperationCanceledException){cancelled=true;}
            Assert(cancelled&&watch.Elapsed<TimeSpan.FromSeconds(6),"running FFmpeg cancellation stops only its process");
        }
        bool timeout=false;
        try{await ChildProcess.RunAsync(engines.Ffmpeg,["-nostdin","-v","error","-re","-f","lavfi","-i","sine=duration=30","-f","null","-"],default,timeout:TimeSpan.FromMilliseconds(500));}catch(TimeoutException){timeout=true;}
        Assert(timeout,"FFmpeg timeout returns controlled failure");
        using(var cts=new CancellationTokenSource())
        {
            bool cancelled=false;int before=Directory.GetFiles(output).Length;
            var progress=new InlineProgress(p=>cts.Cancel());
            try{await service.ConvertAsync(video,new(FormatCatalog.Video.Single(f=>f.Id=="webm"),output),progress,cts.Token);}catch(OperationCanceledException){cancelled=true;}
            Assert(cancelled&&before==Directory.GetFiles(output).Length&&!Directory.EnumerateDirectories(output,".xfe-convert-*").Any(),"active conversion cancel publishes no partial output and cleans workspace");
        }
        var resized=await service.ConvertAsync(video,new(FormatCatalog.Video[0],output,MaxEdge:80),null,default);
        Assert((await MediaEngine.ProbeAsync(engines,resized.Path,default)).Video,"video resize preset executes successfully");
        Assert(!MediaEngine.SupportsInput("test.avs")&&!MediaEngine.SupportsInput("test.m3u8"),"script and remote playlist inputs excluded");
        string fake=Path.Combine(Root,"script.mp4");File.WriteAllText(fake,"ffconcat version 1.0\nfile 'nonexistent.wav'\n");
        await Rejected(()=>MediaEngine.ProbeAsync(engines,fake,default),"disguised concat input rejected by demuxer whitelist");
    }
    private static void CreateNcm(string path,byte[] audio)
    {
        byte[] secret=Enumerable.Range(1,37).Select(i=>(byte)(i*3)).ToArray();using var aes=Aes.Create();aes.Key=Convert.FromHexString("687A4852416D736F356B496E62617857");
        byte[] key=aes.EncryptEcb([.."neteasecloudmusic"u8,..secret],PaddingMode.PKCS7);for(int i=0;i<key.Length;i++)key[i]^=0x64;
        byte[] box=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();int last=0;
        for(int i=0;i<256;i++){last=(box[i]+last+secret[i%secret.Length])&255;(box[i],box[last])=(box[last],box[i]);}
        byte[] payload=audio.ToArray();for(int i=0;i<payload.Length;i++){int j=(i+1)&255;payload[i]^=box[(box[j]+box[(box[j]+j)&255])&255];}
        using var stream=File.Create(path);using var writer=new BinaryWriter(stream);writer.Write("CTENFDAM"u8);writer.Write((ushort)0);writer.Write(key.Length);writer.Write(key);writer.Write(0);writer.Write(new byte[5]);writer.Write(32);writer.Write(8);writer.Write(new byte[32]);writer.Write(payload);
    }
    private static async Task GuardTestsAsync(ConversionService service,IReadOnlyList<FormatOption> images,string input,string output)
    {
        var settings=new ConvertSettings(images.Single(f=>f.Id=="PNG"),output);
        var one=await service.ConvertAsync(input,settings,null,default);byte[] bytes=File.ReadAllBytes(one.Path);var two=await service.ConvertAsync(input,settings,null,default);
        Assert(one.Path!=two.Path&&bytes.SequenceEqual(File.ReadAllBytes(one.Path)),"existing output never overwritten");
        await Rejected(()=>service.ConvertAsync(input,settings with {Quality=0},null,default),"invalid quality rejected");
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Rejected(()=>service.ConvertAsync(input,settings,null,cancel.Token),"pre-cancelled conversion rejected");
        string corrupt=Path.Combine(Root,"corrupt.png");File.WriteAllText(corrupt,"not an image");await Rejected(()=>service.ConvertAsync(corrupt,settings,null,default),"corrupt image rejected");
        Assert(!Directory.EnumerateDirectories(output,".xfe-convert-*").Any(),"failure and success temporary directories cleaned");
        string svg=Path.Combine(Root,"simple.svg");File.WriteAllText(svg,"<svg xmlns='http://www.w3.org/2000/svg' width='40' height='30'><rect width='40' height='30' fill='red'/></svg>");
        var raster=await service.ConvertAsync(svg,settings,null,default);Assert(File.Exists(raster.Path),"SVG rasterization without external delegates");
        string evil=Path.Combine(Root,"external.svg");File.WriteAllText(evil,"<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='128' height='128'><image xlink:href='"+new Uri(input).AbsoluteUri+"' width='128' height='128'/></svg>");
        await Rejected(()=>service.ConvertAsync(evil,settings,null,default),"SVG external local resource denied by policy");
    }
    private static async Task SetupTestsAsync()
    {
        string zip=Path.Combine(Root,"safe.zip"),directory=Path.Combine(Root,"extract");Directory.CreateDirectory(directory);
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("bin/license.txt").Open());writer.Write("test fixture");}
        EngineSetup.ExtractArchive(zip,directory,default);Assert(File.Exists(Path.Combine(directory,"bin","license.txt")),"environment ZIP extraction");
        string traversal=Path.Combine(Root,"unsafe.zip");using(var archive=ZipFile.Open(traversal,ZipArchiveMode.Create))archive.CreateEntry("../escape.txt");
        await Rejected(()=>Task.Run(()=>EngineSetup.ExtractArchive(traversal,directory,default)),"environment ZIP traversal rejected");
        byte[] data=Enumerable.Range(0,20000).Select(i=>(byte)i).ToArray();using var client=new HttpClient(new DownloadFixture(data));
        var p=new Progress<ConversionProgress>();string download=Path.Combine(Root,"download.zip");
        await EngineSetup.DownloadAsync(client,new Uri("https://fixture.invalid/build.zip"),download,Convert.ToHexString(SHA256.HashData(data)),p,default);
        Assert(File.ReadAllBytes(download).SequenceEqual(data),"environment download verified by SHA-256");
        await Rejected(()=>EngineSetup.DownloadAsync(client,new Uri("https://fixture.invalid/build.zip"),Path.Combine(Root,"bad-download.zip"),new string('0',64),p,default),"checksum mismatch rejected");
    }
    private static async Task ViewTestsAsync(string png)
    {
        var trace=new BindingTrace();PresentationTraceSources.DataBindingSource.Listeners.Add(trace);PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
        var page=new MainPage();var vm=(MainPageViewModel)page.DataContext;await vm.InitializeAsync();vm.AddPaths([png]);vm.OutputDirectory=Path.Combine(Root,"ui-output");
        var window=new Window{Content=page,Width=1180,Height=740,Left=-30000,Top=-30000,ShowInTaskbar=false,WindowStyle=WindowStyle.None};window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);SaveView(page,"converter.png");
        int ticks=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=(_,_)=>ticks++;timer.Start();await vm.RunSelectedAsync();timer.Stop();
        Assert(vm.Items[0].State=="已完成"&&vm.ProgressValue==1&&!vm.IsBusy,"actual ViewModel conversion command completes");
        Assert(ticks>0,"UI dispatcher remains responsive during conversion");
        vm.Items.Clear();vm.AddPaths([Path.Combine(Root,"corrupt.png"),png]);await vm.RunSelectedAsync();
        Assert(vm.Items[0].State=="失败"&&vm.Items[1].State=="已完成","batch continues after a corrupt file");
        vm.IsBusy=true;var closing=new System.ComponentModel.CancelEventArgs();vm.OnClosing(closing);Assert(closing.Cancel,"close waits for current job cleanup");vm.IsBusy=false;
        vm.SelectedTab=2;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert(vm.Formats.Any(f=>f.Id=="original"),"audio tab includes NCM original option");
        window.Width=940;window.Height=650;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);SaveView(page,"converter-minimum.png");
        vm.SelectedTab=4;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);SaveView(page,"converter-environment.png");
        Assert(trace.Errors.Count==0,"WPF binding validation: "+string.Join(" | ",trace.Errors));window.Close();PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
    }
    private static void SaveView(FrameworkElement page,string name){page.UpdateLayout();var bitmap=new RenderTargetBitmap((int)page.ActualWidth,(int)page.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(Root,name));encoder.Save(stream);}
    private static void MakeIcon()
    {
        var visual=new DrawingVisual();using(var d=visual.RenderOpen()){d.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(73,201,228),Color.FromRgb(111,74,213),65),null,new Rect(8,8,112,112),25,25);var pen=new Pen(Brushes.White,8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};d.DrawLine(pen,new Point(31,46),new Point(94,46));d.DrawLine(pen,new Point(94,46),new Point(79,31));d.DrawLine(pen,new Point(94,46),new Point(79,61));d.DrawLine(pen,new Point(97,82),new Point(34,82));d.DrawLine(pen,new Point(34,82),new Point(49,67));d.DrawLine(pen,new Point(34,82),new Point(49,97));}
        var bitmap=new RenderTargetBitmap(128,128,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(Path.Combine(Workspace,"Assets"));using var file=File.Create(Path.Combine(Workspace,"Assets","icon.png"));encoder.Save(file);
    }
    private static async Task HostAsync(string[] args)
    {
        var assembly=typeof(XFEToolBox.Client.Models.LauncherItem).Assembly;var service=assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectRunService",true)!;
        var manifest=JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(Workspace,"manifest.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        async Task Build(string root){var task=(Task)service.GetMethod("BuildAsync",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[root,manifest,CancellationToken.None,null])!;await task.WaitAsync(TimeSpan.FromMinutes(4));var result=task.GetType().GetProperty("Result")!.GetValue(task)!;Assert((bool)result.GetType().GetProperty("Success")!.GetValue(result)!,"production host compilation: "+result.GetType().GetProperty("Message")!.GetValue(result));}
        await Build(Workspace);
        if(args.Contains("--check-package")){string package=Path.Combine(Path.GetDirectoryName(Workspace)!,"Packages",$"{manifest.Id}-{manifest.Version}.xfetool"),extracted=Path.Combine(Root,"package");await(Task<ToolPackageManifest>)service.GetMethod("ExtractAndValidatePackageAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[package,extracted,manifest.Id,manifest.Version,CancellationToken.None])!;var files=Directory.GetFiles(extracted,"*",SearchOption.AllDirectories);Assert(files.Length==Directory.GetFiles(Workspace,"*",SearchOption.AllDirectories).Length&&files.All(f=>File.ReadAllBytes(f).SequenceEqual(File.ReadAllBytes(Path.Combine(Workspace,Path.GetRelativePath(extracted,f))))),"package byte matches workspace");await Build(extracted);}
        if(args.Contains("--register"))await(Task)assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectWorkspaceService",true)!.GetMethod("RememberProjectAsync",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[Workspace])!;
    }
    private sealed class DownloadFixture(byte[] bytes):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
    private sealed class InlineProgress(Action<ConversionProgress> callback):IProgress<ConversionProgress>{public void Report(ConversionProgress value)=>callback(value);}
    private sealed class BindingTrace:TraceListener{public List<string> Errors{get;}=[];public override void Write(string? text){if(!string.IsNullOrEmpty(text))Errors.Add(text);}public override void WriteLine(string? text)=>Write(text);}
}
