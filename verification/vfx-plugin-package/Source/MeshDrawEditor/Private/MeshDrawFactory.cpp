#include "MeshDrawFactory.h"
#include "MeshDrawBundle.h"
#include "MeshDrawVfx.h"
#include "AssetToolsModule.h"
#include "AssetImportTask.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Factories/FbxFactory.h"
#include "Factories/FbxImportUI.h"
#include "Factories/FbxStaticMeshImportData.h"
#include "Factories/FbxSkeletalMeshImportData.h"
#include "Factories/TextureFactory.h"
#include "Engine/StaticMesh.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/Texture2D.h"
#include "Animation/Skeleton.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpressionTextureSample.h"
#include "Materials/MaterialExpressionCustom.h"
#include "Materials/MaterialExpressionWorldPosition.h"
#include "Materials/MaterialExpressionVertexNormalWS.h"
#include "Misc/Base64.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/PackageName.h"
#include "Misc/SecureHash.h"
#include "Misc/FeedbackContext.h"
#include "Internationalization/Regex.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/SavePackage.h"
#include "UObject/StrongObjectPtr.h"

DEFINE_LOG_CATEGORY_STATIC(LogMeshDraw,Log,All);

namespace MeshDrawImport
{
    constexpr int64 MaxBytes=256ll*1024*1024;
    bool Parse(const TArray<uint8>& Bytes,TSharedPtr<FJsonObject>& Root,TMap<FString,TArray<uint8>>& Files,FString& Error)
    {
        const FUTF8ToTCHAR Converted(reinterpret_cast<const ANSICHAR*>(Bytes.GetData()),Bytes.Num());
        const FString Json(Converted.Length(),Converted.Get());
        if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json),Root) || !Root.IsValid()) {Error=TEXT("Invalid MeshDraw JSON.");return false;}
        FString Format,Mode; double Version;
        if(!Root->TryGetStringField(TEXT("format"),Format) || Format!=TEXT("MeshDrawAsset") || !Root->TryGetNumberField(TEXT("version"),Version) || Version!=1
            || !Root->TryGetStringField(TEXT("outputType"),Mode) || (Mode!=TEXT("static") && Mode!=TEXT("skeletal") && Mode!=TEXT("vfx")))
        {Error=TEXT("Unsupported MeshDraw asset format or output type.");return false;}
        const TArray<TSharedPtr<FJsonValue>>* Entries;
        if(!Root->TryGetArrayField(TEXT("files"),Entries) || Entries->Num()>128) {Error=TEXT("Invalid file manifest.");return false;}
        int64 Total=0;
        for(const auto& Value:*Entries)
        {
            if(!Value.IsValid() || Value->Type!=EJson::Object) {Error=TEXT("Invalid file entry.");return false;}
            auto Entry=Value->AsObject(); FString Name,Data,Hash; double Size;
            if(!Entry->TryGetStringField(TEXT("name"),Name) || !Entry->TryGetStringField(TEXT("data"),Data) || !Entry->TryGetStringField(TEXT("sha1"),Hash) || !Entry->TryGetNumberField(TEXT("size"),Size)
                || Size<0 || Size>96ll*1024*1024 || Data.Len()>128*1024*1024 || Files.Contains(Name)) {Error=TEXT("Invalid or duplicate file entry.");return false;}
            FRegexMatcher Matcher(FRegexPattern(TEXT("^[A-Za-z0-9_-]+\\.(fbx|png|json|meshdraw|blend|obj)$")),Name);
            if(!Matcher.FindNext()) {Error=TEXT("Unsafe file name in MeshDraw bundle.");return false;}
            TArray<uint8> Payload;
            if(!FBase64::Decode(Data,Payload) || Payload.Num()!=Size || FSHA1::HashBuffer(Payload.GetData(),Payload.Num()).ToString().ToLower()!=Hash.ToLower())
            {Error=TEXT("A bundled file is damaged or has an invalid checksum.");return false;}
            Total+=Payload.Num(); if(Total>MaxBytes) {Error=TEXT("Bundle exceeds the 256 MB payload limit.");return false;}
            Files.Add(Name,MoveTemp(Payload));
        }
        if(Mode==TEXT("vfx"))
        {
            for(const FString& Required:{TEXT("effect.json"),TEXT("vfx_sprite.png"),TEXT("project.meshdraw"),TEXT("skeleton.json"),TEXT("job.json")})if(!Files.Contains(Required)){Error=TEXT("Missing VFX source: ")+Required;return false;}
            FString Effect,Sprite,Project;
            if(!Root->TryGetStringField(TEXT("effect"),Effect) || Effect!=TEXT("effect.json") || !Root->TryGetStringField(TEXT("sprite"),Sprite) || Sprite!=TEXT("vfx_sprite.png") || !Root->TryGetStringField(TEXT("project"),Project) || Project!=TEXT("project.meshdraw")) {Error=TEXT("Invalid VFX source references.");return false;}
            return true;
        }
        for(const FString& Required:{TEXT("mesh.fbx"),TEXT("project.meshdraw"),TEXT("skeleton.json"),TEXT("job.json"),TEXT("front.png"),TEXT("back.png"),TEXT("left.png"),TEXT("right.png"),TEXT("top.png"),TEXT("bottom.png")})
            if(!Files.Contains(Required)) {Error=TEXT("A required source file is missing: ")+Required;return false;}
        FString Mesh,Project,Skeleton;
        if(!Root->TryGetStringField(TEXT("mesh"),Mesh) || Mesh!=TEXT("mesh.fbx") || !Root->TryGetStringField(TEXT("project"),Project) || Project!=TEXT("project.meshdraw")
            || !Root->TryGetStringField(TEXT("skeleton"),Skeleton) || Skeleton!=TEXT("skeleton.json")) {Error=TEXT("Invalid source references.");return false;}
        const TSharedPtr<FJsonObject>* Textures;
        if(!Root->TryGetObjectField(TEXT("textures"),Textures)) {Error=TEXT("Missing texture manifest.");return false;}
        const TSet<FString> Kinds={TEXT("basecolor"),TEXT("roughness"),TEXT("metallic"),TEXT("emission"),TEXT("height")};
        for(const auto& Pair:(*Textures)->Values)
        {
            const FString Kind(*Pair.Key);
            FString Name;
            if(!Kinds.Contains(Kind) || !Pair.Value->TryGetString(Name) || Name!=TEXT("mesh_")+Kind+TEXT(".png") || !Files.Contains(Name))
            {Error=TEXT("Invalid texture reference.");return false;}
        }
        return true;
    }
    FString Text(const TArray<uint8>& Bytes)
    {
        const FUTF8ToTCHAR Converted(reinterpret_cast<const ANSICHAR*>(Bytes.GetData()),Bytes.Num());
        return FString(Converted.Length(),Converted.Get());
    }
    TArray<UObject*> ImportFile(const FString& Filename,const FString& Folder,const FString& Name,UFactory* Factory,UObject* Options=nullptr)
    {
        TStrongObjectPtr<UAssetImportTask> Task(NewObject<UAssetImportTask>());
        Task->Filename=Filename;Task->DestinationPath=Folder;Task->DestinationName=Name;
        Task->bAutomated=true;Task->bAsync=false;Task->bSave=false;Task->bReplaceExisting=false;
        Task->Factory=Factory;Task->Options=Options;
        FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get().ImportAssetTasks({Task.Get()});
        return Task->GetObjects();
    }
    UMaterial* Material(const FString& Folder,UMeshDrawBundle* Bundle)
    {
        UPackage* Package=CreatePackage(*(Folder/TEXT("M_MeshDraw")));
        UMaterial* Result=NewObject<UMaterial>(Package,TEXT("M_MeshDraw"),RF_Public|RF_Standalone);
        Result->GetEditorOnlyData()->BaseColor.UseConstant=true;
        Result->GetEditorOnlyData()->BaseColor.Constant=FColor(166,166,166);
        Result->GetEditorOnlyData()->Roughness.UseConstant=true;
        Result->GetEditorOnlyData()->Roughness.Constant=.5f;
        UMaterialExpressionTextureSample* Height=nullptr; int32 Row=0;
        for(const auto& Pair:Bundle->TextureMaps)
        {
            const FString Kind=Pair.Key.ToString();
            auto* Sample=NewObject<UMaterialExpressionTextureSample>(Result);
            Sample->Texture=Pair.Value;Sample->SamplerType=(Kind==TEXT("basecolor") || Kind==TEXT("emission")) ? SAMPLERTYPE_Color : SAMPLERTYPE_Masks;
            Sample->MaterialExpressionEditorX=-500;Sample->MaterialExpressionEditorY=Row++*200;
            Result->GetExpressionCollection().AddExpression(Sample);
            auto* Data=Result->GetEditorOnlyData();
            if(Kind==TEXT("basecolor")) Data->BaseColor.Connect(0,Sample);
            else if(Kind==TEXT("roughness")) Data->Roughness.Connect(1,Sample);
            else if(Kind==TEXT("metallic")) Data->Metallic.Connect(1,Sample);
            else if(Kind==TEXT("emission")) Data->EmissiveColor.Connect(0,Sample);
            else if(Kind==TEXT("height")) Height=Sample;
        }
        if(Height)
        {
            auto* Position=NewObject<UMaterialExpressionWorldPosition>(Result);
            auto* Normal=NewObject<UMaterialExpressionVertexNormalWS>(Result);
            auto* Bump=NewObject<UMaterialExpressionCustom>(Result);
            Bump->Description=TEXT("MeshDraw height to world normal (2 cm)");Bump->OutputType=CMOT_Float3;
            Bump->Inputs.Empty();
            Bump->Code=TEXT("float3 N=normalize(Normal); float3 Q1=ddx(Position); float3 Q2=ddy(Position); float3 R1=cross(Q2,N); float3 R2=cross(N,Q1); float D=dot(Q1,R1); float3 G=(ddx(Height)*R1+ddy(Height)*R2)*sign(D); return normalize(max(abs(D),1e-8)*N-2.0*G);");
            FCustomInput H;H.InputName=TEXT("Height");H.Input.Connect(1,Height);Bump->Inputs.Add(H);
            FCustomInput P;P.InputName=TEXT("Position");P.Input.Connect(0,Position);Bump->Inputs.Add(P);
            FCustomInput N;N.InputName=TEXT("Normal");N.Input.Connect(0,Normal);Bump->Inputs.Add(N);
            Result->GetExpressionCollection().AddExpression(Position);Result->GetExpressionCollection().AddExpression(Normal);Result->GetExpressionCollection().AddExpression(Bump);
            Result->bTangentSpaceNormal=false;Result->GetEditorOnlyData()->Normal.Connect(0,Bump);
        }
        Result->PostEditChange();Result->MarkPackageDirty();FAssetRegistryModule::AssetCreated(Result);return Result;
    }
}

