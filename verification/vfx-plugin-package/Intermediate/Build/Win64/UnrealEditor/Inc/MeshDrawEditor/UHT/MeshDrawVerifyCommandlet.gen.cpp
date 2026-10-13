// Copyright Epic Games, Inc. All Rights Reserved.
/*===========================================================================
	Generated code exported from UnrealHeaderTool.
	DO NOT modify this manually! Edit the corresponding .h files instead!
===========================================================================*/

#include "UObject/GeneratedCppIncludes.h"
#include "MeshDrawVerifyCommandlet.h"

PRAGMA_DISABLE_DEPRECATION_WARNINGS
static_assert(!UE_WITH_CONSTINIT_UOBJECT, "This generated code can only be compiled with !UE_WITH_CONSTINIT_UOBJECT");
void EmptyLinkFunctionForGeneratedCodeMeshDrawVerifyCommandlet() {}

// ********** Begin Cross Module References ********************************************************
ENGINE_API UClass* Z_Construct_UClass_UCommandlet(ETypeConstructPhase);
// ********** End Cross Module References **********************************************************

// ********** Begin Same Module References *********************************************************
UPackage* Z_Construct_UPackage__Script_MeshDrawEditor(ETypeConstructPhase);
MESHDRAWEDITOR_API UClass* Z_Construct_UClass_UMeshDrawVerifyCommandlet(ETypeConstructPhase);
MESHDRAWEDITOR_API UClass* Z_Construct_UClass_UMeshDrawVerifyCommandlet(ETypeConstructPhase);
// ********** End Same Module References ***********************************************************
#define UHT_STRUCT_BASE(INIT) UE::CodeGen::ConstInit::TCompiledInObjectPtr<const FStructBaseChain>(UE::Private::AsStructBaseChain(INIT))

// ********** Begin Class UMeshDrawVerifyCommandlet ************************************************
#ifdef UHT_STATICS
#error UHT_STATICS already defined
#endif
#define UHT_STATICS Z_Construct_UClass_UMeshDrawVerifyCommandlet_Statics
struct UHT_STATICS
{
#if WITH_METADATA
	static constexpr UECodeGen_Private::FMetaDataPairParam Type_MetaData[] = {
		{ "Comment", "/** Headless integration test for the actual importer, usable with UnrealEditor-Cmd. */" },
		{ "IncludePath", "MeshDrawVerifyCommandlet.h" },
		{ "ModuleRelativePath", "Public/MeshDrawVerifyCommandlet.h" },
		{ "ToolTip", "Headless integration test for the actual importer, usable with UnrealEditor-Cmd." },
	};
#endif // WITH_METADATA

// ********** Begin Class UMeshDrawVerifyCommandlet constinit property declarations ****************
// ********** End Class UMeshDrawVerifyCommandlet constinit property declarations ******************
	static FTypeConstructFunc* DependentSingletons[];
	static constexpr FCppClassTypeInfoStatic StaticCppClassTypeInfo = {
		TCppClassTypeTraits<UMeshDrawVerifyCommandlet>::IsAbstract,
	};
	static const UECodeGen_Private::FClassParams ClassParams;
}; // struct UHT_STATICS
FTypeConstructFunc* UHT_STATICS::DependentSingletons[] = {
	(FTypeConstructFunc*)Z_Construct_UClass_UCommandlet,
	(FTypeConstructFunc*)Z_Construct_UPackage__Script_MeshDrawEditor,
};
static_assert(UE_ARRAY_COUNT(UHT_STATICS::DependentSingletons) < 16);
const UECodeGen_Private::FClassParams UHT_STATICS::ClassParams = {
	&Z_Construct_UClass_UMeshDrawVerifyCommandlet,
	nullptr,
	&StaticCppClassTypeInfo,
	DependentSingletons,
	nullptr,
	nullptr,
	nullptr,
	UE_ARRAY_COUNT(DependentSingletons),
	0,
	0,
	0,
	0x001000A8u,
	METADATA_PARAMS(UE_ARRAY_COUNT(UHT_STATICS::Type_MetaData), UHT_STATICS::Type_MetaData)
};
FClassRegistrationInfo Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet;
UClass* Z_Construct_UClass_UMeshDrawVerifyCommandlet(ETypeConstructPhase Phase)
{
	if (Phase == ETypeConstructPhase::Inner)
	{
		using TClass = UMeshDrawVerifyCommandlet;
		if (!Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet.InnerSingleton)
		{
			GetPrivateStaticClassBody(
				TClass::StaticPackage(),
				TEXT("MeshDrawVerifyCommandlet"),
				Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet.InnerSingleton,
				nullptr,
				DataSizeOf<TClass>(),
				alignof(TClass),
				TClass::StaticClassFlags,
				TClass::StaticClassCastFlags(),
				TClass::StaticConfigName(),
				(UClass::ClassConstructorType)InternalConstructor<TClass>,
				(UClass::ClassVTableHelperCtorCallerType)InternalVTableHelperCtorCaller<TClass>,
				UOBJECT_CPPCLASS_STATICFUNCTIONS_FORCLASS(TClass),
				&TClass::Super::StaticClass,
				&TClass::WithinClass::StaticClass
			);
		}
		return Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet.InnerSingleton;
	}
	if (!Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet.OuterSingleton)
	{
		UECodeGen_Private::ConstructUClass(Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet.OuterSingleton, UHT_STATICS::ClassParams);
	}
	return Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet.OuterSingleton;
}
#undef UHT_STATICS
DEFINE_VTABLE_PTR_HELPER_CTOR_NS(, UMeshDrawVerifyCommandlet);
UMeshDrawVerifyCommandlet::~UMeshDrawVerifyCommandlet() {}
// ********** End Class UMeshDrawVerifyCommandlet **************************************************

// ********** Begin Registration *******************************************************************
#ifdef UHT_STATICS
#error UHT_STATICS already defined
#endif
#define UHT_STATICS Z_CompiledInDeferFile_FID_GameProjects_MeshDraw_verification_vfx_plugin_package_HostProject_Plugins_MeshDrawImporter_Source_MeshDrawEditor_Public_MeshDrawVerifyCommandlet_h__Script_MeshDrawEditor_Statics
struct UHT_STATICS
{
	static constexpr FClassRegisterCompiledInInfo ClassInfo[] = {
		{ Z_Construct_UClass_UMeshDrawVerifyCommandlet, TEXT("UMeshDrawVerifyCommandlet"), &Z_Registration_Info_UClass_UMeshDrawVerifyCommandlet, CONSTRUCT_RELOAD_VERSION_INFO(FClassReloadVersionInfo, sizeof(UMeshDrawVerifyCommandlet), 1535324321U) },
	};
}; // UHT_STATICS 
static FRegisterCompiledInInfo Z_CompiledInDeferFile_FID_GameProjects_MeshDraw_verification_vfx_plugin_package_HostProject_Plugins_MeshDrawImporter_Source_MeshDrawEditor_Public_MeshDrawVerifyCommandlet_h__Script_MeshDrawEditor_09541729df61e2ce20bdcd043f7cbe43c363c242{
	TEXT("/Script/MeshDrawEditor"),
	UHT_STATICS::ClassInfo, UE_ARRAY_COUNT(UHT_STATICS::ClassInfo),
	nullptr, 0,
	nullptr, 0,
	nullptr, 0,
};
#undef UHT_STATICS
// ********** End Registration *********************************************************************
#undef UHT_STRUCT_BASE

PRAGMA_ENABLE_DEPRECATION_WARNINGS
