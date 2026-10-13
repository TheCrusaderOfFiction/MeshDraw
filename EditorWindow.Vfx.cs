using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MeshDraw;
public sealed record VfxSettings(string Preset="Fire",double SpawnRate=80,double Lifetime=1.2,double Size=45,double Speed=120,double Spread=30,string ColorHex="#FF6A19",string Blend="Additive",bool Loop=true,bool UseDrawing=false)
{
    public static VfxSettings ForPreset(string name)=>name switch {
        "Smoke"=>new("Smoke",35,3,80,45,45,"#A1A6B0","Translucent"),
        "Sparks"=>new("Sparks",120,.8,8,450,90,"#FFE8A6",Loop:false),
        "Magic"=>new("Magic",50,2,25,100,80,"#8D5CFF"),_=>new()};
    public static void Validate(VfxSettings value)
    {
        if(value.Preset is not ("Fire" or "Smoke" or "Sparks" or "Magic") || value.Blend is not ("Additive" or "Translucent") || value.ColorHex==null || !Regex.IsMatch(value.ColorHex,"^#[A-Fa-f0-9]{6}$"))throw new InvalidDataException("Choose a VFX preset, blend mode, and a color such as #FF6A19.");
        foreach(var (number,min,max) in new[]{(value.SpawnRate,1d,500d),(value.Lifetime,.1,10),(value.Size,1d,200d),(value.Speed,0d,1000d),(value.Spread,0d,180d)})if(!double.IsFinite(number) || number<min || number>max)throw new InvalidDataException("VFX settings are outside their allowed range.");
    }
}
public partial class EditorWindow
{
    private VfxSettings? vfxSettings;
    private bool syncingVfx,vfxModeActive;
    private GridLength savedVfxBottomHeight;
    private DispatcherTimer? vfxTimer;
    private double vfxTime;
    private BitmapSource? vfxSprite;
    private VfxSettings ReadVfxSettings()=>new(((ComboBoxItem)VfxPreset.SelectedItem).Content.ToString()!,VfxRate.Value,VfxLifetime.Value,VfxSize.Value,VfxSpeed.Value,VfxSpread.Value,Regex.IsMatch(VfxColor.Text.Trim(),"^#[A-Fa-f0-9]{6}$")?VfxColor.Text.Trim():(vfxSettings ?? new()).ColorHex,VfxBlend.SelectedIndex==1?"Translucent":"Additive",VfxLoop.IsChecked==true,VfxUseDrawing.IsChecked==true);
    private void LoadVfxSettings()
    {
        if(VfxPreset==null || VfxRate==null)return;var value=vfxSettings ?? new();syncingVfx=true;
        try {VfxPreset.SelectedIndex=Array.IndexOf(new[]{"Fire","Smoke","Sparks","Magic"},value.Preset);VfxRate.Value=value.SpawnRate;VfxLifetime.Value=value.Lifetime;VfxSize.Value=value.Size;VfxSpeed.Value=value.Speed;VfxSpread.Value=value.Spread;VfxColor.Text=value.ColorHex;VfxBlend.SelectedIndex=value.Blend=="Translucent"?1:0;VfxLoop.IsChecked=value.Loop;VfxUseDrawing.IsChecked=value.UseDrawing;}
        finally {syncingVfx=false;}VfxSpread.IsEnabled=value.Preset!="Sparks";vfxSprite=null;vfxTime=0;
    }
    private void UpdateVfxMode()
    {
        if(VfxSettingsPanel==null || GenerateButton==null)return;
        bool enabled=Output.SelectedIndex==2;
        if(enabled && vfxSettings==null) {vfxSettings=new();LoadVfxSettings();}
        VfxSettingsPanel.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;VertexBudgetSettings.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;
        SkeletonSettingsPanel.Visibility=ExtraFacesSettings.Visibility=BoneTool.Visibility=JointTool.Visibility=OutlinePreviewButton.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;
        GenerateButton.Content=enabled?"Generate Niagara VFX":"Generate mesh";
        if(enabled!=vfxModeActive)
        {
            vfxModeActive=enabled;
            if(enabled){savedVfxBottomHeight=BottomPanelRow.Height;BottomPanelRow.Height=new GridLength(350);if(viewLayout=="3d")SetViewLayout("single");BottomSettingsScroll.ScrollToTop();}
            else BottomPanelRow.Height=savedVfxBottomHeight;
            RefreshBackend();
        }
        vfxTimer ??= CreateVfxTimer();
        if(enabled && VfxPreviewPlaying.IsChecked==true)vfxTimer.Start();else vfxTimer.Stop();
        if(enabled && VfxPreviewImage.Source==null)RenderVfxPreview();
    }
    private DispatcherTimer CreateVfxTimer()
    {
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(33)};
        timer.Tick+=(_,_)=> {if(Output.SelectedIndex==2 && EditorScreen.Visibility==Visibility.Visible){vfxTime+=.033;RenderVfxPreview();}};return timer;
    }
    private void VfxPresetChanged(object sender,SelectionChangedEventArgs e)
    {
        if(syncing || syncingVfx || drawingViews.Count<6 || Output.SelectedIndex!=2)return;
        vfxSettings=VfxSettings.ForPreset(((ComboBoxItem)VfxPreset.SelectedItem).Content.ToString()!);LoadVfxSettings();RecordChange();RenderVfxPreview();
    }
    private void VfxSliderChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(syncing || syncingVfx || drawingViews.Count<6 || Output.SelectedIndex!=2)return;
        vfxSettings=ReadVfxSettings();RenderVfxPreview();
    }
    private void CommitVfxSettings(object sender,RoutedEventArgs e) {if(!syncing && !syncingVfx && Output.SelectedIndex==2) {vfxSettings=ReadVfxSettings();VfxSettings.Validate(vfxSettings);RecordChange();}}
    private void VfxOptionChanged(object sender,RoutedEventArgs e)
    {
        if(syncing || syncingVfx || drawingViews.Count<6 || Output.SelectedIndex!=2)return;
        vfxSettings=ReadVfxSettings();vfxSprite=null;RecordChange();UpdateVfxMode();RenderVfxPreview();
    }
    private void VfxColorCommitted(object sender,RoutedEventArgs e)
    {
        if(syncing || syncingVfx || Output.SelectedIndex!=2)return;
        if(!Regex.IsMatch(VfxColor.Text.Trim(),"^#[A-Fa-f0-9]{6}$")){VfxColor.Text=(vfxSettings ?? new()).ColorHex;Status.Text="Use a VFX color such as #FF6A19.";return;}
        vfxSettings=ReadVfxSettings();vfxSprite=null;RecordChange();RenderVfxPreview();
    }
    private void VfxColorKey(object sender,KeyEventArgs e) {if(e.Key==Key.Enter){VfxColorCommitted(sender,e);e.Handled=true;}}
    private void RestartVfxPreview(object sender,RoutedEventArgs e) {vfxTime=0;RenderVfxPreview();}
    private BitmapSource DrawingVfxSprite()
    {
        var visual=new DrawingVisual();double factor=Math.Min(1,1024d/Math.Max(canvasWidth,canvasHeight));
        using(var context=visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(factor,factor));
            foreach(var layer in drawingViews["Front"].Where(l=>l.Visible && l.Opacity>0 && l.Kind is LayerKind.Artwork or LayerKind.BaseColor).Reverse())
            {
                context.PushOpacity(layer.Opacity);
                if(layer.Photo!=null)context.DrawImage(layer.Photo,new Rect(0,0,canvasWidth,canvasHeight));
                var strokes=layer.Ink.Strokes.Clone();if(layer.Kind==LayerKind.Artwork)foreach(var stroke in strokes){var color=stroke.DrawingAttributes.Color;stroke.DrawingAttributes.Color=Color.FromArgb(color.A,255,255,255);}
                strokes.Draw(context);context.Pop();
            }
        }
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Round(canvasWidth*factor)),Math.Max(1,(int)Math.Round(canvasHeight*factor)),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    private BitmapSource PresetVfxSprite(VfxSettings settings)
    {
        const int size=128;var pixels=new byte[size*size*4];var color=(Color)ColorConverter.ConvertFromString(settings.ColorHex);
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            double xx=2d*x/(size-1)-1,yy=2d*y/(size-1)-1,r=Math.Sqrt(xx*xx+yy*yy);
            double alpha=settings.Preset switch {"Fire"=>Math.Exp(-3*Math.Pow(xx/(.2+.35*(yy+1)/2),2)-3*Math.Pow((yy-.2)/.85,2))*Math.Clamp((1-Math.Abs(yy))*5,0,1),"Smoke"=>Math.Exp(-4*r*r)*(.85+.15*Math.Sin(xx*9+yy*5)*Math.Sin(yy*11)),"Sparks"=>Math.Exp(-45*xx*xx-8*yy*yy),_=>Math.Max(Math.Exp(-80*Math.Pow(r-.45,2))*.5,Math.Exp(-20*r*r))};
            alpha=Math.Clamp(alpha*Math.Clamp((1-r)*8,0,1),0,1);int i=(y*size+x)*4;pixels[i]=(byte)(color.B*alpha);pixels[i+1]=(byte)(color.G*alpha);pixels[i+2]=(byte)(color.R*alpha);pixels[i+3]=(byte)(255*alpha);
        }
        var bitmap=BitmapSource.Create(size,size,96,96,PixelFormats.Pbgra32,null,pixels,size*4);bitmap.Freeze();return bitmap;
    }
    private BitmapSource TintVfxDrawing(BitmapSource bitmap,string hex)
    {
        var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Pbgra32,null,0);int stride=converted.PixelWidth*4;var pixels=new byte[stride*converted.PixelHeight];converted.CopyPixels(pixels,stride,0);var color=(Color)ColorConverter.ConvertFromString(hex);
        for(int i=0;i<pixels.Length;i+=4){pixels[i]=(byte)(pixels[i]*color.B/255);pixels[i+1]=(byte)(pixels[i+1]*color.G/255);pixels[i+2]=(byte)(pixels[i+2]*color.R/255);}
        var result=BitmapSource.Create(converted.PixelWidth,converted.PixelHeight,96,96,PixelFormats.Pbgra32,null,pixels,stride);result.Freeze();return result;
    }
    private void RenderVfxPreview()
    {
        if(VfxPreviewImage==null || Output.SelectedIndex!=2)return;var settings=vfxSettings ?? new();vfxSprite ??=settings.UseDrawing?TintVfxDrawing(DrawingVfxSprite(),settings.ColorHex):PresetVfxSprite(settings);
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(8,12,19)),null,new Rect(0,0,340,230));
            int count=Math.Clamp((int)(settings.SpawnRate*(settings.Preset=="Sparks"?1:settings.Lifetime)),1,400);
            for(int i=0;i<count;i++)
            {
                double seed=(Math.Sin((i+1)*12.9898)*43758.5453)%1;seed-=Math.Floor(seed);
                double age=settings.Preset=="Sparks"?vfxTime:(vfxTime-i/settings.SpawnRate);if(settings.Loop){age%=settings.Lifetime;if(age<0)age+=settings.Lifetime;}if(age<0 || age>settings.Lifetime)continue;
                double life=age/settings.Lifetime,angle=(seed-.5)*settings.Spread*Math.PI/180;
                double speed=settings.Speed*.65,x=170+Math.Sin(angle)*speed*age,y=190-Math.Cos(angle)*speed*age;
                if(settings.Preset=="Sparks"){double a=seed*Math.PI*2;x=170+Math.Cos(a)*speed*age;y=115+Math.Sin(a)*speed*age+180*age*age;}
                double width=Math.Clamp(settings.Size*.65*(settings.Preset=="Smoke"?1+life:1-.4*life),2,150),alpha=Math.Pow(1-life,1.5);
                context.PushOpacity(alpha);context.DrawImage(vfxSprite,new Rect(x-width/2,y-width/2,width,width));context.Pop();
            }
        }
        var bitmap=new RenderTargetBitmap(340,230,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();VfxPreviewImage.Source=bitmap;
    }
    private void WriteVfxInput(string folder)
    {
        var settings=vfxSettings ?? new();VfxSettings.Validate(settings);
        File.WriteAllText(Path.Combine(folder,"project.meshdraw"),Snapshot());File.WriteAllText(Path.Combine(folder,"skeleton.json"),JsonSerializer.Serialize(rig));
        if(settings.UseDrawing){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(DrawingVfxSprite()));using(var file=File.Create(Path.Combine(folder,"vfx_drawing.png")))png.Save(file);}
    }
}
