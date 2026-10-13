#include "MeshDrawVfx.h"
#include "MeshDrawBundle.h"
#include "NiagaraSystem.h"
#include "NiagaraEmitter.h"
#include "NiagaraEmitterHandle.h"
#include "NiagaraScript.h"
#include "NiagaraScriptSource.h"
#include "NiagaraGraph.h"
#include "NiagaraNodeFunctionCall.h"
#include "EdGraph/EdGraphPin.h"
#include "NiagaraSystemFactoryNew.h"
#include "NiagaraEditorUtilities.h"
#include "NiagaraSpriteRendererProperties.h"
#include "AssetToolsModule.h"
#include "AssetImportTask.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Factories/TextureFactory.h"
#include "Engine/Texture2D.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpressionTextureSample.h"
#include "Materials/MaterialExpressionParticleColor.h"
#include "Materials/MaterialExpressionVectorParameter.h"
#include "Materials/MaterialExpressionMultiply.h"
#include "Misc/FileHelper.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Internationalization/Regex.h"
#include "UObject/StrongObjectPtr.h"

DEFINE_LOG_CATEGORY_STATIC(LogMeshDrawVfx,Log,All);

bool ImportMeshDrawVfx(const FString& Folder,const FString& SourceFolder,UMeshDrawBundle* Bundle,FString& Error)
{
    FString Json;TSharedPtr<FJsonObject> Recipe;
    if(!FFileHelper::LoadFileToString(Json,*(SourceFolder/TEXT("effect.json"))) || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json),Recipe)) {Error=TEXT("Invalid VFX recipe.");return false;}
    FString Preset,Color,Blend;double Version=0,Rate=0,Life=0,Size=0,Speed=0,Spread=0;bool Loop=false;
    if(!Recipe->TryGetNumberField(TEXT("version"),Version) || Version!=1 || !Recipe->TryGetStringField(TEXT("preset"),Preset)
       || !TSet<FString>({TEXT("Fire"),TEXT("Smoke"),TEXT("Sparks"),TEXT("Magic")}).Contains(Preset)
       || !Recipe->TryGetNumberField(TEXT("spawnRate"),Rate) || !FMath::IsFinite(Rate) || Rate<1 || Rate>500
       || !Recipe->TryGetNumberField(TEXT("lifetime"),Life) || !FMath::IsFinite(Life) || Life<.1 || Life>10
       || !Recipe->TryGetNumberField(TEXT("size"),Size) || !FMath::IsFinite(Size) || Size<1 || Size>200
       || !Recipe->TryGetNumberField(TEXT("speed"),Speed) || !FMath::IsFinite(Speed) || Speed<0 || Speed>1000
       || !Recipe->TryGetNumberField(TEXT("spread"),Spread) || !FMath::IsFinite(Spread) || Spread<0 || Spread>180
       || !Recipe->TryGetBoolField(TEXT("loop"),Loop) || !Recipe->TryGetStringField(TEXT("color"),Color)
       || !Recipe->TryGetStringField(TEXT("blend"),Blend) || (Blend!=TEXT("Additive") && Blend!=TEXT("Translucent"))) {Error=TEXT("Invalid VFX settings.");return false;}
    FRegexMatcher ColorCheck(FRegexPattern(TEXT("^#[A-Fa-f0-9]{6}$")),Color);if(!ColorCheck.FindNext()){Error=TEXT("Invalid VFX color.");return false;}
    const FString TemplateName=Preset==TEXT("Sparks") ? TEXT("OmnidirectionalBurst") : TEXT("Fountain");
    auto* Template=LoadObject<UNiagaraEmitter>(nullptr,*(TEXT("/Niagara/DefaultAssets/Templates/Emitters/")+TemplateName+TEXT(".")+TemplateName));
    if(!Template){Error=TEXT("Required Niagara template is unavailable. Enable Niagara.");return false;}
    TStrongObjectPtr<UAssetImportTask> Task(NewObject<UAssetImportTask>());Task->Filename=SourceFolder/TEXT("vfx_sprite.png");Task->DestinationPath=Folder;Task->DestinationName=TEXT("T_MeshDrawVfx");Task->bAutomated=true;Task->bAsync=false;Task->bSave=false;Task->Factory=NewObject<UTextureFactory>();
    FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get().ImportAssetTasks({Task.Get()});
    UTexture2D* Texture=nullptr;for(auto* Object:Task->GetObjects())if(auto* T=Cast<UTexture2D>(Object))Texture=T;
    if(!Texture){Error=TEXT("Could not import the VFX sprite.");return false;}
    Texture->SRGB=true;Texture->CompressionSettings=TC_Default;Texture->AddressX=TA_Clamp;Texture->AddressY=TA_Clamp;Texture->PostEditChange();Texture->MarkPackageDirty();Bundle->TextureMaps.Add(TEXT("sprite"),Texture);
    auto* Material=NewObject<UMaterial>(CreatePackage(*(Folder/TEXT("M_MeshDrawVfx"))),TEXT("M_MeshDrawVfx"),RF_Public|RF_Standalone);
    Material->BlendMode=Blend==TEXT("Additive")?BLEND_Additive:BLEND_Translucent;Material->SetShadingModel(MSM_Unlit);Material->TwoSided=true;Material->SetUsageByFlag(MATUSAGE_NiagaraSprites,true);
    auto* Sample=NewObject<UMaterialExpressionTextureSample>(Material);Sample->Texture=Texture;Sample->SamplerType=SAMPLERTYPE_Color;
    auto* Particle=NewObject<UMaterialExpressionParticleColor>(Material);
    auto* Tint=NewObject<UMaterialExpressionVectorParameter>(Material);Tint->ParameterName=TEXT("EffectTint");Tint->DefaultValue=FLinearColor(FColor::FromHex(Color));
    auto* Multiply=NewObject<UMaterialExpressionMultiply>(Material);Multiply->A.Connect(0,Sample);Multiply->B.Connect(0,Tint);
    auto* Emission=NewObject<UMaterialExpressionMultiply>(Material);Emission->A.Connect(0,Multiply);Emission->B.Connect(0,Particle);
    auto* Opacity=NewObject<UMaterialExpressionMultiply>(Material);Opacity->A.Connect(4,Sample);Opacity->B.Connect(4,Particle);
    const TArray<UMaterialExpression*> Expressions={Sample,Particle,Tint,Multiply,Emission,Opacity};for(auto* Expression:Expressions)Material->GetExpressionCollection().AddExpression(Expression);
    Material->GetEditorOnlyData()->EmissiveColor.Connect(0,Emission);Material->GetEditorOnlyData()->Opacity.Connect(0,Opacity);Material->PostEditChange();Material->MarkPackageDirty();FAssetRegistryModule::AssetCreated(Material);Bundle->Material=Material;
    auto* System=NewObject<UNiagaraSystem>(CreatePackage(*(Folder/TEXT("NS_MeshDrawVfx"))),TEXT("NS_MeshDrawVfx"),RF_Public|RF_Standalone|RF_Transactional);
    UNiagaraSystemFactoryNew::InitializeSystem(System,true);FNiagaraEditorUtilities::AddEmitterToSystem(*System,*Template,Template->GetExposedVersion().VersionGuid);
    int32 Changed=0,LoopPins=0;
    for(auto& Handle:System->GetEmitterHandles())
    {
        auto* Data=Handle.GetEmitterData();if(!Data)continue;
        TArray<UNiagaraScript*> Scripts;Data->GetScripts(Scripts,false);
        for(auto* Script:Scripts)
        {
            TArray<FNiagaraVariable> Parameters;Script->RapidIterationParameters.GetParameters(Parameters);
            for(const auto& Param:Parameters)
            {
                const FString Name=Param.GetName().ToString().ToLower().Replace(TEXT(" "),TEXT(""));bool Updated=false;
                UE_LOG(LogMeshDrawVfx,Verbose,TEXT("Parameter: %s (%s)"),*Name,*Param.GetType().GetName());
                if(Param.GetType()==FNiagaraTypeDefinition::GetFloatDef())
                {
                    float Value=0;bool Match=true;
                    if(Name.Contains(TEXT("spawnrate")) && Name.EndsWith(TEXT(".spawnrate")))Value=Rate;
                    else if(Name.Contains(TEXT("spawncount")) || Name.Contains(TEXT("burstcount")))Value=Rate;
                    else if(Name.Contains(TEXT("lifetime")))Value=Life;
                    else if(Name.Contains(TEXT("spritesize")))Value=Size;
                    else if(Name.EndsWith(TEXT(".velocityspeedscale")))Value=1;
                    else if(Name.Contains(TEXT("randomrangefloat")) && (Name.EndsWith(TEXT(".minimum")) || Name.EndsWith(TEXT(".maximum"))))Value=Speed;
                    else if(Name.Contains(TEXT("velocitystrength")))Value=Speed;
                    else if(Name.EndsWith(TEXT(".coneangle")))Value=Spread;
                    else if(Name.Contains(TEXT("loopduration")))Value=Life;
                    else Match=false;
                    if(Match)Updated=Script->RapidIterationParameters.SetParameterValue(Value,Param);
                }
                else if(Param.GetType()==FNiagaraTypeDefinition::GetVec2Def() && Name.Contains(TEXT("spritesize")))Updated=Script->RapidIterationParameters.SetParameterValue(FVector2f(Size,Size),Param);
                else if(Param.GetType()==FNiagaraTypeDefinition::GetVec3Def() && Name.Contains(TEXT("gravity")))Updated=Script->RapidIterationParameters.SetParameterValue(FVector3f(0,0,Preset==TEXT("Sparks")?-980:0),Param);
                else if(Param.GetType()==FNiagaraTypeDefinition::GetVec3Def() && Name.EndsWith(TEXT(".velocity")))Updated=Script->RapidIterationParameters.SetParameterValue(FVector3f(0,0,Speed),Param);
                else if(Param.GetType()==FNiagaraTypeDefinition::GetColorDef() && Name.Contains(TEXT("color")))Updated=Script->RapidIterationParameters.SetParameterValue(FLinearColor::White,Param);
                else if(Param.GetType()==FNiagaraTypeDefinition::GetIntDef() && (Name.Contains(TEXT("spawncount")) || Name.Contains(TEXT("burstcount"))))Updated=Script->RapidIterationParameters.SetParameterValue(static_cast<int32>(Rate),Param);
                else if(Name.Contains(TEXT("loopbehavior")) && Param.GetType().GetSize()==4) {const int32 Behavior=Loop?0:2;Updated=Script->RapidIterationParameters.SetParameterData(reinterpret_cast<const uint8*>(&Behavior),Param);}
                if(Updated){Changed++;UE_LOG(LogMeshDrawVfx,Display,TEXT("Applied VFX parameter: %s"),*Name);}
            }
        }
        for(auto* Renderer:Data->GetRenderers())if(auto* Sprite=Cast<UNiagaraSpriteRendererProperties>(Renderer)) {Sprite->Material=Material;Sprite->Modify();}
        Scripts.Add(System->GetSystemUpdateScript());Scripts.Add(System->GetSystemSpawnScript());
        TSet<UNiagaraGraph*> SeenGraphs;
        for(auto* Script:Scripts)if(auto* Source=Cast<UNiagaraScriptSource>(Script->GetLatestSource()))if(Source->NodeGraph && !SeenGraphs.Contains(Source->NodeGraph))
        {
            SeenGraphs.Add(Source->NodeGraph);TArray<UNiagaraNodeFunctionCall*> Calls;Source->NodeGraph->GetNodesOfClass(Calls);
            for(auto* Call:Calls)for(auto* Pin:Call->Pins)if(Pin->Direction==EGPD_Input && Pin->PinName.ToString().ToLower().Replace(TEXT(" "),TEXT("")).Contains(TEXT("loopbehavior")))
            {
                const FString Desired=Loop?TEXT("Infinite"):TEXT("Once");FString Internal=Desired;
                if(auto* Enum=Cast<UEnum>(Pin->PinType.PinSubCategoryObject.Get()))for(int32 Index=0;Index<Enum->NumEnums();Index++)if(Enum->GetDisplayNameTextByIndex(Index).ToString().Equals(Desired,ESearchCase::IgnoreCase)){Internal=Enum->GetNameStringByIndex(Index);break;}
                UE_LOG(LogMeshDrawVfx,Display,TEXT("Loop pin default %s -> %s (%s)"),*Pin->DefaultValue,*Internal,*Desired);Pin->DefaultValue=Internal;Call->MarkNodeRequiresSynchronization(TEXT("MeshDraw VFX loop settings"),true);LoopPins++;
            }
        }
    }
    System->PostEditChange();System->RequestCompile(false);System->WaitForCompilationComplete();System->MarkPackageDirty();FAssetRegistryModule::AssetCreated(System);
    if(System->GetSystemSpawnScript()->GetLastCompileStatus()==ENiagaraScriptCompileStatus::NCS_Error || System->GetSystemUpdateScript()->GetLastCompileStatus()==ENiagaraScriptCompileStatus::NCS_Error){Error=TEXT("The generated Niagara system did not compile.");return false;}
    Bundle->VfxSystem=System;Bundle->EffectSettingsJson=Json;Bundle->MarkPackageDirty();
    UE_LOG(LogMeshDrawVfx,Display,TEXT("Created Niagara VFX: %s, %d configured parameters, %d loop pins."),*Preset,Changed,LoopPins);return true;
}
