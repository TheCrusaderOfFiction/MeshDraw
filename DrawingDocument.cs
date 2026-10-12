using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshDraw;

public enum LayerKind { Artwork, BaseColor, Roughness, Metallic, Emission, Height, Reference }
public sealed class PaintLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Layer";
    public LayerKind Kind { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 1;
    private string? imageData;
    public BitmapSource? Photo { get; private set; }
    public string? ImageData
    {
        get => imageData;
        set
        {
            Photo=DrawingDocument.DecodePhoto(value);imageData=value;
            Ink.Background=Photo==null ? Brushes.Transparent : new ImageBrush(Photo) {Stretch=Stretch.Uniform};
        }
    }
    public void Draw(DrawingContext context,double width,double height)
    {
        if(Photo!=null)
        {
            var scale=Math.Min(width/Photo.PixelWidth,height/Photo.PixelHeight);var w=Photo.PixelWidth*scale;var h=Photo.PixelHeight*scale;
            context.DrawImage(Photo,new Rect((width-w)/2,(height-h)/2,w,h));
        }
        Ink.Strokes.Draw(context);
    }
    public InkCanvas Ink { get; } = new() { Width = 640, Height = 640, Background = Brushes.Transparent };
    public string Description => $"{(Kind==LayerKind.Artwork ? "Outline" : Kind==LayerKind.BaseColor ? "Color" : Kind.ToString())}{(Locked ? " · locked" : "")}{(!Visible ? " · hidden" : "")}";
    public BitmapSource Thumbnail
    {
        get
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 64, 64));
                context.PushTransform(new ScaleTransform(64/Ink.Width, 64/Ink.Height));
                context.PushOpacity(Opacity); Draw(context,Ink.Width,Ink.Height);
            }
            var bitmap = new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
        }
    }
}
public sealed record LayerData(string Id, string Name, LayerKind Kind, bool Visible, bool Locked, double Opacity, string Strokes, string? Image=null);
public sealed record JointData(string Id, double X, double Y, double Z);
public sealed record BoneData(string Id, string Name, string Head, string Tail, string? Parent);
public sealed record RigData(List<JointData> Joints, List<BoneData> Bones);
public sealed record FaceDefinition(string Id, string Name, double Yaw=0, double Pitch=0, double Offset=0, bool IsSection=false, double MoveX=0, double MoveY=0, double MoveZ=0, double Roll=0, string SectionMode="Whole", bool ReverseSection=false);
public sealed record EdgeGuideData(string Id,string Kind,double X1,double Y1,double X2,double Y2,double Radius=8,string Mode="Relative",string CornerStyle="Pointed",string? LinkGroup=null,bool PositionLocked=false,string? CornerGroup=null,string? LockGroup=null,double Depth1=0,double Depth2=0);
public sealed record ConnectionEdgeData(string Id,string FaceA,string GuideA,int EndpointA,string FaceB,string GuideB,int EndpointB);
public sealed record GuideReference(string Face,string Id);
public sealed record LineEqualityData(string Id,GuideReference Whole,GuideReference First,GuideReference Second,string Mode="Sections",double Split=.5,bool ReverseFirst=false,bool ReverseSecond=false);
public sealed record EditorDocument(int Version, int OutputType, Dictionary<string,List<LayerData>> Layers, RigData Skeleton, int CanvasWidth=640, int CanvasHeight=640, string SkeletonType="Custom", List<FaceDefinition>? ExtraFaces=null, Dictionary<string,string>? Selections=null, int VertexLimit=50000, Dictionary<string,List<EdgeGuideData>>? EdgeGuides=null,List<ConnectionEdgeData>? Connections=null,List<LineEqualityData>? LineEqualities=null);

