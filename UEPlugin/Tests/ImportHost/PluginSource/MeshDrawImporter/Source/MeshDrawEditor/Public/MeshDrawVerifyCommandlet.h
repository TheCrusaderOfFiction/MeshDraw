#pragma once
#include "CoreMinimal.h"
#include "Commandlets/Commandlet.h"
#include "MeshDrawVerifyCommandlet.generated.h"
/** Headless integration test for the actual importer, usable with UnrealEditor-Cmd. */
UCLASS()
class MESHDRAWEDITOR_API UMeshDrawVerifyCommandlet : public UCommandlet
{
    GENERATED_BODY()
public:
    UMeshDrawVerifyCommandlet();
    virtual int32 Main(const FString& Params) override;
};
