using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MeshDraw;

public partial class FilterWindow : Window
{
    private readonly string root;
    private readonly byte[] original,previewOriginal;
    private readonly int width,height,pw,ph;
    private readonly bool[]? selection,previewSelection;
    private readonly List<FilterDefinition> catalog;
    private readonly Dictionary<string,Dictionary<string,object>> settings=new();
    private readonly DispatcherTimer debounce=new() {Interval=TimeSpan.FromMilliseconds(400)};
    private CancellationTokenSource? running;
    private bool initialized,building,applying,closed;
    private int revision;
    public byte[]? Result {get;private set;}
    public string AppliedName {get;private set;}="";
    public FilterWindow(string root,byte[] pixels,int width,int height,bool[]? mask,string target)
    {
        InitializeComponent();this.root=root;original=pixels;this.width=width;this.height=height;selection=mask;
        catalog=FilterEngine.Catalog(root);
        var scale=Math.Min(1,512.0/Math.Max(width,height));pw=Math.Max(1,(int)Math.Round(width*scale));ph=Math.Max(1,(int)Math.Round(height*scale));
        var source=FilterEngine.Bitmap(original,width,height);
        var resized=new TransformedBitmap(source,new ScaleTransform((double)pw/width,(double)ph/height));
        previewOriginal=new byte[pw*ph*4];resized.CopyPixels(previewOriginal,pw*4,0);
        if(mask!=null)
        {
            previewSelection=new bool[pw*ph];
            for(int y=0;y<ph;y++) for(int x=0;x<pw;x++) previewSelection[y*pw+x]=mask[Math.Min(height-1,(int)((y+.5)*height/ph))*width+Math.Min(width-1,(int)((x+.5)*width/pw))];
        }
        Before.Source=FilterEngine.Bitmap(previewOriginal,pw,ph);After.Source=Before.Source;
        TargetLabel.Text=target+" · "+width+" × "+height+(mask!=null?" · Selection limits the effect":" · Entire layer");
        Category.ItemsSource=new[]{"All categories"}.Concat(catalog.Select(f=>f.Category).Distinct()).ToList();Category.SelectedIndex=0;
        debounce.Tick+=(_,_)=> {debounce.Stop();_ = Preview();};
        initialized=true;RefreshList();Closed+=(_,_)=> {closed=true;debounce.Stop();running?.Cancel();};
    }
    private void RefreshList()
    {
        if(!initialized) return;
        var key=(FilterList.SelectedItem as FilterDefinition)?.Key;
        var category=Category.SelectedItem as string;var text=Search.Text.Trim();
        var list=catalog.Where(f=>(Category.SelectedIndex==0 || f.Category==category) && (f.Name.Contains(text,StringComparison.OrdinalIgnoreCase)||f.Category.Contains(text,StringComparison.OrdinalIgnoreCase))).ToList();
        FilterList.ItemsSource=list;FilterList.SelectedItem=list.FirstOrDefault(f=>f.Key==key) ?? list.FirstOrDefault();
    }
    private void SearchChanged(object sender,TextChangedEventArgs e)=>RefreshList();
    private void CategoryChanged(object sender,SelectionChangedEventArgs e)=>RefreshList();
    private Dictionary<string,object> Values(FilterDefinition filter)
    {
        if(!settings.TryGetValue(filter.Key,out var values))
            settings[filter.Key]=values=filter.Controls.ToDictionary(c=>c.Key,c=>c.Type=="number"?(object)c.Value:c.Text);
        return values;
    }
    private void FilterChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized || applying) return;
        Controls.Children.Clear();
        if(FilterList.SelectedItem is not FilterDefinition filter) {debounce.Stop();running?.Cancel();revision++;After.Source=Before.Source;ApplyButton.IsEnabled=false;FilterTitle.Text="No matching filters";Loading.Visibility=Visibility.Collapsed;Message.Text="Try another search or category.";Note.Text="";return;}
        building=true;
        try
        {
            FilterTitle.Text=filter.Name;Note.Text=filter.Note;var values=Values(filter);
            foreach(var control in filter.Controls)
            {
                Controls.Children.Add(new TextBlock {Text=control.Label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,0)});
                var box=new TextBox {Text=Convert.ToString(values[control.Key],CultureInfo.InvariantCulture)};
                if(control.Type=="number")
                {
                    var slider=new Slider {Minimum=control.Min,Maximum=control.Max,Value=Convert.ToDouble(values[control.Key]),TickFrequency=control.Step,IsSnapToTickEnabled=true,Margin=new Thickness(0,6,0,0)};
                    slider.ValueChanged+=(_,_)=> {values[control.Key]=slider.Value;box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);QueuePreview();};
                    void Commit()
                    {
                        if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n) && double.IsFinite(n)) slider.Value=Math.Clamp(n,control.Min,control.Max);
                        box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);
                    }
                    box.LostKeyboardFocus+=(_,_)=>Commit();box.KeyDown+=(_,eventArgs)=> {if(eventArgs.Key==Key.Enter) Commit();};
                    Controls.Children.Add(slider);
                }
                else box.TextChanged+=(_,_)=> {values[control.Key]=box.Text ?? "";QueuePreview();};
                Controls.Children.Add(box);
            }
        }
        finally {building=false;}
        QueuePreview();
    }
    private void QueuePreview()
    {
        if(!initialized || building || applying || closed) return;
        revision++;running?.Cancel();ApplyButton.IsEnabled=false;Message.Text="Preparing preview…";debounce.Stop();debounce.Start();
    }
    private void AmountChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>QueuePreview();
    private Dictionary<string,object> ValidatedValues(FilterDefinition filter)
    {
        var values=new Dictionary<string,object>(Values(filter));
        foreach(var c in filter.Controls.Where(c=>c.Type=="color"))
        {
            var text=Convert.ToString(values[c.Key]) ?? "";
            if(text.Length!=7 || text[0]!='#' || !int.TryParse(text[1..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _)) throw new InvalidOperationException("Enter a color as #RRGGBB.");
        }
        return values;
    }
    private async Task Preview()
    {
        if(FilterList.SelectedItem is not FilterDefinition filter || closed || applying) return;
        var version=revision;var tokenSource=new CancellationTokenSource();running=tokenSource;Loading.Visibility=Visibility.Visible;
        try
        {
            var values=ValidatedValues(filter);var amount=Amount.Value;
            var result=await FilterEngine.Run(root,previewOriginal,pw,ph,filter,values,tokenSource.Token,(double)pw/width);
            FilterEngine.BlendSelection(previewOriginal,result,previewSelection,amount);
            if(closed || version!=revision) return;
            After.Source=FilterEngine.Bitmap(result,pw,ph);ApplyButton.IsEnabled=true;Message.Text="Preview ready · Apply processes your full canvas.";
        }
        catch(OperationCanceledException) {}
        catch(Exception error) {if(!closed && version==revision) {Message.Text=error.Message;ApplyButton.IsEnabled=false;}}
        finally {if(!closed && version==revision) Loading.Visibility=Visibility.Collapsed;if(ReferenceEquals(running,tokenSource))running=null;tokenSource.Dispose();}
    }
    private async void Apply(object sender,RoutedEventArgs e)
    {
        if(applying || FilterList.SelectedItem is not FilterDefinition filter) return;
        Dictionary<string,object> values;
        try {values=ValidatedValues(filter);} catch(Exception error) {Message.Text=error.Message;return;}
        applying=true;revision++;debounce.Stop();running?.Cancel();
        FilterList.IsEnabled=false;Controls.IsEnabled=false;Category.IsEnabled=false;Search.IsEnabled=false;Amount.IsEnabled=false;ApplyButton.IsEnabled=false;
        var tokenSource=new CancellationTokenSource();running=tokenSource;Loading.Visibility=Visibility.Visible;Message.Text="Applying to full canvas… Cancel leaves your drawing unchanged.";
        try
        {
            var amount=Amount.Value;var result=await FilterEngine.Run(root,original,width,height,filter,values,tokenSource.Token);
            FilterEngine.BlendSelection(original,result,selection,amount);
            if(closed)return;Result=result;AppliedName=filter.Name;DialogResult=true;
        }
        catch(OperationCanceledException) {}
        catch(Exception error) {if(!closed) Message.Text=error.Message;}
        finally
        {
            tokenSource.Dispose();if(ReferenceEquals(running,tokenSource))running=null;
            if(!closed) {applying=false;Loading.Visibility=Visibility.Collapsed;FilterList.IsEnabled=true;Controls.IsEnabled=true;Category.IsEnabled=true;Search.IsEnabled=true;Amount.IsEnabled=true;ApplyButton.IsEnabled=true;}
        }
    }
    private void Cancel(object sender,RoutedEventArgs e) {running?.Cancel();DialogResult=false;}
    private void ResetSettings(object sender,RoutedEventArgs e)
    {
        if(applying || FilterList.SelectedItem is not FilterDefinition filter) return;
        settings.Remove(filter.Key);Amount.Value=1;FilterChanged(this,new SelectionChangedEventArgs(SelectorSelectionChanged(),Array.Empty<object>(),Array.Empty<object>()));
    }
    private static RoutedEvent SelectorSelectionChanged()=>System.Windows.Controls.Primitives.Selector.SelectionChangedEvent;

    internal async Task CheckBrowser(string folder)
    {
        static void Check(bool condition,string message) {if(!condition) throw new Exception(message);}
        Check(FilterList.Items.Count==78,"Filter catalog missing items");
        Search.Text="twirl";Check(FilterList.Items.Count==1 && (FilterList.SelectedItem as FilterDefinition)?.Key=="twirl","Filter search failed");
        debounce.Stop();await Preview();Check(ApplyButton.IsEnabled && After.Source!=null,"Filter preview did not finish");
        Search.Text="unmatched filter xyz";Check(FilterList.Items.Count==0 && !ApplyButton.IsEnabled,"Empty search permits apply");
        Search.Text="";Category.SelectedItem="Style";Check(FilterList.Items.Count==16,"Category filtering failed");
        FilterList.SelectedItem=catalog.First(f=>f.Key=="outer_glow");Amount.Value=.6;debounce.Stop();await Preview();
        Check(ApplyButton.IsEnabled && Controls.Children.Count>0,"Filter settings or preview failed");
        var colorBox=Controls.Children.OfType<TextBox>().Last();colorBox.Text="bad color";debounce.Stop();await Preview();
        Check(!ApplyButton.IsEnabled && Message.Text.Contains("#RRGGBB"),"Invalid color not rejected");
        ResetSettings(this,new RoutedEventArgs());debounce.Stop();await Preview();Check(ApplyButton.IsEnabled && Amount.Value==1,"Reset settings failed");
        var content=(FrameworkElement)Content;content.Measure(new Size(1040,680));content.Arrange(new Rect(0,0,1040,680));content.UpdateLayout();
        var screenshot=new RenderTargetBitmap(1040,680,96,96,PixelFormats.Pbgra32);screenshot.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(screenshot));
        using(var file=System.IO.File.Create(System.IO.Path.Combine(folder,"filter-browser.png")))encoder.Save(file);
        Check(Result==null,"Preview changed the final drawing result");debounce.Stop();running?.Cancel();Close();
    }
}
