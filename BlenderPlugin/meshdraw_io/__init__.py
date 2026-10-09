bl_info = {"name": "MeshDraw Import Export", "author": "MeshDraw", "version": (1, 0, 0), "blender": (4, 2, 0), "location": "File > Import / Export; 3D View > Sidebar > MeshDraw", "description": "Import and export self-contained MeshDraw assets", "category": "Import-Export"}

import bpy
from bpy.props import StringProperty
from bpy_extras.io_utils import ImportHelper, ExportHelper
from . import bridge

class MESHDRAW_OT_import_asset(bpy.types.Operator, ImportHelper):
    bl_idname = "meshdraw.import_asset"
    bl_label = "Import MeshDraw Asset"
    bl_options = {"REGISTER", "UNDO"}
    filename_ext = ".meshdrawasset"
    filter_glob: StringProperty(default="*.meshdrawasset", options={"HIDDEN"})

    def execute(self, context):
        try:
            collection = bridge.import_asset(context, self.filepath)
            self.report({"INFO"}, "Imported " + collection.name)
            return {"FINISHED"}
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

class MESHDRAW_OT_export_asset(bpy.types.Operator, ExportHelper):
    bl_idname = "meshdraw.export_asset"
    bl_label = "Export MeshDraw Asset"
    filename_ext = ".meshdrawasset"
    filter_glob: StringProperty(default="*.meshdrawasset", options={"HIDDEN"})

    def execute(self, context):
        try:
            count, mode = bridge.export_asset(context, self.filepath)
            self.report({"INFO"}, f"Exported {count} mesh object(s), {mode}, and five maps")
            return {"FINISHED"}
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

class MESHDRAW_PT_asset(bpy.types.Panel):
    bl_label = "MeshDraw"
    bl_idname = "MESHDRAW_PT_asset"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "MeshDraw"

    def draw(self, context):
        layout = self.layout
        layout.operator("meshdraw.import_asset", icon="IMPORT")
        collection = bridge.asset_collection(context)
        if collection:
            layout.label(text=collection.name, icon="OUTLINER_COLLECTION")
            layout.label(text="Exports this collection and its skeleton")
        else: layout.label(text="Select an imported MeshDraw object")
        row = layout.row(); row.enabled = collection is not None
        row.operator("meshdraw.export_asset", icon="EXPORT")
        layout.label(text="Source drawings stay embedded")

classes = (MESHDRAW_OT_import_asset, MESHDRAW_OT_export_asset, MESHDRAW_PT_asset)

def menu_import(self, context): self.layout.operator(MESHDRAW_OT_import_asset.bl_idname, text="MeshDraw Asset (.meshdrawasset)")
def menu_export(self, context): self.layout.operator(MESHDRAW_OT_export_asset.bl_idname, text="MeshDraw Asset (.meshdrawasset)")

def register():
    for cls in classes: bpy.utils.register_class(cls)
    bpy.types.TOPBAR_MT_file_import.append(menu_import)
    bpy.types.TOPBAR_MT_file_export.append(menu_export)

def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(menu_export)
    bpy.types.TOPBAR_MT_file_import.remove(menu_import)
    for cls in reversed(classes): bpy.utils.unregister_class(cls)
