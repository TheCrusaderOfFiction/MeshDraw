# MeshDraw
Draw 3D meshes for Blender and Unreal

### Move and rotate custom faces in 3D

In **3D view**, choose a custom face in the bottom **Custom face** dropdown (or click its plane with Move/Rotate selected). Choose **Move face** or **Rotate face** and drag in the preview. The axis dropdown chooses movement on X/Y/Z or rotation in yaw/pitch/roll; Screen uses the camera plane for movement and yaw/pitch for rotation. Numeric position and angle fields allow exact changes with **Apply**. Positions are relative to the original view cube (−2 to +2); **Reset face** restores zero translation/rotation and centers a slice. Escape cancels an active drag, and Undo restores a committed change. The original six faces remain fixed. Custom transforms persist in the drawing and are used for generation, texture projection, and edge marks.

### Independent interior sections

Select an interior face in **Extra faces · angles and slices**, choose **Separate sections**, and press **Apply to selected**. The original opposing outlines constrain the region before that plane. After crossing the plane, its outline replaces that pair of projections until the next interior plane or opposite side. Other view directions still constrain the object. This allows a sword hilt and blade to use different side profiles, including a blade that is wider than the hilt within the overall drawing bounds. Rotate and move the plane in 3D to position the boundary; **Reverse which side this face controls** changes the direction. Multiple planes create consecutive sections along their dominant view direction.

**Smooth transition** interpolates profile distances toward the interior plane. **Legacy slice constraint** keeps the previous behavior for older drawings. The rough 3D overlap supports both new modes; saved projects and generated asset bundles retain their settings.

For sectioned objects, partial front/back/side profiles retain their drawn vertical alignment against the full-length outline. A hilt-only side drawing is no longer re-centered into the blade. Custom profile coordinates, texture painting, and edge marks use that same full-object vertical anchor.

### Round outlines

Surface generation uses 480 alternating non-shrinking smoothing passes, with vertex movement capped at 0.75 of a reconstruction voxel. Creases over 35 degrees retain their vertices and export with split shading normals; edge marks restore a broader crisp band. This reduces fine surface ripples while retaining the existing thin-feature and edge-guide protections. Regenerate a drawing to apply the updated smoothing.

Generation conservatively recognizes nearly circular closed outline components, fits a circle to remove small drawing/raster irregularities, and corrects nearby curved surface vertices to a true circular radius. Clearly elliptical and angular shapes retain their outlines. Faces with explicit edge/corner marks are excluded from automatic circle fitting. Matching opposite circles construct an analytic capped cylinder over the existing local span before triangulation. Cylinder walls are restored to the fitted radius after vertex reduction, with face-flip checks protecting junctions. Detected circular walls also export analytic radial shading normals after mesh simplification, reducing visible facets while keeping caps and angular junctions separate. Round-profile detection is automatic and runs locally; regenerate existing drawings to apply it. Low vertex limits still reduce how smooth a curve can appear.

### Gentle edge and corner guidance

Edge marks now adjust the outline used for generation before building the volume. Straight marks fit their offset to the nearby painted boundary, gently reduce waviness, and fade at their ends without changing adjacent edges. Corner marks near two straight guides follow their fitted intersection. Generation preserves this corrected outline detail instead of cutting the 3D surface with a hard plane or collapsing vertices toward an inset corner marker. The editable painted strokes remain intact. This prevents marks placed slightly inside a drawing from introducing dents.

### Relative and absolute edge marks

In the **Edges** bottom controls, **Relative** follows the painted outline with gentle fitting (orange), and **Absolute** uses the original exact straightening/corner behavior (green). The mode applies to new lines and corners. To change an existing mark, choose **Change mode · tap mark**, choose Relative or Absolute, then tap that mark. **Apply mode to all marks on this face** changes them together. Modes save with each mark, support Undo and face copying, and are mixed independently during generation. Older marks without a stored mode load as Relative. Absolute marks use their exact location and can cut inward when placed inside the outline.

In 3D preview, drawing tools and drawing aids are hidden and the bottom panel shows preview quality, camera, custom-face transforms, and status. Return to Single view or All views to restore drawing controls and the previous tool panel width. The bottom panel remains resizable in both modes.

Matching circular outlines in opposite views now protect a local round cross-section across its existing object span. Rectangular views no longer flatten that circle into a band. This gives consistent opposite circles priority locally when views disagree. Round-surface correction freezes unsafe junction vertices instead of discarding every valid correction.

Opposite interior section profiles with nearly identical mirrored silhouettes are registered automatically when their canvas offsets are small. Generation corrects the mirror and pixel-center offset; saved drawings and explicit plane transforms remain unchanged. Extra-face texture projection uses the same registration.

Straight edge marks that agree on a plane in independent orthographic views flatten the supported surface to that plane. Analytic cylinder caps are flattened too. Export dissolves redundant coplanar topology before vertex budgeting while retaining sharp boundaries and curved geometry.

Relative straight marks reconstruct fitted analytic planes rather than restoring raw voxel steps. Closely matching registered inside profiles share one exact boundary to avoid a region seam. Relative corner marks retain the fitted tip through a connectivity-preserving fallback when collapsing the cap would damage topology.

Straight-plane distance-field corrections are limited to a narrow boundary band and cannot create occupied samples outside the constrained drawing hull. A generation regression checks for unsupported volume and disconnected sheets.

In the Edges tool, enable Connect nearby line endpoints to snap line ends together and automatically mark their shared corner. Choose Pointed or Rounded for new joints; use Change corner shape and tap a corner to switch it, or apply the style to every corner on a face. Rounded corners retain curved reconstruction and skip pointed-tip pinning.

