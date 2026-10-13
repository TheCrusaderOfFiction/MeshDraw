using UnrealBuildTool;
public class MeshDrawRuntime : ModuleRules
{
    public MeshDrawRuntime(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "Niagara" });
    }
}
