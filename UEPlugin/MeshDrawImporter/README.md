# MeshDraw Importer for Unreal Engine 5.8

## Install

1. Close Unreal Editor.
2. Copy the packaged `MeshDrawImporter` folder from `UEPlugin/Packaged` into your Unreal project's `Plugins` folder. Create `Plugins` if needed.
3. Open the project. Enable **MeshDraw Importer** under Edit > Plugins if it is not already enabled, then restart when prompted.

The packaged Windows binaries were built against Unreal Engine 5.8.2. Source code is included for rebuilding with another engine build.

## Export and import

In MeshDraw, choose Static Mesh, Skeletal Mesh, or VFX · Niagara, draw and paint, and generate. Generation writes `asset.meshdrawasset`. **Export Unreal file** saves a copy of the last generated snapshot; regenerate after changing drawings, materials, or bones.

In Unreal's Content Browser, choose **Import**, select the `.meshdrawasset` file, and save the imported assets with **Save All**.

The importer creates a MeshDraw bundle asset and a separate folder containing the mesh, textures, and connected material. Skeletal output also creates a Skeleton and Physics Asset. Base Color, Roughness, Metallic, Emission, and Height/Bump are connected automatically, and the material is assigned to the mesh. Height affects surface shading, not the mesh shape. No animations are included.

The bundle retains the complete original file, editable layered project, and drawn skeleton as editor source data. The custom file also carries the generated Blender file, source drawings, material paintings, and generation metadata when available. Extracted files remain under the Unreal project's `Saved/MeshDraw` folder for import source references. Keep the original custom file as a portable backup. Cooked builds retain runtime asset references; editor source data is excluded.

Repeated imports create new asset folders. In-place reimport is not implemented. Review AI topology, skeletal deformation, and physics bodies before production use.

## Niagara VFX

VFX bundles create `NS_MeshDrawVfx`, an unlit particle material, a transparent sprite texture, and a source bundle. The Niagara plugin is enabled as a dependency. Fire, Smoke, and Magic use an upward cone; Sparks use a radial burst. Particle settings, tint, blending, and looping are applied during import. Drag the Niagara system into a level or open it to customize the effect.

The local MeshDraw preview approximates the effect; Unreal renders the final Niagara system. VFX import checks passed for all four presets, including sprite/material references, particle settings, source preservation, compilation, and saved assets. Headless checks do not verify viewport appearance.

## Validation

The plugin was compiled against installed UE 5.8.2. Headless editor import tests passed for static and skeletal fixtures, including five texture maps, texture color spaces, material connections, bump graph, source preservation, material assignment, and custom bone names. These tests do not validate viewport appearance or animation quality.

The test project is `UEPlugin/Tests/ImportHost`. Its `MeshDrawVerify` commandlet imports a real custom file and saves the resulting assets. Python package tests also check byte-for-byte recovery, checksums, and rejection of unsafe paths.