public static class DrawingDocument
{
    public static readonly string[] ViewNames = ["Front", "Back", "Left", "Right", "Top", "Bottom"];
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static BitmapSource? DecodePhoto(string? value)
    {
        if(value==null) return null;
        if(value.Length>16*1024*1024) throw new InvalidDataException("Photo layer is too large.");
        using var stream=new MemoryStream(Convert.FromBase64String(value));
        var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
        if(frame.PixelWidth>2048 || frame.PixelHeight>2048) throw new InvalidDataException("Photo layer dimensions exceed 2048 pixels.");
        frame.Freeze();return frame;
    }
    public static string Encode(StrokeCollection strokes)
    {
        using var stream = new MemoryStream(); strokes.Save(stream); return Convert.ToBase64String(stream.ToArray());
    }
    public static StrokeCollection Decode(string value)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(value)); return new StrokeCollection(stream);
    }
    public static LayerData Data(PaintLayer layer) => new(layer.Id, layer.Name, layer.Kind, layer.Visible, layer.Locked, layer.Opacity, Encode(layer.Ink.Strokes),layer.ImageData);
    public static PaintLayer Layer(LayerData data)
    {
        var result = new PaintLayer { Id=data.Id, Name=data.Name, Kind=data.Kind, Visible=data.Visible, Locked=data.Locked, Opacity=data.Opacity, ImageData=data.Image };
        result.Ink.Strokes.Add(Decode(data.Strokes)); return result;
    }
    public static EditorDocument Read(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var version = parsed.RootElement.GetProperty("Version").GetInt32();
        EditorDocument document;
        if (version == 1)
        {
            var old = JsonSerializer.Deserialize<LegacyProject>(json, JsonOptions) ?? throw new InvalidDataException("Empty project.");
            if (old.Views == null || old.Views.Count != 6) throw new InvalidDataException("Missing drawing views.");
            document = new(2, old.OutputType, ViewNames.ToDictionary(name => name, name => new List<LayerData> {
                new(Guid.NewGuid().ToString("N"), "Original drawing", LayerKind.Artwork, true, false, 1, old.Views[name]) }), new([], []));
        }
        else document = JsonSerializer.Deserialize<EditorDocument>(json, JsonOptions) ?? throw new InvalidDataException("Empty project.");
        Validate(document); return document;
    }
    public static void Validate(EditorDocument document)
    {
        ValidateConnections(document);
        ValidateLineEqualities(document);
        if (document.Version != 2 || document.OutputType is < 0 or > 1 || document.Layers == null || document.Layers.Count != 6+(document.ExtraFaces?.Count ?? 0) || document.Skeleton == null)
            throw new InvalidDataException("Unsupported project format.");
        if(document.CanvasWidth is <128 or >2048 || document.CanvasHeight is <128 or >2048 || string.IsNullOrWhiteSpace(document.SkeletonType) || document.SkeletonType.Length>80)
            throw new InvalidDataException("Canvas dimensions must be 128–2048 pixels.");
        if(document.VertexLimit is <256 or >100000) throw new InvalidDataException("Vertex limit must be 256–100,000.");
        var faceIds=new HashSet<string>(ViewNames);
        if(document.ExtraFaces?.Count>8) throw new InvalidDataException("Keep at most eight extra faces.");
        foreach(var face in document.ExtraFaces ?? [])
            if(face==null || !System.Text.RegularExpressions.Regex.IsMatch(face.Id ?? "", "^custom_[a-f0-9]{32}$") || !faceIds.Add(face.Id!)
                || string.IsNullOrWhiteSpace(face.Name) || face.Name.Length>40 || !double.IsFinite(face.Yaw) || Math.Abs(face.Yaw)>180
                || !double.IsFinite(face.Pitch) || Math.Abs(face.Pitch)>90 || !double.IsFinite(face.Offset) || Math.Abs(face.Offset)>.95 || !double.IsFinite(face.MoveX) || Math.Abs(face.MoveX)>2 || !double.IsFinite(face.MoveY) || Math.Abs(face.MoveY)>2 || !double.IsFinite(face.MoveZ) || Math.Abs(face.MoveZ)>2 || !double.IsFinite(face.Roll) || Math.Abs(face.Roll)>180 || face.SectionMode is not ("Whole" or "Sections" or "Blend"))
                throw new InvalidDataException("Invalid extra face settings.");
        var ids = new HashSet<string>();
        foreach (var name in faceIds)
        {
            if (!document.Layers.TryGetValue(name, out var layers) || layers == null || layers.Count is < 1 or > 32)
                throw new InvalidDataException($"{name} must contain 1–32 layers.");
            foreach (var layer in layers)
            {
                if (layer == null || string.IsNullOrWhiteSpace(layer.Id) || !ids.Add(layer.Id) || string.IsNullOrWhiteSpace(layer.Name) || layer.Name.Length > 80
                    || !Enum.IsDefined(layer.Kind) || !double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1)
                    throw new InvalidDataException("Invalid layer properties.");
                _ = Decode(layer.Strokes);
                _ = DecodePhoto(layer.Image);
            }
        }
        foreach(var selection in document.Selections ?? [])
        {
            if(!faceIds.Contains(selection.Key) || selection.Value==null || selection.Value.Length>262144) throw new InvalidDataException("Invalid selection layer.");
            Geometry geometry;
            try {geometry=Geometry.Parse(selection.Value);} catch(Exception error) {throw new InvalidDataException("Invalid selection shape.",error);}
            var bounds=geometry.Bounds;
            if(!bounds.IsEmpty && (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
                || bounds.Left<-.01 || bounds.Top<-.01 || bounds.Right>document.CanvasWidth+.01 || bounds.Bottom>document.CanvasHeight+.01))
                throw new InvalidDataException("Selection extends outside the canvas.");
        }
        var guideIds=new HashSet<string>();
        foreach(var pair in document.EdgeGuides ?? [])
        {
            if(!faceIds.Contains(pair.Key) || pair.Value==null || pair.Value.Count>128) throw new InvalidDataException("Keep at most 128 edge marks on each face.");
            foreach(var guide in pair.Value)
                if(guide==null || (guide.LockGroup!=null && !System.Text.RegularExpressions.Regex.IsMatch(guide.LockGroup,"^[a-f0-9]{32}$")) || !System.Text.RegularExpressions.Regex.IsMatch(guide.Id ?? "","^[a-f0-9]{32}$") || !guideIds.Add(guide.Id!) || guide.Kind is not ("Straight" or "Corner") || guide.Mode is not ("Relative" or "Absolute") || guide.CornerStyle is not ("Pointed" or "Rounded") || (guide.LinkGroup!=null && !System.Text.RegularExpressions.Regex.IsMatch(guide.LinkGroup,"^[a-f0-9]{32}$")) || (guide.PositionLocked && guide.LinkGroup==null && guide.LockGroup==null) || (guide.CornerGroup!=null && !System.Text.RegularExpressions.Regex.IsMatch(guide.CornerGroup,"^[a-f0-9]{32}$"))
                    || !double.IsFinite(guide.Depth1) || !double.IsFinite(guide.Depth2) || Math.Abs(guide.Depth1)>8 || Math.Abs(guide.Depth2)>8 || !double.IsFinite(guide.X1) || !double.IsFinite(guide.Y1) || !double.IsFinite(guide.X2) || !double.IsFinite(guide.Y2) || !double.IsFinite(guide.Radius)
                    || guide.X1<0 || guide.X1>document.CanvasWidth || guide.X2<0 || guide.X2>document.CanvasWidth || guide.Y1<0 || guide.Y1>document.CanvasHeight || guide.Y2<0 || guide.Y2>document.CanvasHeight || guide.Radius is <2 or >64
                    || (guide.Kind=="Straight" && (new Vector(guide.X2-guide.X1,guide.Y2-guide.Y1)).Length<4))
                    throw new InvalidDataException("Invalid straight edge or corner mark.");
        }
        ValidateRig(document.Skeleton);
    }
    private static void ValidateConnections(EditorDocument document)
    {
        var ids=new HashSet<string>();
        if((document.Connections?.Count ?? 0)>512)throw new InvalidDataException("Too many 3D connection edges.");
        foreach(var c in document.Connections ?? [])
            if(c==null || !System.Text.RegularExpressions.Regex.IsMatch(c.Id ?? "","^[a-f0-9]{32}$") || !ids.Add(c.Id!) || c.EndpointA is not (0 or 1) || c.EndpointB is not (0 or 1) || !document.Layers.ContainsKey(c.FaceA) || !document.Layers.ContainsKey(c.FaceB) || !System.Text.RegularExpressions.Regex.IsMatch(c.GuideA ?? "","^[a-f0-9]{32}$") || !System.Text.RegularExpressions.Regex.IsMatch(c.GuideB ?? "","^[a-f0-9]{32}$"))throw new InvalidDataException("Invalid 3D connection edge.");
    }
    private static void ValidateLineEqualities(EditorDocument document)
    {
        var ids=new HashSet<string>();
        if((document.LineEqualities?.Count ?? 0)>256)throw new InvalidDataException("Too many 1-to-2 relationships.");
        foreach(var r in document.LineEqualities ?? [])
        {
            if(r==null || !System.Text.RegularExpressions.Regex.IsMatch(r.Id ?? "","^[a-f0-9]{32}$") || !ids.Add(r.Id!) || r.Mode!="Sections" || !double.IsFinite(r.Split) || r.Split<=0 || r.Split>=1)throw new InvalidDataException("Invalid 1-to-2 relationship.");
            var refs=new[]{r.Whole,r.First,r.Second};
            if(refs.Any(p=>p==null || !document.Layers.ContainsKey(p.Face) || !System.Text.RegularExpressions.Regex.IsMatch(p.Id ?? "","^[a-f0-9]{32}$")) || refs.Select(p=>p.Id).Distinct().Count()!=3)throw new InvalidDataException("Invalid 1-to-2 line members.");
        }
    }
    public static void ValidateRig(RigData rig)
    {
        if (rig.Joints == null || rig.Bones == null || rig.Joints.Count > 256 || rig.Bones.Count > 128) throw new InvalidDataException("Skeleton is too large.");
        var joints = new Dictionary<string,JointData>();
        foreach (var joint in rig.Joints)
        {
            if (joint == null || string.IsNullOrWhiteSpace(joint.Id) || !joints.TryAdd(joint.Id, joint)
                || new[] {joint.X,joint.Y,joint.Z}.Any(v => !double.IsFinite(v) || v is < 0 or > 1))
                throw new InvalidDataException("Invalid skeleton joint.");
        }
        var bones = new Dictionary<string,BoneData>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {"root"}; var tails = new HashSet<string>();
        foreach (var bone in rig.Bones)
        {
            if (bone == null || string.IsNullOrWhiteSpace(bone.Id) || !bones.TryAdd(bone.Id,bone)
                || string.IsNullOrWhiteSpace(bone.Name) || bone.Name.Length > 64 || !names.Add(bone.Name)
                || !joints.TryGetValue(bone.Head, out var head) || !joints.TryGetValue(bone.Tail, out var tail) || !tails.Add(bone.Tail)
                || Math.Pow(head.X-tail.X,2) + Math.Pow(head.Y-tail.Y,2) + Math.Pow(head.Z-tail.Z,2) < 1e-8)
                throw new InvalidDataException("Bones need unique names, unique end joints, and two distinct joints.");
        }
        foreach (var bone in bones.Values)
        {
            var visited = new HashSet<string> { bone.Id }; var cursor = bone;
            while (cursor.Parent != null)
            {
                if (!bones.TryGetValue(cursor.Parent, out var parent) || parent.Tail != cursor.Head || !visited.Add(parent.Id))
                    throw new InvalidDataException("Invalid bone hierarchy.");
                cursor = parent;
            }
        }
    }
    private sealed record LegacyProject(int Version, int OutputType, Dictionary<string,string> Views);
}
