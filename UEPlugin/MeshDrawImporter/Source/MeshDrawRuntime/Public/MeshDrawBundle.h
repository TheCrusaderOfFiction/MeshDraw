#pragma once
#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "MeshDrawBundle.generated.h"

class UStaticMesh;
class USkeletalMesh;
class USkeleton;
class UPhysicsAsset;
class UMaterial;
class UTexture2D;

/** The generated Unreal assets plus the original editable MeshDraw source. */
UCLASS(BlueprintType)
class MESHDRAWRUNTIME_API UMeshDrawBundle : public UDataAsset
{
    GENERATED_BODY()
public:
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="MeshDraw") FString OutputType;
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="MeshDraw") TObjectPtr<UStaticMesh> StaticMesh;
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="MeshDraw") TObjectPtr<USkeletalMesh> SkeletalMesh;
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="MeshDraw") TObjectPtr<USkeleton> Skeleton;
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="MeshDraw") TObjectPtr<UPhysicsAsset> PhysicsAsset;
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="MeshDraw") TObjectPtr<UMaterial> Material;
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="MeshDraw") TMap<FName,TObjectPtr<UTexture2D>> TextureMaps;
#if WITH_EDITORONLY_DATA
    UPROPERTY(VisibleAnywhere, Category="Source") FString SourceFilename;
    UPROPERTY(VisibleAnywhere, Category="Source", meta=(MultiLine=true)) FString LayeredProjectJson;
    UPROPERTY(VisibleAnywhere, Category="Source", meta=(MultiLine=true)) FString DrawnSkeletonJson;
    /** Exact interchange bytes, retained to recover all source files without the original disk file. */
    UPROPERTY() TArray<uint8> OriginalBundle;
#endif
};
