// Copyright Epic Games, Inc. All Rights Reserved.
/*===========================================================================
	Generated code exported from UnrealHeaderTool.
	DO NOT modify this manually! Edit the corresponding .h files instead!
===========================================================================*/

#include "UObject/GeneratedCppIncludes.h"
#include "MeshDrawBundle.h"

PRAGMA_DISABLE_DEPRECATION_WARNINGS
static_assert(!UE_WITH_CONSTINIT_UOBJECT, "This generated code can only be compiled with !UE_WITH_CONSTINIT_UOBJECT");
void EmptyLinkFunctionForGeneratedCodeMeshDrawBundle() {}

// ********** Begin Cross Module References ********************************************************
ENGINE_API UClass* Z_Construct_UClass_UDataAsset(ETypeConstructPhase);
ENGINE_API UClass* Z_Construct_UClass_UMaterial(ETypeConstructPhase);
ENGINE_API UClass* Z_Construct_UClass_UPhysicsAsset(ETypeConstructPhase);
ENGINE_API UClass* Z_Construct_UClass_USkeletalMesh(ETypeConstructPhase);
ENGINE_API UClass* Z_Construct_UClass_USkeleton(ETypeConstructPhase);
ENGINE_API UClass* Z_Construct_UClass_UStaticMesh(ETypeConstructPhase);
ENGINE_API UClass* Z_Construct_UClass_UTexture2D(ETypeConstructPhase);
NIAGARA_API UClass* Z_Construct_UClass_UNiagaraSystem(ETypeConstructPhase);
// ********** End Cross Module References **********************************************************

// ********** Begin Same Module References *********************************************************
UPackage* Z_Construct_UPackage__Script_MeshDrawRuntime(ETypeConstructPhase);
MESHDRAWRUNTIME_API UClass* Z_Construct_UClass_UMeshDrawBundle(ETypeConstructPhase);
MESHDRAWRUNTIME_API UClass* Z_Construct_UClass_UMeshDrawBundle(ETypeConstructPhase);
// ********** End Same Module References ***********************************************************
#define UHT_STRUCT_BASE(INIT) UE::CodeGen::ConstInit::TCompiledInObjectPtr<const FStructBaseChain>(UE::Private::AsStructBaseChain(INIT))

