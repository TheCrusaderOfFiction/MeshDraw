#pragma once
#include "CoreMinimal.h"
#include "Factories/Factory.h"
#include "MeshDrawFactory.generated.h"

UCLASS()
class MESHDRAWEDITOR_API UMeshDrawFactory : public UFactory
{
    GENERATED_BODY()
public:
    UMeshDrawFactory();
    virtual bool FactoryCanImport(const FString& Filename) override;
    virtual UObject* FactoryCreateFile(UClass* InClass,UObject* InParent,FName InName,EObjectFlags Flags,
        const FString& Filename,const TCHAR* Parms,FFeedbackContext* Warn,bool& bOutOperationCanceled) override;
};
