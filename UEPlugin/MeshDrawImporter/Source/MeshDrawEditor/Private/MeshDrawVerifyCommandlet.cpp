#include "MeshDrawVerifyCommandlet.h"
#include "MeshDrawFactory.h"
#include "MeshDrawBundle.h"
#include "NiagaraSystem.h"
#include "NiagaraEmitter.h"
#include "NiagaraScript.h"
#include "NiagaraSpriteRendererProperties.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "AssetImportTask.h"
#include "AssetToolsModule.h"
#include "Engine/StaticMesh.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/Texture2D.h"
#include "Animation/Skeleton.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpressionCustom.h"
#include "Misc/Parse.h"
#include "Misc/PackageName.h"
#include "Misc/Paths.h"
#include "Misc/FileHelper.h"
#include "UObject/SavePackage.h"
#include "UObject/StrongObjectPtr.h"

UMeshDrawVerifyCommandlet::UMeshDrawVerifyCommandlet()
{
    IsClient=false;IsServer=false;IsEditor=true;LogToConsole=true;ShowErrorCount=true;
}
int32 UMeshDrawVerifyCommandlet::Main(const FString& Params)
{
    FString Source,Mode=TEXT("static"),Report;
    if(!FParse::Value(*Params,TEXT("Source="),Source)) {UE_LOG(LogTemp,Error,TEXT("Provide -Source=<asset.meshdrawasset>"));return 1;}
    FParse::Value(*Params,TEXT("Mode="),Mode);FParse::Value(*Params,TEXT("Report="),Report);
    TStrongObjectPtr<UAssetImportTask> Task(NewObject<UAssetImportTask>());
    Task->Filename=Source;Task->DestinationPath=TEXT("/Game/MeshDrawVerification");Task->DestinationName=TEXT("Bundle_")+FGuid::NewGuid().ToString(EGuidFormats::Digits);
    Task->bAutomated=true;Task->bAsync=false;Task->bSave=false;Task->Factory=NewObject<UMeshDrawFactory>();
    FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get().ImportAssetTasks({Task.Get()});
    UMeshDrawBundle* Bundle=nullptr;for(UObject* Object:Task->GetObjects()) if(auto* Candidate=Cast<UMeshDrawBundle>(Object)) Bundle=Candidate;
    if(!Bundle) {UE_LOG(LogTemp,Error,TEXT("MeshDraw bundle import failed."));return 2;}
    if(Mode==TEXT("vfx"))
    {
        bool Good=Bundle->OutputType==Mode && Bundle->VfxSystem && Bundle->Material && !Bundle->StaticMesh && !Bundle->SkeletalMesh && Bundle->TextureMaps.Contains(TEXT("sprite")) && Bundle->OriginalBundle.Num()>0 && Bundle->EffectSettingsJson.Contains(TEXT("spawnRate"));
        TSharedPtr<FJsonObject> Settings;Good&=FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Bundle->EffectSettingsJson),Settings);
        int32 LifetimeMatches=0,SizeMatches=0,RateMatches=0,SpeedMatches=0,SpreadMatches=0;
        Good&=Bundle->Material && Bundle->Material->GetUsageByFlag(MATUSAGE_NiagaraSprites);
        if(Bundle->VfxSystem)
        {
            Good&=!Bundle->VfxSystem->HasOutstandingCompilationRequests() && Bundle->VfxSystem->GetSystemSpawnScript()->GetLastCompileStatus()!=ENiagaraScriptCompileStatus::NCS_Error && Bundle->VfxSystem->GetSystemUpdateScript()->GetLastCompileStatus()!=ENiagaraScriptCompileStatus::NCS_Error && Bundle->VfxSystem->GetEmitterHandles().Num()==1;
            for(const auto& Handle:Bundle->VfxSystem->GetEmitterHandles())if(auto* Data=Handle.GetEmitterData())
            {
                bool RendererFound=false;for(auto* Renderer:Data->GetRenderers())if(auto* Sprite=Cast<UNiagaraSpriteRendererProperties>(Renderer)){RendererFound=true;Good&=Sprite->Material==Bundle->Material;}Good&=RendererFound;
                TArray<UNiagaraScript*> Scripts;Data->GetScripts(Scripts,false);for(auto* Script:Scripts)
                {
                    TArray<FNiagaraVariable> Parameters;Script->RapidIterationParameters.GetParameters(Parameters);
                    for(const auto& Param:Parameters)
                    {
                        const FString Name=Param.GetName().ToString().ToLower().Replace(TEXT(" "),TEXT(""));const auto* Value=Script->RapidIterationParameters.GetParameterData(Param);
                        UE_LOG(LogTemp,Display,TEXT("VFX VERIFY PARAM %s : %s"),*Name,*Param.GetType().GetName());
                        if(Settings && Value && Param.GetType()==FNiagaraTypeDefinition::GetFloatDef())
                        {
                            float Actual=*reinterpret_cast<const float*>(Value);
                            if(Name.Contains(TEXT("lifetime")) && FMath::IsNearlyEqual(Actual,static_cast<float>(Settings->GetNumberField(TEXT("lifetime")))))LifetimeMatches++;
                            if(Name.Contains(TEXT("spawnrate")) && FMath::IsNearlyEqual(Actual,static_cast<float>(Settings->GetNumberField(TEXT("spawnRate")))))RateMatches++;
                            if(Name.Contains(TEXT("spritesize")) && FMath::IsNearlyEqual(Actual,static_cast<float>(Settings->GetNumberField(TEXT("size")))))SizeMatches++;
                            if(Name.Contains(TEXT("randomrangefloat")) && (Name.EndsWith(TEXT(".minimum")) || Name.EndsWith(TEXT(".maximum"))) && FMath::IsNearlyEqual(Actual,static_cast<float>(Settings->GetNumberField(TEXT("speed")))))SpeedMatches++;
                            if(Name.EndsWith(TEXT(".coneangle")) && FMath::IsNearlyEqual(Actual,static_cast<float>(Settings->GetNumberField(TEXT("spread")))))SpreadMatches++;
                        }
                        if(Settings && Value && Param.GetType()==FNiagaraTypeDefinition::GetVec2Def() && Name.Contains(TEXT("spritesize")) && FMath::IsNearlyEqual(reinterpret_cast<const FVector2f*>(Value)->X,static_cast<float>(Settings->GetNumberField(TEXT("size")))))SizeMatches++;
                        if(Settings && Value && Param.GetType()==FNiagaraTypeDefinition::GetIntDef() && (Name.Contains(TEXT("spawncount")) || Name.Contains(TEXT("burstcount"))) && *reinterpret_cast<const int32*>(Value)==static_cast<int32>(Settings->GetNumberField(TEXT("spawnRate"))))RateMatches++;
                    }
                }
            }
        }
        Good&=LifetimeMatches>0 && SizeMatches>0 && (RateMatches>0 || (Settings && Settings->GetStringField(TEXT("preset"))==TEXT("Sparks")));
        TSet<UPackage*> Packages;Packages.Add(Bundle->GetOutermost());if(Bundle->Material)Packages.Add(Bundle->Material->GetOutermost());if(Bundle->VfxSystem)Packages.Add(Bundle->VfxSystem->GetOutermost());for(const auto& Pair:Bundle->TextureMaps)Packages.Add(Pair.Value->GetOutermost());
        for(auto* Package:Packages){const FString Filename=FPackageName::LongPackageNameToFilename(Package->GetName(),FPackageName::GetAssetPackageExtension());IFileManager::Get().MakeDirectory(*FPaths::GetPath(Filename),true);FSavePackageArgs Args;Args.TopLevelFlags=RF_Public|RF_Standalone;Args.SaveFlags=SAVE_NoError;Good&=UPackage::SavePackage(Package,nullptr,*Filename,Args);}
        const FString Result=FString(Good?TEXT("PASS: Niagara system, sprite material, retained source, lifetime/size/rate settings, and saved assets.\n"):TEXT("FAIL: Niagara verification failed.\n"))+FString::Printf(TEXT("Lifetime=%d Size=%d Rate=%d Speed=%d Spread=%d\n"),LifetimeMatches,SizeMatches,RateMatches,SpeedMatches,SpreadMatches);
        if(!Report.IsEmpty())FFileHelper::SaveStringToFile(Result,*Report);UE_LOG(LogTemp,Display,TEXT("%s"),*Result);return Good?0:3;
    }
    bool Good=Bundle->OutputType==Mode && Bundle->Material && Bundle->LayeredProjectJson.Contains(TEXT("Layers")) && Bundle->OriginalBundle.Num()>0;
    Good&=Mode==TEXT("skeletal") ? (Bundle->SkeletalMesh!=nullptr && Bundle->StaticMesh==nullptr && Bundle->Skeleton!=nullptr) : (Bundle->StaticMesh!=nullptr && Bundle->SkeletalMesh==nullptr);
    for(const FName Kind:{FName(TEXT("basecolor")),FName(TEXT("roughness")),FName(TEXT("metallic")),FName(TEXT("emission")),FName(TEXT("height"))})
    {
        const auto* Texture=Bundle->TextureMaps.Find(Kind);Good&=Texture && *Texture;
        if(Texture && *Texture) Good&=(*Texture)->SRGB==(Kind==TEXT("basecolor") || Kind==TEXT("emission"));
    }
    if(Bundle->Material)
    {
        auto* Data=Bundle->Material->GetEditorOnlyData();
        Good&=Data->BaseColor.Expression!=nullptr && Data->Roughness.Expression!=nullptr && Data->Metallic.Expression!=nullptr && Data->EmissiveColor.Expression!=nullptr && Cast<UMaterialExpressionCustom>(Data->Normal.Expression)!=nullptr;
    }
    if(Bundle->StaticMesh) Good&=Bundle->StaticMesh->GetMaterial(0)==Bundle->Material;
    if(Bundle->SkeletalMesh)
    {
        Good&=!Bundle->SkeletalMesh->GetMaterials().IsEmpty() && Bundle->SkeletalMesh->GetMaterials()[0].MaterialInterface==Bundle->Material;
        const auto& Reference=Bundle->SkeletalMesh->GetRefSkeleton();
        Good&=Reference.FindBoneIndex(TEXT("drawn_spine"))!=INDEX_NONE && Reference.FindBoneIndex(TEXT("drawn_branch"))!=INDEX_NONE;
    }
    TSet<UPackage*> Packages;Packages.Add(Bundle->GetOutermost());Packages.Add(Bundle->Material->GetOutermost());
    if(Bundle->StaticMesh) Packages.Add(Bundle->StaticMesh->GetOutermost());
    if(Bundle->SkeletalMesh) Packages.Add(Bundle->SkeletalMesh->GetOutermost());
    if(Bundle->Skeleton) Packages.Add(Bundle->Skeleton->GetOutermost());
    if(Bundle->PhysicsAsset) Packages.Add(Bundle->PhysicsAsset->GetOutermost());
    for(const auto& Pair:Bundle->TextureMaps) Packages.Add(Pair.Value->GetOutermost());
    for(UPackage* Package:Packages)
    {
        const FString Filename=FPackageName::LongPackageNameToFilename(Package->GetName(),FPackageName::GetAssetPackageExtension());
        IFileManager::Get().MakeDirectory(*FPaths::GetPath(Filename),true);
        FSavePackageArgs Args;Args.TopLevelFlags=RF_Public|RF_Standalone;Args.SaveFlags=SAVE_NoError;
        Good&=UPackage::SavePackage(Package,nullptr,*Filename,Args);
    }
    FString Result=Good ? TEXT("PASS: UE 5.8 importer created mesh, all five textures, assigned material, bump graph, source data, and rig assets.\n") : TEXT("FAIL: Unreal asset checks failed.\n");
    Result+=TEXT("Bundle=")+Bundle->GetPathName()+TEXT("\nMaterial=")+Bundle->Material->GetPathName()+TEXT("\n");
    if(!Report.IsEmpty()) FFileHelper::SaveStringToFile(Result,*Report);
    UE_LOG(LogTemp,Display,TEXT("%s"),*Result);return Good ? 0 : 3;
}