// ********** Begin Class UMeshDrawBundle **********************************************************
#ifdef UHT_STATICS
#error UHT_STATICS already defined
#endif
#define UHT_STATICS Z_Construct_UClass_UMeshDrawBundle_Statics
struct UHT_STATICS
{
#if WITH_METADATA
	static constexpr UECodeGen_Private::FMetaDataPairParam Type_MetaData[] = {
		{ "BlueprintType", "true" },
		{ "Comment", "/** The generated Unreal assets plus the original editable MeshDraw source. */" },
		{ "IncludePath", "MeshDrawBundle.h" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
		{ "ToolTip", "The generated Unreal assets plus the original editable MeshDraw source." },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_OutputType_MetaData[] = {
		{ "Category", "MeshDraw" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_StaticMesh_MetaData[] = {
		{ "Category", "MeshDraw" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_SkeletalMesh_MetaData[] = {
		{ "Category", "MeshDraw" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_Skeleton_MetaData[] = {
		{ "Category", "MeshDraw" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_PhysicsAsset_MetaData[] = {
		{ "Category", "MeshDraw" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_Material_MetaData[] = {
		{ "Category", "MeshDraw" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_VfxSystem_MetaData[] = {
		{ "Category", "MeshDraw|VFX" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_EffectSettingsJson_MetaData[] = {
		{ "Category", "MeshDraw|VFX" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
		{ "MultiLine", "TRUE" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_TextureMaps_MetaData[] = {
		{ "Category", "MeshDraw" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
#if WITH_EDITORONLY_DATA
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_SourceFilename_MetaData[] = {
		{ "Category", "Source" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_LayeredProjectJson_MetaData[] = {
		{ "Category", "Source" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
		{ "MultiLine", "TRUE" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_DrawnSkeletonJson_MetaData[] = {
		{ "Category", "Source" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
		{ "MultiLine", "TRUE" },
	};
	static constexpr UECodeGen_Private::FMetaDataPairParam NewProp_OriginalBundle_MetaData[] = {
		{ "Comment", "/** Exact interchange bytes, retained to recover all source files without the original disk file. */" },
		{ "ModuleRelativePath", "Public/MeshDrawBundle.h" },
		{ "ToolTip", "Exact interchange bytes, retained to recover all source files without the original disk file." },
	};
#endif // WITH_EDITORONLY_DATA
#endif // WITH_METADATA

// ********** Begin Class UMeshDrawBundle constinit property declarations **************************
	static const UECodeGen_Private::FStrPropertyParams NewProp_OutputType;
	static const UECodeGen_Private::FObjectPropertyParams NewProp_StaticMesh;
	static const UECodeGen_Private::FObjectPropertyParams NewProp_SkeletalMesh;
	static const UECodeGen_Private::FObjectPropertyParams NewProp_Skeleton;
	static const UECodeGen_Private::FObjectPropertyParams NewProp_PhysicsAsset;
	static const UECodeGen_Private::FObjectPropertyParams NewProp_Material;
	static const UECodeGen_Private::FObjectPropertyParams NewProp_VfxSystem;
	static const UECodeGen_Private::FStrPropertyParams NewProp_EffectSettingsJson;
	static const UECodeGen_Private::FObjectPropertyParams NewProp_TextureMaps_ValueProp;
	static const UECodeGen_Private::FNamePropertyParams NewProp_TextureMaps_Key_KeyProp;
	static const UECodeGen_Private::FMapPropertyParams NewProp_TextureMaps;
#if WITH_EDITORONLY_DATA
	static const UECodeGen_Private::FStrPropertyParams NewProp_SourceFilename;
	static const UECodeGen_Private::FStrPropertyParams NewProp_LayeredProjectJson;
	static const UECodeGen_Private::FStrPropertyParams NewProp_DrawnSkeletonJson;
	static const UECodeGen_Private::FBytePropertyParams NewProp_OriginalBundle_Inner;
	static const UECodeGen_Private::FArrayPropertyParams NewProp_OriginalBundle;
#endif // WITH_EDITORONLY_DATA
	static const UECodeGen_Private::FPropertyParamsBase* const PropPointers[];
// ********** End Class UMeshDrawBundle constinit property declarations ****************************
	static FTypeConstructFunc* DependentSingletons[];
	static constexpr FCppClassTypeInfoStatic StaticCppClassTypeInfo = {
		TCppClassTypeTraits<UMeshDrawBundle>::IsAbstract,
	};
	static const UECodeGen_Private::FClassParams ClassParams;
}; // struct UHT_STATICS

// ********** Begin Class UMeshDrawBundle Property Definitions *************************************
const UECodeGen_Private::FStrPropertyParams UHT_STATICS::NewProp_OutputType = { "OutputType", nullptr, (EPropertyFlags)0x0010000000020015, UECodeGen_Private::EPropertyGenFlags::Str, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, OutputType), METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_OutputType_MetaData), NewProp_OutputType_MetaData) };
const UECodeGen_Private::FObjectPropertyParams UHT_STATICS::NewProp_StaticMesh = { "StaticMesh", nullptr, (EPropertyFlags)0x0114000000020015, UECodeGen_Private::EPropertyGenFlags::Object | UECodeGen_Private::EPropertyGenFlags::ObjectPtr, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, StaticMesh), Z_Construct_UClass_UStaticMesh, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_StaticMesh_MetaData), NewProp_StaticMesh_MetaData) };
const UECodeGen_Private::FObjectPropertyParams UHT_STATICS::NewProp_SkeletalMesh = { "SkeletalMesh", nullptr, (EPropertyFlags)0x0114000000020015, UECodeGen_Private::EPropertyGenFlags::Object | UECodeGen_Private::EPropertyGenFlags::ObjectPtr, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, SkeletalMesh), Z_Construct_UClass_USkeletalMesh, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_SkeletalMesh_MetaData), NewProp_SkeletalMesh_MetaData) };
const UECodeGen_Private::FObjectPropertyParams UHT_STATICS::NewProp_Skeleton = { "Skeleton", nullptr, (EPropertyFlags)0x0114000000020015, UECodeGen_Private::EPropertyGenFlags::Object | UECodeGen_Private::EPropertyGenFlags::ObjectPtr, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, Skeleton), Z_Construct_UClass_USkeleton, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_Skeleton_MetaData), NewProp_Skeleton_MetaData) };
const UECodeGen_Private::FObjectPropertyParams UHT_STATICS::NewProp_PhysicsAsset = { "PhysicsAsset", nullptr, (EPropertyFlags)0x0114000000020015, UECodeGen_Private::EPropertyGenFlags::Object | UECodeGen_Private::EPropertyGenFlags::ObjectPtr, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, PhysicsAsset), Z_Construct_UClass_UPhysicsAsset, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_PhysicsAsset_MetaData), NewProp_PhysicsAsset_MetaData) };
const UECodeGen_Private::FObjectPropertyParams UHT_STATICS::NewProp_Material = { "Material", nullptr, (EPropertyFlags)0x0114000000020015, UECodeGen_Private::EPropertyGenFlags::Object | UECodeGen_Private::EPropertyGenFlags::ObjectPtr, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, Material), Z_Construct_UClass_UMaterial, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_Material_MetaData), NewProp_Material_MetaData) };
const UECodeGen_Private::FObjectPropertyParams UHT_STATICS::NewProp_VfxSystem = { "VfxSystem", nullptr, (EPropertyFlags)0x0114000000020015, UECodeGen_Private::EPropertyGenFlags::Object | UECodeGen_Private::EPropertyGenFlags::ObjectPtr, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, VfxSystem), Z_Construct_UClass_UNiagaraSystem, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_VfxSystem_MetaData), NewProp_VfxSystem_MetaData) };
const UECodeGen_Private::FStrPropertyParams UHT_STATICS::NewProp_EffectSettingsJson = { "EffectSettingsJson", nullptr, (EPropertyFlags)0x0010000000020015, UECodeGen_Private::EPropertyGenFlags::Str, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, EffectSettingsJson), METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_EffectSettingsJson_MetaData), NewProp_EffectSettingsJson_MetaData) };
const UECodeGen_Private::FObjectPropertyParams UHT_STATICS::NewProp_TextureMaps_ValueProp = { "TextureMaps", nullptr, (EPropertyFlags)0x0104000000020001, UECodeGen_Private::EPropertyGenFlags::Object | UECodeGen_Private::EPropertyGenFlags::ObjectPtr, nullptr, nullptr, 1, 1, Z_Construct_UClass_UTexture2D, METADATA_PARAMS(0, nullptr) };
const UECodeGen_Private::FNamePropertyParams UHT_STATICS::NewProp_TextureMaps_Key_KeyProp = { "TextureMaps_Key", nullptr, (EPropertyFlags)0x0100000000020001, UECodeGen_Private::EPropertyGenFlags::Name, nullptr, nullptr, 1, 0, METADATA_PARAMS(0, nullptr) };
const UECodeGen_Private::FMapPropertyParams UHT_STATICS::NewProp_TextureMaps = { "TextureMaps", nullptr, (EPropertyFlags)0x0114000000020015, UECodeGen_Private::EPropertyGenFlags::Map, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, TextureMaps), EMapPropertyFlags::None, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_TextureMaps_MetaData), NewProp_TextureMaps_MetaData) };
#if WITH_EDITORONLY_DATA
const UECodeGen_Private::FStrPropertyParams UHT_STATICS::NewProp_SourceFilename = { "SourceFilename", nullptr, (EPropertyFlags)0x0010000800020001, UECodeGen_Private::EPropertyGenFlags::Str, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, SourceFilename), METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_SourceFilename_MetaData), NewProp_SourceFilename_MetaData) };
const UECodeGen_Private::FStrPropertyParams UHT_STATICS::NewProp_LayeredProjectJson = { "LayeredProjectJson", nullptr, (EPropertyFlags)0x0010000800020001, UECodeGen_Private::EPropertyGenFlags::Str, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, LayeredProjectJson), METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_LayeredProjectJson_MetaData), NewProp_LayeredProjectJson_MetaData) };
const UECodeGen_Private::FStrPropertyParams UHT_STATICS::NewProp_DrawnSkeletonJson = { "DrawnSkeletonJson", nullptr, (EPropertyFlags)0x0010000800020001, UECodeGen_Private::EPropertyGenFlags::Str, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, DrawnSkeletonJson), METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_DrawnSkeletonJson_MetaData), NewProp_DrawnSkeletonJson_MetaData) };
const UECodeGen_Private::FBytePropertyParams UHT_STATICS::NewProp_OriginalBundle_Inner = { "OriginalBundle", nullptr, (EPropertyFlags)0x0000000800000000, UECodeGen_Private::EPropertyGenFlags::Byte, nullptr, nullptr, 1, 0, nullptr, METADATA_PARAMS(0, nullptr) };
const UECodeGen_Private::FArrayPropertyParams UHT_STATICS::NewProp_OriginalBundle = { "OriginalBundle", nullptr, (EPropertyFlags)0x0010000800000000, UECodeGen_Private::EPropertyGenFlags::Array, nullptr, nullptr, 1, STRUCT_OFFSET(UMeshDrawBundle, OriginalBundle), EArrayPropertyFlags::None, METADATA_PARAMS(UE_ARRAY_COUNT(NewProp_OriginalBundle_MetaData), NewProp_OriginalBundle_MetaData) };
#endif // WITH_EDITORONLY_DATA
const UECodeGen_Private::FPropertyParamsBase* const UHT_STATICS::PropPointers[] = {
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_OutputType,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_StaticMesh,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_SkeletalMesh,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_Skeleton,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_PhysicsAsset,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_Material,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_VfxSystem,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_EffectSettingsJson,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_TextureMaps_ValueProp,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_TextureMaps_Key_KeyProp,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_TextureMaps,
#if WITH_EDITORONLY_DATA
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_SourceFilename,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_LayeredProjectJson,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_DrawnSkeletonJson,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_OriginalBundle_Inner,
	(const UECodeGen_Private::FPropertyParamsBase*)&UHT_STATICS::NewProp_OriginalBundle,
#endif // WITH_EDITORONLY_DATA
};
static_assert(UE_ARRAY_COUNT(UHT_STATICS::PropPointers) < 2048);
// ********** End Class UMeshDrawBundle Property Definitions ***************************************
FTypeConstructFunc* UHT_STATICS::DependentSingletons[] = {
	(FTypeConstructFunc*)Z_Construct_UClass_UDataAsset,
	(FTypeConstructFunc*)Z_Construct_UPackage__Script_MeshDrawRuntime,
};
static_assert(UE_ARRAY_COUNT(UHT_STATICS::DependentSingletons) < 16);
const UECodeGen_Private::FClassParams UHT_STATICS::ClassParams = {
	&Z_Construct_UClass_UMeshDrawBundle,
	nullptr,
	&StaticCppClassTypeInfo,
	DependentSingletons,
	nullptr,
	UHT_STATICS::PropPointers,
	nullptr,
	UE_ARRAY_COUNT(DependentSingletons),
	0,
	UE_ARRAY_COUNT(UHT_STATICS::PropPointers),
	0,
	0x001000A0u,
	METADATA_PARAMS(UE_ARRAY_COUNT(UHT_STATICS::Type_MetaData), UHT_STATICS::Type_MetaData)
};
FClassRegistrationInfo Z_Registration_Info_UClass_UMeshDrawBundle;
UClass* Z_Construct_UClass_UMeshDrawBundle(ETypeConstructPhase Phase)
{
	if (Phase == ETypeConstructPhase::Inner)
	{
		using TClass = UMeshDrawBundle;
		if (!Z_Registration_Info_UClass_UMeshDrawBundle.InnerSingleton)
		{
			GetPrivateStaticClassBody(
				TClass::StaticPackage(),
				TEXT("MeshDrawBundle"),
				Z_Registration_Info_UClass_UMeshDrawBundle.InnerSingleton,
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
		return Z_Registration_Info_UClass_UMeshDrawBundle.InnerSingleton;
	}
	if (!Z_Registration_Info_UClass_UMeshDrawBundle.OuterSingleton)
	{
		UECodeGen_Private::ConstructUClass(Z_Registration_Info_UClass_UMeshDrawBundle.OuterSingleton, UHT_STATICS::ClassParams);
	}
	return Z_Registration_Info_UClass_UMeshDrawBundle.OuterSingleton;
}
#undef UHT_STATICS
UMeshDrawBundle::UMeshDrawBundle(const FObjectInitializer& ObjectInitializer) : Super(ObjectInitializer) {}
DEFINE_VTABLE_PTR_HELPER_CTOR_NS(, UMeshDrawBundle);
UMeshDrawBundle::~UMeshDrawBundle() {}
// ********** End Class UMeshDrawBundle ************************************************************

// ********** Begin Registration *******************************************************************
#ifdef UHT_STATICS
#error UHT_STATICS already defined
#endif
#define UHT_STATICS Z_CompiledInDeferFile_FID_GameProjects_MeshDraw_verification_vfx_plugin_package_HostProject_Plugins_MeshDrawImporter_Source_MeshDrawRuntime_Public_MeshDrawBundle_h__Script_MeshDrawRuntime_Statics
struct UHT_STATICS
{
	static constexpr FClassRegisterCompiledInInfo ClassInfo[] = {
		{ Z_Construct_UClass_UMeshDrawBundle, TEXT("UMeshDrawBundle"), &Z_Registration_Info_UClass_UMeshDrawBundle, CONSTRUCT_RELOAD_VERSION_INFO(FClassReloadVersionInfo, sizeof(UMeshDrawBundle), 969442769U) },
	};
}; // UHT_STATICS 
static FRegisterCompiledInInfo Z_CompiledInDeferFile_FID_GameProjects_MeshDraw_verification_vfx_plugin_package_HostProject_Plugins_MeshDrawImporter_Source_MeshDrawRuntime_Public_MeshDrawBundle_h__Script_MeshDrawRuntime_2edea7f4e54cfab17d3de4d360f8c4e8c099d971{
	TEXT("/Script/MeshDrawRuntime"),
	UHT_STATICS::ClassInfo, UE_ARRAY_COUNT(UHT_STATICS::ClassInfo),
	nullptr, 0,
	nullptr, 0,
	nullptr, 0,
};
#undef UHT_STATICS
// ********** End Registration *********************************************************************
#undef UHT_STRUCT_BASE

PRAGMA_ENABLE_DEPRECATION_WARNINGS
