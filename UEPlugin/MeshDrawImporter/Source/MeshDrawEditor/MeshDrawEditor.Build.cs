using UnrealBuildTool;
public class MeshDrawEditor : ModuleRules
{
    public MeshDrawEditor(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "UnrealEd", "MeshDrawRuntime" });
        PrivateDependencyModuleNames.AddRange(new[] { "AssetTools", "AssetRegistry", "Json", "JsonUtilities", "Projects", "RenderCore", "Niagara", "NiagaraEditor" });
    }
}