In 3D, select Connect edge lines and click one marked line on each face. Cyan connectors show the pair. Enable Lock connected lines on parallel faces before connecting to align their endpoints in world space; locked lines appear green. Move straight line in the Edges tool updates either partner. Locked face moves preserve alignment, and rotations or moves that break the parallel lock are blocked. Select a connected line in 3D and clear its connection to unlock it. Links save with the drawing.

Choose Merge corners in 3D and click corner marks on different faces. Select an existing merged corner and another corner to extend the group across more faces, including custom planes. The shared point is inferred from face projections, connected line endpoints follow it, and groups persist with the drawing. Clear selected connection unmerges the group. Pointed merged corners guide a shared generated vertex within the existing corner influence; Rounded merged corners retain the curved surface.

In 3D view, use the separate **Select** dropdown to choose **Edges** or **Corners**, click marks to highlight/unhighlight them, and use **Merge**, **Connect**, or **Lock positions**. Locks align parallel edges and move both partners without adding bridges. Joined corners stay attached as edges move; neighboring edges stretch and turn. Use the edge tool’s **Move corner** to move merged corners across faces. **Dissolve joint / unlock** explicitly releases selected relationships.

Merge markers are dashed blue indicators that annotations describe the same point; they are not edges. Connect creates a solid orange 3D edge between the nearest selected endpoints without moving or merging them. These edges follow their endpoints and persist in saved drawings. Select them using Edges or Connections; select merge indicators separately with Merge markers. Selection is pink in either case.

Drawing canvas navigation: scroll to zoom around the pointer (10%–2000%); hold Space and drag to pan; hold Space+R and drag to rotate. Reset canvas view restores the active face. Navigation transforms the view only, preserving drawing coordinates, saves, and exports. Works in single and all-view drawing layouts.

To match one face edge to two others in 3D: highlight the single edge and click Set single line, then highlight its two partner edges and click 1 line = 2 lines. The pair is aligned to form the reference edge when viewed from that edge’s face, while retaining each partner’s face and depth. This also supports angled faces whose projection can match the span. The split follows the pair’s original projected lengths. Moving any member maintains the projected match. Select a member and click Clear 1-to-2 match to release it. Relationships save with drawings and support undo.

3D editing: Unlock is separate from Dissolve. Choose Lock lines to highlight red dotted position-lock or 1-to-2 markers; Unlock or Delete selected releases these relationships and keeps their geometry. Delete selected also deletes highlighted face edges, corners, 3D connection edges, or merge relationships according to the selection mode. Dangling relationships are cleaned up, and each operation supports undo. Dissolve releases joints/connections without releasing position locks.

When matching front edges to a perpendicular inside-face reference, the reference remains where drawn. Partner edges receive saved 3D depth offsets so their projection matches the inside reference without shifting its line or changing their 2D sideways positions. Connected endpoints follow that depth, and guide export retains the depth data.

Merged corners now share an exact 3D point. Their position combines the coordinates each face supplies; per-face depth offsets make every member coincide. Edge merges join both corresponding endpoints, including reversed opposite-face lines, and movement keeps attached endpoints together. Coincident merges have small blue selectable markers.

Reference edge merge: highlight one edge and Set reference edge. Highlight any number of partner edges, then Merge. Partners adopt the reference’s depth and slope on its face axes, combining their other-axis coordinates; the construction reference is then removed. Projected partner spans can overlap or form consecutive sections. The entire operation, including reference deletion, is undoable. Merged corners render/select as one logical 3D point with per-face drawing representations.

Show visible edges from other faces projects the 3D guide/connection edges into every drawing face as blue dashed overlays. Closed planar edge loops, including loops built across faces using connections, are treated as opaque surfaces. Only edge portions in front of these surfaces remain visible. Partial hiding uses geometric intersection intervals, and cached overlays avoid rebuilding visibility during ordinary brush movement. The checkbox hides these overlays; they are viewing aids and do not alter saved paint or textures.

Mesh export now welds small connected pointed corner caps into one geometric vertex and dissolves redundant collinear vertices along marked straight ridges, retaining end points and marked junctions. Cleanup runs before UVs and skinning, checks closed topology and volume, and preserves Rounded corners.

Merged pointed corner groups near a shape’s end now rebuild the tip cap as a closed triangle fan around one shared apex. Marked straight strips receive additional local planar simplification, so small reconstruction bends do not prevent removing subdivisions. Existing tiny triangles outside the edited region no longer reject every valid cleanup; new topology defects and large volume changes remain blocked.

Straight-edge cleanup now includes sharp-ridge vertices whose normals are averaged between adjoining faces. It uses the full marked influence radius and removes high-valence ridge subdivisions by rebuilding each adjacent face separately, retaining the shared sharp edge. Export diagnostics include removal coverage along each marked span.


Hard silhouette corners also simplify through depth: their tiny cap subdivisions collapse onto a shared ridge while retaining the front and back depth endpoints. Straight cleanup handles shallow blade bevels and checks independent edge sections when a junction rejects a larger change. Rounded corner marks remain protected.

Straight-edge triangle strips are contracted after final vertex budgeting, before UV rebaking and rig weights. This removes near-collinear strip vertices that simple face dissolving leaves behind, with endpoint, hard-corner, closed-volume, and analytic cylinder protection. The final strip cleanup is included in the generation report.

Analytic cylinders are protected during straight-plane corrections, corner welding, and final strip cleanup, including cap interiors and adjacent junction vertices. Existing split normals on circular faces are restored after UV rebuilding so blade simplification does not create longitudinal cylinder shading seams.