UMeshDrawFactory::UMeshDrawFactory()
{
    SupportedClass=UMeshDrawBundle::StaticClass();bCreateNew=false;bEditorImport=true;
    Formats.Add(TEXT("meshdrawasset;MeshDraw complete object"));
}
bool UMeshDrawFactory::FactoryCanImport(const FString& Filename) {return FPaths::GetExtension(Filename).Equals(TEXT("meshdrawasset"),ESearchCase::IgnoreCase);}
UObject* UMeshDrawFactory::FactoryCreateFile(UClass* InClass,UObject* InParent,FName InName,EObjectFlags Flags,const FString& Filename,const TCHAR* Parms,FFeedbackContext* Warn,bool& bOutOperationCanceled)
{
    bOutOperationCanceled=false; FString Error; TArray<uint8> Bytes;TSharedPtr<FJsonObject> Root;TMap<FString,TArray<uint8>> Files;
    if(IFileManager::Get().FileSize(*Filename)>384ll*1024*1024 || !FFileHelper::LoadFileToArray(Bytes,*Filename) || !MeshDrawImport::Parse(Bytes,Root,Files,Error))
    {UE_LOG(LogMeshDraw,Error,TEXT("Import rejected: %s"),*Error);if(Warn) Warn->Logf(ELogVerbosity::Error,TEXT("MeshDraw: %s"),*Error);return nullptr;}
    // Keep checked source data in Saved so Unreal's own import source references remain valid.
    const FString SourceFolder=FPaths::ConvertRelativePathToFull(FPaths::ProjectSavedDir()/TEXT("MeshDraw")/FGuid::NewGuid().ToString(EGuidFormats::Digits));
    IFileManager::Get().MakeDirectory(*SourceFolder,true);
    for(const auto& Pair:Files) if(!FFileHelper::SaveArrayToFile(Pair.Value,*(SourceFolder/Pair.Key))) {UE_LOG(LogMeshDraw,Error,TEXT("Could not extract checked source data."));return nullptr;}
    const FString Folder=FPackageName::GetLongPackagePath(InParent->GetOutermost()->GetName())/(InName.ToString()+TEXT("_Assets_")+FGuid::NewGuid().ToString(EGuidFormats::Digits).Left(8));
    TStrongObjectPtr<UMeshDrawBundle> Bundle(NewObject<UMeshDrawBundle>(InParent,InName,Flags));
    Bundle->OutputType=Root->GetStringField(TEXT("outputType"));
    Bundle->SourceFilename=FPaths::ConvertRelativePathToFull(Filename);Bundle->LayeredProjectJson=MeshDrawImport::Text(Files[TEXT("project.meshdraw")]);
    Bundle->DrawnSkeletonJson=MeshDrawImport::Text(Files[TEXT("skeleton.json")]);Bundle->OriginalBundle=Bytes;
    if(Bundle->OutputType==TEXT("vfx"))
    {
        if(!ImportMeshDrawVfx(Folder,SourceFolder,Bundle.Get(),Error)){UE_LOG(LogMeshDraw,Error,TEXT("VFX import failed: %s"),*Error);return nullptr;}
        return Bundle.Get();
    }
    UFbxFactory* Fbx=NewObject<UFbxFactory>();Fbx->SetDetectImportTypeOnImport(false);
    UFbxImportUI* Options=NewObject<UFbxImportUI>(Fbx);Fbx->ImportUI=Options;
    const bool Skeletal=Bundle->OutputType==TEXT("skeletal");
    Options->bImportAsSkeletal=Skeletal;Options->MeshTypeToImport=Skeletal ? FBXIT_SkeletalMesh : FBXIT_StaticMesh;
    Options->OriginalImportType=Options->MeshTypeToImport;Options->bImportMesh=true;Options->bImportMaterials=false;Options->bImportTextures=false;
    Options->bImportAnimations=false;Options->bCreatePhysicsAsset=Skeletal;Options->bOverrideFullName=true;
    Options->StaticMeshImportData->bCombineMeshes=true;
    TArray<UObject*> Imported=MeshDrawImport::ImportFile(SourceFolder/TEXT("mesh.fbx"),Folder,Skeletal ? TEXT("SK_MeshDraw") : TEXT("SM_MeshDraw"),Fbx,Options);
    for(UObject* Object:Imported)
    {
        if(auto* Mesh=Cast<UStaticMesh>(Object)) Bundle->StaticMesh=Mesh;
        if(auto* Mesh=Cast<USkeletalMesh>(Object)) Bundle->SkeletalMesh=Mesh;
    }
    if((Skeletal && !Bundle->SkeletalMesh) || (!Skeletal && !Bundle->StaticMesh)) {UE_LOG(LogMeshDraw,Error,TEXT("FBX mesh import failed."));return nullptr;}
    for(const auto& Pair:Root->GetObjectField(TEXT("textures"))->Values)
    {
        const FString Kind(*Pair.Key);
        UTextureFactory* Factory=NewObject<UTextureFactory>();
        const auto Results=MeshDrawImport::ImportFile(SourceFolder/Pair.Value->AsString(),Folder,TEXT("T_MeshDraw_")+Kind,Factory);
        UTexture2D* Texture=nullptr;for(UObject* Object:Results) if(auto* Candidate=Cast<UTexture2D>(Object)) Texture=Candidate;
        if(!Texture) {UE_LOG(LogMeshDraw,Error,TEXT("Texture import failed: %s"),*Kind);return nullptr;}
        Texture->SRGB=Kind==TEXT("basecolor") || Kind==TEXT("emission");
        Texture->CompressionSettings=Texture->SRGB ? TC_Default : TC_Masks;
        Texture->PostEditChange();Texture->MarkPackageDirty();Bundle->TextureMaps.Add(FName(*Kind),Texture);
    }
    Bundle->Material=MeshDrawImport::Material(Folder,Bundle.Get());
    if(Bundle->StaticMesh) {Bundle->StaticMesh->SetMaterial(0,Bundle->Material);Bundle->StaticMesh->PostEditChange();Bundle->StaticMesh->MarkPackageDirty();}
    if(Bundle->SkeletalMesh)
    {
        auto Materials=Bundle->SkeletalMesh->GetMaterials();
        if(Materials.IsEmpty()) Materials.Add(FSkeletalMaterial(Bundle->Material));else for(auto& Slot:Materials) Slot.MaterialInterface=Bundle->Material;
        Bundle->SkeletalMesh->SetMaterials(Materials);Bundle->Skeleton=Bundle->SkeletalMesh->GetSkeleton();Bundle->PhysicsAsset=Bundle->SkeletalMesh->GetPhysicsAsset();
        Bundle->SkeletalMesh->PostEditChange();Bundle->SkeletalMesh->MarkPackageDirty();
    }
    Bundle->MarkPackageDirty();
    UE_LOG(LogMeshDraw,Display,TEXT("Imported MeshDraw %s into %s (%d material channels)."),*Bundle->OutputType,*Folder,Bundle->TextureMaps.Num());
    return Bundle.Get();
}
