# MeshDraw
Draw 3D meshes for Blender and Unreal

### Move and rotate custom faces in 3D

In **3D view**, choose a custom face in the bottom **Custom face** dropdown (or click its plane with Move/Rotate selected). Choose **Move face** or **Rotate face** and drag in the preview. The axis dropdown chooses movement on X/Y/Z or rotation in yaw/pitch/roll; Screen uses the camera plane for movement and yaw/pitch for rotation. Numeric position and angle fields allow exact changes with **Apply**. Positions are relative to the original view cube (−2 to +2); **Reset face** restores zero translation/rotation and centers a slice. Escape cancels an active drag, and Undo restores a committed change. The original six faces remain fixed. Custom transforms persist in the drawing and are used for generation, texture projection, and edge marks.

### Independent interior sections

Select an interior face in **Extra faces · angles and slices**, choose **Separate sections**, and press **Apply to selected**. The original opposing outlines constrain the region before that plane. After crossing the plane, its outline replaces that pair of projections until the next interior plane or opposite side. Other view directions still constrain the object. This allows a sword hilt and blade to use different side profiles, including a blade that is wider than the hilt within the overall drawing bounds. Rotate and move the plane in 3D to position the boundary; **Reverse which side this face controls** changes the direction. Multiple planes create consecutive sections along their dominant view direction.

**Smooth transition** interpolates profile distances toward the interior plane. **Legacy slice constraint** keeps the previous behavior for older drawings. The rough 3D overlap supports both new modes; saved projects and generated asset bundles retain their settings.

For sectioned objects, partial front/back/side profiles retain their drawn vertical alignment against the full-length outline. A hilt-only side drawing is no longer re-centered into the blade. Custom profile coordinates, texture painting, and edge marks use that same full-object vertical anchor.

### Round outlines

Generation conservatively recognizes nearly circular closed outline components, fits a circle to remove small drawing/raster irregularities, and corrects nearby curved surface vertices to a true circular radius. Clearly elliptical and angular shapes retain their outlines. Faces with explicit edge/corner marks are excluded from automatic circle fitting. Round-profile detection is automatic and runs locally; regenerate existing drawings to apply it. Low vertex limits still reduce how smooth a curve can appear.
