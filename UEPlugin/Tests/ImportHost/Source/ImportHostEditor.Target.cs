using UnrealBuildTool;
public class ImportHostEditorTarget : TargetRules
{
    public ImportHostEditorTarget(TargetInfo Target) : base(Target)
    {
        Type=TargetType.Editor;
        DefaultBuildSettings=BuildSettingsVersion.Latest;
        ExtraModuleNames.Add("ImportHost");
    }
}
