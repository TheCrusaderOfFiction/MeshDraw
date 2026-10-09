# MeshDraw for Blender

Tested in Blender 5.1.1 on Windows. The extension targets Blender 4.2 and newer, but other versions have not been tested.

## Install

1. In Blender, open **Edit > Preferences > Add-ons**.
2. Open the menu at the top right and choose **Install from Disk**.
3. Select **MeshDraw-Blender-1.0.0.zip** and enable **MeshDraw Import Export**.

Blender installation reference: https://docs.blender.org/manual/en/latest/editors/preferences/addons.html

No internet, model download, Python packages, or additional software is required by the add-on.

## Import, edit, export

1. In MeshDraw, generate your static or skeletal mesh and use **Export Unreal file** to create a `.meshdrawasset` file.
2. In Blender, choose **File > Import > MeshDraw Asset (.meshdrawasset)**. You can also press **N** in the 3D viewport and use the **MeshDraw** tab.
3. Edit the mesh, UVs, painted textures, bone hierarchy, or skin weights. The imported asset gets its own collection, one Principled material, and packed Base Color, Roughness, Metallic, Emission, and Height images. Height connects through a Bump node.
4. Select an object in that collection and choose **File > Export > MeshDraw Asset (.meshdrawasset)**, or use the MeshDraw sidebar. Choose your export filename.
5. Import that exported file into Unreal using the MeshDraw Unreal importer. You can also import it back into Blender using this add-on.

The exporter uses all mesh/armature objects in the chosen asset collection, including its child collections. Other scene objects are excluded. Keep newly added geometry and the asset's armature in that collection. One armature is supported per asset. Selection is restored after exporting.

Saving the Blender scene retains packed textures and embedded source drawing data. Reopening the `.blend` does not require the original `.meshdrawasset` or temporary extracted files. The add-on reads the embedded FBX; it does not open embedded Blender files.

## What the file preserves

The edited geometry, UVs, skeleton hierarchy, and skin weights are exported in FBX. All five current image maps are embedded as PNGs; flat Principled colors/property values are also supported. Original six-view drawings, drawing layers, and drawn skeleton guides remain embedded as source information. Blender edits do not redraw those source canvases or convert Blender bones into the original drawing guides. To keep Blender edits, use the exported asset directly in Blender or Unreal; regenerating from the source drawings creates a new mesh.

MeshDraw currently uses one material atlas per asset. Keep one Principled material, with image textures directly connected to the appropriate inputs and Height connected through a Bump node. Bake procedural node graphs or multiple materials to images/a single atlas before exporting. Unsupported shader graphs are reported, rather than silently replaced. Material node graphs, bump strength, emission strength, animation, and custom Blender modifiers are not serialized as Blender data; mesh modifiers are evaluated by the standard FBX exporter. FBX retains rig/skin data; animation is not included in this exporter. For other formats, use Blender's usual exporters.

Export writes a checked v1 MeshDraw bundle compatible with the existing Unreal 5.8 importer. Payload checksums, size limits, and safe file names are validated before import. Exports replace the chosen destination only after the complete file has been written.

## Verification

`Tests/verify_blender.py` runs real Blender import/export checks against static and skeletal MeshDraw fixtures, edits geometry and a texture, adds a bone, saves/reopens Blender scenes, checks skin weights/UVs/source data, and verifies corrupt/unsafe files are rejected without changing the scene.

License: GPL-3.0-or-later; see LICENSE. This license applies to the add-on code, not to the user's exported artwork.

The installable extension was also installed into an isolated Blender test profile and tested through its real extension module. Both Blender-exported fixture bundles passed the UE 5.8 import commandlet, creating meshes, five textures, assigned materials, and rig assets.
