using System.IO;
using System.Windows;
using System.Windows.Ink;

namespace MeshDraw;
public partial class EditorWindow
{
    private async Task CheckFilters(string folder)
    {
        static void Check(bool condition,string message) {if(!condition) throw new Exception(message);}
        var before=Document();var oldUndo=undo.ToList();var oldRedo=redo.ToArray();
        try
        {
            var source=new byte[128*128*4];
            for(int y=20;y<108;y++)for(int x=20;x<108;x++) {var i=(y*128+x)*4;source[i]=30;source[i+1]=50;source[i+2]=100;source[i+3]=128;}
            var image=EncodeRaster(source,128,128);
            var empty=DrawingDocument.Encode(new StrokeCollection());
            var layers=DrawingDocument.ViewNames.ToDictionary(face=>face,face=>new List<LayerData> {
                new(Guid.NewGuid().ToString("N"),"Outline",LayerKind.Artwork,true,false,1,empty),
                new(Guid.NewGuid().ToString("N"),"Color",LayerKind.BaseColor,true,false,1,empty,image)});
            ApplyDocument(new EditorDocument(2,0,layers,new([],[]),128,128,"Custom"));
            Layers.SelectedItem=drawingViews[ActiveView][1];
            SetSelection(new System.Windows.Media.RectangleGeometry(new Rect(40,40,40,40)));
            undo.Clear();undo.Add(Snapshot());redo.Clear();var initial=Snapshot();
            var original=LayerPixels(new[]{ActiveLayer!});var catalog=FilterEngine.Catalog(projectRoot);
            var result=await FilterEngine.Run(projectRoot,original,128,128,catalog.First(f=>f.Key=="invert"),new(),CancellationToken.None);
            FilterEngine.BlendSelection(original,result,SelectionMask(ActiveView),1);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                var i=(y*128+x)*4;if(x<40 || x>=80 || y<40 || y>=80)
                    for(int c=0;c<4;c++)Check(result[i+c]==original[i+c],"Filter changed pixels outside selection");
                Check(result[i]<=result[i+3] && result[i+1]<=result[i+3] && result[i+2]<=result[i+3],"Invalid premultiplied filter alpha");
            }
            Check(result[(60*128+60)*4]!=original[(60*128+60)*4],"Filter failed inside selection");
            CommitRaster(ActiveLayer!,result);Check(undo.Count==2,"Filter did not make one undo step");var applied=Snapshot();
            var saved=Path.Combine(folder,"filtered.meshdraw");File.WriteAllText(saved,applied);DrawingDocument.Validate(DrawingDocument.Read(applied));
            Undo(this,new RoutedEventArgs());Check(Snapshot()==initial,"Filter undo failed");Redo(this,new RoutedEventArgs());Check(Snapshot()==applied,"Filter redo failed");
            var unchanged=(byte[])result.Clone();FilterEngine.BlendSelection(original,unchanged,null,0);Check(unchanged.SequenceEqual(original),"Zero opacity changed drawing");
            var emptyMask=new bool[128*128];unchanged=(byte[])result.Clone();FilterEngine.BlendSelection(original,unchanged,emptyMask,1);Check(unchanged.SequenceEqual(original),"Empty selection changed drawing");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();bool cancelled=false;
                try {await FilterEngine.Run(projectRoot,original,128,128,catalog[0],new(),cancellation.Token);}catch(OperationCanceledException) {cancelled=true;}
                Check(cancelled,"Filter cancellation ignored");
            }
            var dialog=new FilterWindow(projectRoot,original,128,128,SelectionMask(ActiveView),"Front · Color");
            await dialog.CheckBrowser(folder);Check(Snapshot()==applied,"Preview or closing browser changed drawing");
        }
        finally
        {
            ApplyDocument(before);undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);
        }
    }
}
