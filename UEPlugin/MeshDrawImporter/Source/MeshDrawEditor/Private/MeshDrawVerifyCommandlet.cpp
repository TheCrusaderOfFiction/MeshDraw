#include "MeshDrawVerifyCommandlet.h"
#include "MeshDrawFactory.h"
#include "MeshDrawBundle.h"
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
