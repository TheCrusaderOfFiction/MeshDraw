using System.Windows;
namespace MeshDraw;
public partial class EditorWindow
{
    private static bool IsLockMarker(string key)=>key.StartsWith("lock|") || key.StartsWith("equality|");
    private bool SelectionAllowsConnection(string key)=>PreviewSelectionMode.SelectedIndex switch
    {
        3=>key.StartsWith("merge|"),
        4=>IsLockMarker(key),
        0 or 2=>!key.StartsWith("merge|") && !IsLockMarker(key),
        _=>false
    };
    private void ReleaseSelectedLocks(bool includeGuides)
    {
        var members=includeGuides?highlightedGuides.Select(p=>p.id).ToHashSet():new HashSet<string>();
        var equalities=new HashSet<string>();
        foreach(var key in highlightedConnections.Where(IsLockMarker))
        {
            var parts=key.Split('|');if(parts.Length!=3)continue;
            if(parts[0]=="lock")
            {
                if(edgeGuides.Values.SelectMany(g=>g).Any(g=>g.Id==parts[2] && g.PositionLocked && (g.LockGroup ?? g.LinkGroup)==parts[1]))members.Add(parts[2]);
            }
            else equalities.Add(parts[1]);
        }
        foreach(var list in edgeGuides.Values)for(int i=0;i<list.Count;i++)
        {
            var g=list[i];if(members.Contains(g.Id) && g.PositionLocked)list[i]=g with {PositionLocked=false,LockGroup=null,LinkGroup=g.LockGroup==null?null:g.LinkGroup};
        }
        lineEqualities.RemoveAll(r=>equalities.Contains(r.Id) || (includeGuides && new[]{r.Whole.Id,r.First.Id,r.Second.Id}.Any(members.Contains)));
        CleanupPreviewRelationships();
    }
    private void CleanupPreviewRelationships()
    {
        connectionEdges.RemoveAll(c=>!ConnectionExists(c));lineEqualities.RemoveAll(r=>!EqualityExists(r));
        var lockSingles=edgeGuides.Values.SelectMany(g=>g).Where(g=>g.PositionLocked).GroupBy(g=>g.LockGroup ?? g.LinkGroup).Where(g=>g.Count()<2).Select(g=>g.Key).ToHashSet();
        var mergeSingles=edgeGuides.Values.SelectMany(g=>g).Where(g=>g.CornerGroup!=null).GroupBy(g=>g.CornerGroup).Where(g=>g.Count()<2).Select(g=>g.Key).ToHashSet();
        var linkSingles=edgeGuides.Values.SelectMany(g=>g).Where(g=>g.LinkGroup!=null && !g.PositionLocked).GroupBy(g=>g.LinkGroup).Where(g=>g.Count()<2).Select(g=>g.Key).ToHashSet();
        foreach(var list in edgeGuides.Values)for(int i=0;i<list.Count;i++)
        {
            var g=list[i];if(g.PositionLocked && lockSingles.Contains(g.LockGroup ?? g.LinkGroup))g=g with {PositionLocked=false,LockGroup=null,LinkGroup=g.LockGroup==null?null:g.LinkGroup};
            if(g.CornerGroup!=null && mergeSingles.Contains(g.CornerGroup))g=g with {CornerGroup=null};
            if(!g.PositionLocked && g.LinkGroup!=null && linkSingles.Contains(g.LinkGroup))g=g with {LinkGroup=null};list[i]=g;
        }
    }
    private void UnlockSelectedGuides(object sender,RoutedEventArgs e)
    {
        var before=Snapshot();ReleaseSelectedLocks(true);
        if(Snapshot()==before){PreviewLineSelection.Text="Select locked edges or red dotted lock lines to unlock.";return;}
        highlightedConnections.RemoveWhere(IsLockMarker);RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text="Unlocked selected relationships. Edges, corners, and connections preserved.";
    }
    private void DeleteSelectedPreviewItems(object sender,RoutedEventArgs e)
    {
        var before=Snapshot();ReleaseSelectedLocks(false);
        foreach(var key in highlightedConnections.Where(k=>k.StartsWith("merge|")))
        {
            var parts=key.Split('|');if(parts.Length!=3)continue;
            foreach(var list in edgeGuides.Values)for(int i=0;i<list.Count;i++)if(list[i].Id==parts[2] && list[i].CornerGroup==parts[1])list[i]=list[i] with {CornerGroup=null};
        }
        connectionEdges.RemoveAll(c=>highlightedConnections.Contains(c.Id));
        var legacyGroups=previewConnectionModels.Values.Where(c=>highlightedConnections.Contains(c.key) && !c.key.StartsWith("merge|") && !IsLockMarker(c.key) && c.key.Contains('|')).Select(c=>c.group).ToHashSet();
        foreach(var pair in edgeGuides)
        {
            pair.Value.RemoveAll(g=>highlightedGuides.Contains((pair.Key,g.Id)));
            for(int i=0;i<pair.Value.Count;i++)if(pair.Value[i].LinkGroup!=null && !pair.Value[i].PositionLocked && legacyGroups.Contains(pair.Value[i].LinkGroup!))pair.Value[i]=pair.Value[i] with {LinkGroup=null};
        }
        CleanupPreviewRelationships();
        if(Snapshot()==before){PreviewLineSelection.Text="Highlight items first, then click Delete selected.";return;}
        highlightedGuides.Clear();highlightedConnections.Clear();selectedPreviewLine=null;
        RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text="Deleted selected items. Undo restores them.";
    }
}
