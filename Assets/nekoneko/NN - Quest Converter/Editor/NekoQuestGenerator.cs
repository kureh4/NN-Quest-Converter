#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace NekoNeko.QuestConverter
{
    internal static class NekoQuestGenerator
    {
        private enum MaterialSurfaceMode
        {
            Opaque,
            Cutout,
            Transparent
        }

        private static readonly string[] MainTexturePropertyNames =
        {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_BaseColorTex", "_BaseTex",
            "_AlbedoTex", "_Albedo", "_DiffuseTex", "_Diffuse", "_Texture",
            "_MainTexture", "_Main2ndTex", "_Main3rdTex"
        };

        // 変換先へ設定するメインテクスチャ名。2nd/3rdレイヤーは含めません。
        // Toon Standardには互換用の隠しプロパティが存在する場合があるため、
        // 最初に見つかった1項目だけでなく、存在する全ての別名へ設定します。
        private static readonly string[] TargetMainTexturePropertyNames =
        {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_BaseColorTex", "_BaseTex",
            "_AlbedoTex", "_Albedo", "_DiffuseTex", "_Diffuse", "_Texture",
            "_MainTexture"
        };

        private static readonly string[] EmissionTogglePropertyNames =
        {
            "_UseEmission", "_EnableEmission", "_EmissionEnabled", "_UseEmission0",
            "_EnableEmissive", "_UseEmissive", "_EmissionEnable"
        };

        private static readonly string[] EmissionTexturePropertyNames =
        {
            "_EmissionMap", "_EmissionTex", "_EmissionTexture", "_EmissiveMap",
            "_EmissiveTex"
        };

        private static readonly string[] EmissionBlendMaskPropertyNames =
        {
            "_EmissionBlendMask", "_EmissionMask", "_EmissiveMask",
            "_EmissionMaskTex", "_EmissiveMaskTex"
        };

        private static readonly string[] TargetEmissionTexturePropertyNames =
        {
            "_EmissionMap", "_EmissionTex", "_EmissionTexture", "_EmissiveMap",
            "_EmissiveTex"
        };

        private static readonly string[] EmissionColorPropertyNames =
        {
            "_EmissionColor", "_EmissionTint", "_EmissiveColor", "_EmissionColor0"
        };

        private static readonly string[] EmissionStrengthPropertyNames =
        {
            "_EmissionStrength", "_EmissionIntensity", "_EmissionPower", "_EmissiveIntensity"
        };

        private static readonly string[] MainColorPropertyNames =
        {
            "_Color", "_BaseColor", "_BaseColorTint", "_TintColor", "_MainColor",
            "_AlbedoColor", "_DiffuseColor"
        };


        private static readonly string[] MatCapTogglePropertyNames =
        {
            "_UseMatCap", "_EnableMatCap", "_MatCapEnabled", "_UseMatcap",
            "_EnableMatcap", "_MatcapEnabled"
        };

        private static readonly string[] MatCapTexturePropertyNames =
        {
            "_MatCapTex", "_MatCap", "_MatcapTex", "_Matcap",
            "_MatCapTexture", "_MatcapTexture"
        };

        private static readonly string[] MatCapColorPropertyNames =
        {
            "_MatCapColor", "_MatcapColor", "_MatCapTint", "_MatcapTint"
        };

        private static readonly string[] TargetMatCapTexturePropertyNames =
        {
            "_MatCap", "_MatCapTex", "_Matcap", "_MatcapTex",
            "_MatCapTexture", "_MatcapTexture"
        };

        private static readonly string[] MatCapSecondTexturePropertyNames =
        {
            "_MatCap2ndTex", "_MatCapSecondTex", "_SecondMatCapTex",
            "_Matcap2ndTex", "_MatcapSecondTex"
        };

        private static readonly string[] MatCapSecondColorPropertyNames =
        {
            "_MatCap2ndColor", "_MatCapSecondColor", "_SecondMatCapColor",
            "_Matcap2ndColor", "_MatcapSecondColor"
        };

        private static readonly string[] AlphaMaskTexturePropertyNames =
        {
            "_AlphaMask", "_OpacityMask", "_CutoutMask", "_TransparencyMask",
            "_AlphaTex", "_OpacityTex"
        };

        private static readonly string[] AlphaMaskTogglePropertyNames =
        {
            "_AlphaMaskMode", "_UseAlphaMask", "_EnableAlphaMask",
            "_UseOpacityMask", "_EnableOpacityMask"
        };

        private static readonly string[] NormalTexturePropertyNames =
        {
            "_BumpMap", "_NormalMap", "_NormalTex", "_Bump2ndMap",
            "_DetailNormalMap", "_MatCapBumpMap", "_MatCap2ndBumpMap"
        };

        private static readonly string[] TargetNormalTexturePropertyNames =
        {
            "_NormalMap", "_BumpMap", "_NormalTex", "_DetailNormalMap"
        };

        private static readonly string[] NormalStrengthPropertyNames =
        {
            "_BumpScale", "_NormalScale", "_NormalStrength",
            "_Bump2ndScale", "_DetailNormalMapScale", "_MatCapBumpScale"
        };

        private sealed class MaterialSnapshot
        {
            public readonly Dictionary<string, float> floats = new Dictionary<string, float>();
            public readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
            public readonly Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>();
            public readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
            public readonly Dictionary<string, Vector2> textureScales = new Dictionary<string, Vector2>();
            public readonly Dictionary<string, Vector2> textureOffsets = new Dictionary<string, Vector2>();

            public Texture primaryTexture;
            public string primaryTextureProperty = string.Empty;
            public Vector2 primaryTextureScale = Vector2.one;
            public Vector2 primaryTextureOffset = Vector2.zero;
            public Color primaryColor = Color.white;
            public string primaryColorProperty = string.Empty;
            public bool hasPrimaryColor;
        }

        internal static NekoQuestGenerationResult Generate(
            NekoQuestAnalysis analysis,
            NekoQuestGenerationOptions options,
            Action<float, string> progress)
        {
            NekoQuestGenerationResult result = new NekoQuestGenerationResult();
            if (analysis == null || !analysis.isValid || analysis.avatarRoot == null)
            {
                result.message = "先に有効なアバターを解析してください。";
                return result;
            }

            if (options == null)
            {
                options = new NekoQuestGenerationOptions();
            }

            GameObject clone = null;
            try
            {
                progress?.Invoke(0.03f, "Quest版の保存先を準備しています");
                string avatarName = SanitizeFileName(analysis.avatarRoot.name);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string outputFolder = options.outputRoot.TrimEnd('/') + "/" + avatarName + "_Quest_" + timestamp;
                EnsureAssetFolder(outputFolder);
                EnsureAssetFolder(outputFolder + "/Materials");
                EnsureAssetFolder(outputFolder + "/Textures");
                result.outputFolder = outputFolder;

                progress?.Invoke(0.10f, "元のPC版を変更せずコピーしています");
                clone = UnityEngine.Object.Instantiate(analysis.avatarRoot);
                clone.name = analysis.avatarRoot.name + "_Quest";
                clone.transform.SetParent(analysis.avatarRoot.transform.parent, true);
                clone.transform.SetSiblingIndex(analysis.avatarRoot.transform.GetSiblingIndex() + 1);
                Undo.RegisterCreatedObjectUndo(clone, "Quest版アバターを生成");
                if (PrefabUtility.IsPartOfPrefabInstance(clone))
                {
                    PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                RemoveCopiedPerPlatformOverrideComponent(clone);
                int removedPipelineManagers = RemoveCopiedPipelineManagerComponent(clone);
                if (removedPipelineManagers > 0)
                {
                    result.changes.Add("元アバターのBlueprint IDを継承しないようPipeline Managerを除去: " + removedPipelineManagers + "個");
                }

                NekoQuestLimits limits = NekoQuestLimits.ForMode(options.targetMode);
                progress?.Invoke(0.22f, "残したい揺れものを調整しています");
                ApplyPhysBonePlan(clone, analysis, result, limits);
                progress?.Invoke(0.34f, "Questで使えない機能を整理しています");
                if (options.removeUnsupportedComponents)
                {
                    RemoveUnsupportedComponents(clone, result);
                }

                if (options.trimContactsToLimit)
                {
                    TrimComponentsByPredicate(clone, NekoQuestSdkBridge.IsContact, limits.contacts, "Contact", result);
                }

                if (options.trimParticlesToLimit)
                {
                    TrimUnityComponents<ParticleSystem>(clone, limits.particles, "Particle System", result);
                    TrimUnityComponents<TrailRenderer>(clone, limits.trailRenderers, "Trail Renderer", result);
                    TrimUnityComponents<LineRenderer>(clone, limits.lineRenderers, "Line Renderer", result);
                }

                progress?.Invoke(0.48f, "Quest用マテリアルを作っています");
                if (options.convertMaterials)
                {
                    ConvertMaterialsAndTextures(clone, outputFolder, options, result, progress);
                }

                if (options.enforceAndroidUploadSize && options.duplicateAndCompressTextures)
                {
                    progress?.Invoke(0.79f, "Androidの容量上限に合わせて調整しています");
                    OptimizeAndroidTextureBudget(clone, outputFolder, options, result);
                }

                progress?.Invoke(0.82f, "Quest版Prefabを保存しています");
                if (!options.setupPerPlatformOverride)
                {
                    int removedBeforeSave = RemoveCopiedPipelineManagerComponent(clone);
                    if (removedBeforeSave > 0)
                    {
                        result.changes.Add("保存直前にPipeline Managerを再確認して除去: " + removedBeforeSave + "個");
                    }
                }

                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/" + avatarName + "_Quest.prefab");
                GameObject saved = PrefabUtility.SaveAsPrefabAssetAndConnect(clone, prefabPath, InteractionMode.UserAction);
                if (saved == null)
                {
                    throw new InvalidOperationException("Prefabの保存に失敗しました。Consoleを確認してください。");
                }

                if (!options.setupPerPlatformOverride)
                {
                    int removedAfterSave = RemoveCopiedPipelineManagerComponent(clone);
                    if (removedAfterSave > 0)
                    {
                        PrefabUtility.SaveAsPrefabAssetAndConnect(clone, prefabPath, InteractionMode.AutomatedAction);
                        result.changes.Add("Prefab保存後に再追加されたPipeline Managerを除去: " + removedAfterSave + "個");
                    }
                }

                result.generatedAvatar = clone;
                result.prefabPath = prefabPath;

                progress?.Invoke(0.90f, "PC版とQuest版を紐づけています");
                if (options.setupPerPlatformOverride)
                {
                    string overrideError;
                    result.overrideConfigured = NekoQuestSdkBridge.TryConfigureAndroidOverride(analysis.avatarRoot, clone, out overrideError);
                    if (!result.overrideConfigured && !string.IsNullOrEmpty(overrideError))
                    {
                        result.warnings.Add(overrideError);
                    }
                }

                progress?.Invoke(0.95f, "変換レポートを保存しています");
                result.reportPath = outputFolder + "/変換レポート.txt";
                WriteReport(result.reportPath, analysis, options, result);
                AssetDatabase.ImportAsset(result.reportPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (!options.keepGeneratedAvatarInScene && clone != null)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    result.generatedAvatar = null;
                }

                result.success = true;
                result.message = options.setupPerPlatformOverride
                    ? "Quest版が完成しました。元のPC版は変更されていません。"
                    : "Quest版が完成しました。Pipeline Managerを継承していないため、別アバターとして新規アップロードできます。";
                progress?.Invoke(1f, "完了しました");
                return result;
            }
            catch (Exception exception)
            {
                if (clone != null)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }

                result.success = false;
                result.message = "生成に失敗しました: " + exception.GetBaseException().Message;
                Debug.LogException(exception);
                return result;
            }
        }


        private static void RemoveCopiedPerPlatformOverrideComponent(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            Component[] rootComponents = clone.GetComponents<Component>();
            for (int i = rootComponents.Length - 1; i >= 0; i--)
            {
                Component component = rootComponents[i];
                if (component == null)
                {
                    continue;
                }

                string typeName = component.GetType().FullName ?? string.Empty;
                if (typeName.IndexOf("PerPlatformOverride", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }
        }

        private static int RemoveCopiedPipelineManagerComponent(GameObject clone)
        {
            if (clone == null)
            {
                return 0;
            }

            Component[] components = clone.GetComponentsInChildren<Component>(true);
            int removed = 0;
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                string fullName = type.FullName ?? string.Empty;
                bool isPipelineManager =
                    string.Equals(fullName, "VRC.Core.PipelineManager", StringComparison.Ordinal) ||
                    string.Equals(fullName, "VRC.SDKBase.PipelineManager", StringComparison.Ordinal) ||
                    (string.Equals(type.Name, "PipelineManager", StringComparison.Ordinal) &&
                     fullName.StartsWith("VRC.", StringComparison.Ordinal));

                if (!isPipelineManager)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(component);
                removed++;
            }

            return removed;
        }

        private static void ApplyPhysBonePlan(GameObject clone, NekoQuestAnalysis analysis, NekoQuestGenerationResult result, NekoQuestLimits limits)
        {
            int removed = 0;
            int collisionCleared = 0;

            for (int i = 0; i < analysis.physBones.Count; i++)
            {
                NekoPhysBoneItem item = analysis.physBones[i];
                Component component = NekoQuestSdkBridge.FindMatchingComponent(clone, item);
                if (component == null)
                {
                    result.warnings.Add("揺れものを見つけられませんでした: " + item.displayName);
                    continue;
                }

                if (!item.keep)
                {
                    UnityEngine.Object.DestroyImmediate(component);
                    removed++;
                    continue;
                }

                if (!item.keepCollisions && item.colliderCount > 0)
                {
                    if (NekoQuestSdkBridge.ClearPhysBoneColliders(component))
                    {
                        collisionCleared++;
                    }
                    else
                    {
                        result.warnings.Add("当たり判定を軽量化できませんでした: " + item.displayName);
                    }
                }
            }

            RemoveUnusedPhysBoneCollidersIfNeeded(clone, limits, result);

            if (removed > 0)
            {
                result.changes.Add("優先度の低い揺れものをQuest版で停止: " + removed + "個");
            }
            if (collisionCleared > 0)
            {
                result.changes.Add("揺れを残したまま当たり判定を軽量化: " + collisionCleared + "個");
            }
        }

        private static void RemoveUnusedPhysBoneCollidersIfNeeded(GameObject clone, NekoQuestLimits limits, NekoQuestGenerationResult result)
        {
            List<Component> allColliders = NekoQuestSdkBridge.GetComponentsByTypeName(clone, NekoQuestSdkBridge.IsPhysBoneCollider);
            if (allColliders.Count <= limits.physBoneColliders)
            {
                return;
            }

            HashSet<Component> referenced = new HashSet<Component>();
            List<Component> physBones = NekoQuestSdkBridge.GetComponentsByTypeName(clone, NekoQuestSdkBridge.IsPhysBone);
            for (int i = 0; i < physBones.Count; i++)
            {
                List<Component> colliders = NekoQuestSdkBridge.GetPhysBoneColliders(physBones[i]);
                for (int c = 0; c < colliders.Count; c++)
                {
                    referenced.Add(colliders[c]);
                }
            }

            int keepBudget = limits.physBoneColliders;
            HashSet<Component> keep = new HashSet<Component>();
            foreach (Component collider in referenced)
            {
                if (keep.Count >= keepBudget)
                {
                    break;
                }
                keep.Add(collider);
            }

            for (int i = 0; i < allColliders.Count && keep.Count < keepBudget; i++)
            {
                keep.Add(allColliders[i]);
            }

            int removed = 0;
            for (int i = 0; i < allColliders.Count; i++)
            {
                if (keep.Contains(allColliders[i]))
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(allColliders[i]);
                removed++;
            }

            if (removed > 0)
            {
                result.changes.Add("PhysBone Colliderを上限内へ整理: " + removed + "個削除");
            }
        }

        private static void RemoveUnsupportedComponents(GameObject clone, NekoQuestGenerationResult result)
        {
            Component[] components = clone.GetComponentsInChildren<Component>(true);
            int removed = 0;
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null || component is Transform || component is Animator)
                {
                    continue;
                }

                Type type = component.GetType();
                bool unsupported = component is Light || component is Camera || component is AudioSource ||
                                   component is Cloth || component is Rigidbody || component is Collider ||
                                   component is Joint || component is IConstraint ||
                                   NekoQuestSdkBridge.IsDynamicBone(type) || NekoQuestSdkBridge.IsFinalIk(type);

                if (!unsupported || NekoQuestSdkBridge.IsVrcConstraint(type))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(component);
                removed++;
            }

            if (removed > 0)
            {
                result.changes.Add("Questで使用できないコンポーネントをコピーから除去: " + removed + "個");
            }
        }

        private static void TrimComponentsByPredicate(GameObject clone, Predicate<Type> predicate, int limit, string label, NekoQuestGenerationResult result)
        {
            List<Component> components = NekoQuestSdkBridge.GetComponentsByTypeName(clone, predicate);
            if (components.Count <= limit)
            {
                return;
            }

            int removed = 0;
            for (int i = components.Count - 1; i >= limit; i--)
            {
                UnityEngine.Object.DestroyImmediate(components[i]);
                removed++;
            }

            result.changes.Add(label + "を目標上限まで整理: " + removed + "個削除");
            result.warnings.Add(label + "の一部を自動で除去しました。ギミックの動作をUnity上で確認してください。");
        }

        private static void TrimUnityComponents<T>(GameObject clone, int limit, string label, NekoQuestGenerationResult result)
            where T : Component
        {
            T[] components = clone.GetComponentsInChildren<T>(true);
            if (components.Length <= limit)
            {
                return;
            }

            int removed = 0;
            for (int i = components.Length - 1; i >= limit; i--)
            {
                if (components[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(components[i]);
                    removed++;
                }
            }

            if (removed > 0)
            {
                result.changes.Add(label + "を目標上限まで整理: " + removed + "個削除");
            }
        }

        private static void ConvertMaterialsAndTextures(
            GameObject clone,
            string outputFolder,
            NekoQuestGenerationOptions options,
            NekoQuestGenerationResult result,
            Action<float, string> progress)
        {
            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
            Dictionary<Texture, Texture> textureMap = new Dictionary<Texture, Texture>();
            int convertedMaterials = 0;
            int copiedTextures = 0;

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                Material[] sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    continue;
                }

                Material[] questMaterials = new Material[sourceMaterials.Length];
                for (int m = 0; m < sourceMaterials.Length; m++)
                {
                    Material source = sourceMaterials[m];
                    if (source == null)
                    {
                        questMaterials[m] = null;
                        continue;
                    }

                    Material quest;
                    if (!materialMap.TryGetValue(source, out quest))
                    {
                        MaterialSnapshot snapshot = CaptureMaterial(source);
                        MaterialSurfaceMode surfaceMode = DetectSurfaceMode(source);
                        bool sourceEmissionEnabled = IsSourceEmissionEnabled(source, snapshot);
                        bool sourceMatCapEnabled = IsSourceMatCapEnabled(source, snapshot);
                        bool matCapDominant = sourceMatCapEnabled && snapshot.primaryTexture == null &&
                                              surfaceMode == MaterialSurfaceMode.Opaque;
                        bool sourceIsMobile = NekoQuestAnalyzer.IsMobileMaterial(source);
                        bool isParticleMaterial = renderer is ParticleSystemRenderer;
                        Shader targetShader = sourceIsMobile
                            ? source.shader
                            : FindBestMobileShader(source, surfaceMode, matCapDominant, isParticleMaterial);

                        if (!sourceIsMobile && targetShader != null)
                        {
                            // 元シェーダーの保存済みプロパティを持ち越すと、同名で型が異なる
                            // プロパティ（例: _ColorMask）が競合するため、変換先シェーダーから
                            // 新しいマテリアルを作成します。
                            quest = CreateMaterialAssetWithShader(source, targetShader, outputFolder + "/Materials");
                            RestoreMaterial(quest, snapshot);
                            ApplySurfaceSettings(source, quest, surfaceMode);
                            convertedMaterials++;
                        }
                        else
                        {
                            quest = CreateMaterialAssetFromSource(source, outputFolder + "/Materials");
                            if (!sourceIsMobile && targetShader == null)
                            {
                                result.warnings.Add("VRChat/Mobileシェーダーが見つからず変換できませんでした: " + source.name);
                            }
                        }

                        quest.enableInstancing = true;

                        Texture bakedPrimaryTexture = TryBakePrimaryTexture(
                            source,
                            snapshot,
                            outputFolder + "/Textures",
                            options,
                            surfaceMode,
                            result);

                        if (bakedPrimaryTexture == null && snapshot.primaryTexture == null)
                        {
                            bakedPrimaryTexture = TryCreateFallbackPrimaryTexture(
                                source,
                                snapshot,
                                outputFolder + "/Textures",
                                options,
                                surfaceMode,
                                sourceMatCapEnabled,
                                result);
                        }

                        Texture bakedMatCapTexture = TryBakeMatCapTexture(
                            source,
                            snapshot,
                            outputFolder + "/Textures",
                            options,
                            result);

                        Texture bakedEmissionTexture = TryBakeEmissionTexture(
                            source,
                            snapshot,
                            outputFolder + "/Textures",
                            options,
                            sourceEmissionEnabled,
                            result);

                        if (options.duplicateAndCompressTextures)
                        {
                            copiedTextures += DuplicateMaterialTextures(
                                quest,
                                snapshot,
                                outputFolder + "/Textures",
                                options,
                                textureMap,
                                result,
                                bakedPrimaryTexture != null);
                        }

                        if (bakedPrimaryTexture != null)
                        {
                            ApplyPrimaryTexture(quest, bakedPrimaryTexture, snapshot.primaryTextureScale, snapshot.primaryTextureOffset);
                            SetPrimaryColorWhite(quest);
                        }

                        bool sourceHasEmissionMask = FindSnapshotTexture(snapshot, EmissionBlendMaskPropertyNames) != null;
                        bool emissionTextureApplied = false;
                        if (bakedEmissionTexture != null)
                        {
                            emissionTextureApplied = ApplyEmissionTexture(quest, bakedEmissionTexture);
                            if (!emissionTextureApplied)
                            {
                                result.warnings.Add("Quest用シェーダーへ発光テクスチャを設定できませんでした: " + source.name);
                            }
                        }

                        bool matCapApplied = ConfigureTargetMatCap(
                            source,
                            quest,
                            snapshot,
                            outputFolder + "/Textures",
                            options,
                            textureMap,
                            result,
                            ref copiedTextures,
                            matCapDominant,
                            bakedMatCapTexture);
                        if (sourceMatCapEnabled && !matCapApplied)
                        {
                            result.warnings.Add("MatCapをQuest用マテリアルへ設定できませんでした: " + source.name);
                        }

                        bool normalApplied = ConfigureTargetNormalMap(
                            source,
                            quest,
                            snapshot,
                            outputFolder + "/Textures",
                            options,
                            textureMap,
                            result,
                            ref copiedTextures);
                        if (HasEnabledSourceNormalMap(source, snapshot) && !normalApplied)
                        {
                            result.warnings.Add("ノーマルマップをQuest用マテリアルへ設定できませんでした: " + source.name);
                        }

                        ConfigureTargetCull(source, quest);
                        ConfigureTargetRim(source, quest, snapshot);
                        EnsurePrimaryMaterialAppearance(source, quest, snapshot, result);

                        // lilToonなどの発光マスクを無視して元のEmission Mapだけを渡すと、
                        // 瞳や顔全体が白く発光する場合があります。マスクの焼き込みに失敗した場合は、
                        // 見た目の破綻を避けるためQuest版の発光を無効化します。
                        bool safeEmissionEnabled = sourceEmissionEnabled && (!sourceHasEmissionMask || emissionTextureApplied);
                        ConfigureTargetEmission(source, quest, snapshot, safeEmissionEnabled, result);
                        if (sourceEmissionEnabled && sourceHasEmissionMask && !safeEmissionEnabled)
                        {
                            result.warnings.Add("発光マスクを安全に変換できなかったため、Quest版では発光を無効化しました: " + source.name);
                        }
                        EditorUtility.SetDirty(quest);
                        materialMap[source] = quest;
                    }

                    questMaterials[m] = quest;
                }

                renderer.sharedMaterials = questMaterials;
                EditorUtility.SetDirty(renderer);
                progress?.Invoke(0.50f + 0.28f * ((float)(r + 1) / Mathf.Max(1, renderers.Length)),
                    "マテリアルを変換中 " + (r + 1) + " / " + renderers.Length);
            }

            if (convertedMaterials > 0)
            {
                result.changes.Add("Quest対応シェーダーへ変換: " + convertedMaterials + "マテリアル");
            }
            if (copiedTextures > 0)
            {
                result.changes.Add("PC版と分離したQuest用テクスチャを作成・圧縮: " + copiedTextures + "枚");
            }
        }


        private sealed class GeneratedTextureBudgetItem
        {
            public Texture texture;
            public string assetPath;
            public TextureImporter importer;
            public int currentMaxSize;
            public int minimumMaxSize;
            public int priority;
            public long estimatedBytes;
        }

        private static void OptimizeAndroidTextureBudget(
            GameObject clone,
            string outputFolder,
            NekoQuestGenerationOptions options,
            NekoQuestGenerationResult result)
        {
            if (clone == null || string.IsNullOrEmpty(outputFolder))
            {
                return;
            }

            Dictionary<string, GeneratedTextureBudgetItem> itemsByPath = new Dictionary<string, GeneratedTextureBudgetItem>(StringComparer.OrdinalIgnoreCase);
            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] materials = renderers[r].sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (int m = 0; m < materials.Length; m++)
                {
                    CollectGeneratedMaterialTextures(materials[m], outputFolder, itemsByPath);
                }
            }

            List<GeneratedTextureBudgetItem> items = itemsByPath.Values.ToList();
            if (items.Count == 0)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                RefreshTextureBudgetItem(items[i]);
            }

            long targetBytes = GetAndroidTextureBudget(options.targetMode);
            long beforeBytes = items.Sum(item => item.estimatedBytes);
            long totalBytes = beforeBytes;
            int adjustedCount = 0;

            // 低優先度（マスク・ノーマル・MatCapなど）から順に半分へ縮小します。
            // 同じ優先度では、削減効果の大きいテクスチャを先に処理します。
            int safety = 0;
            while (totalBytes > targetBytes && safety++ < 256)
            {
                GeneratedTextureBudgetItem candidate = items
                    .Where(item => item.importer != null && item.currentMaxSize > item.minimumMaxSize)
                    .OrderBy(item => item.priority)
                    .ThenByDescending(item => item.estimatedBytes)
                    .FirstOrDefault();

                if (candidate == null)
                {
                    break;
                }

                int nextSize = Mathf.Max(candidate.minimumMaxSize, candidate.currentMaxSize / 2);
                if (nextSize >= candidate.currentMaxSize)
                {
                    candidate.minimumMaxSize = candidate.currentMaxSize;
                    continue;
                }

                ApplyAndroidTextureSize(candidate.importer, nextSize);
                candidate.currentMaxSize = nextSize;
                candidate.estimatedBytes = EstimateAndroidTextureBytes(candidate.texture, candidate.importer, nextSize);
                adjustedCount++;
                totalBytes = items.Sum(item => item.estimatedBytes);
            }

            // SDKの10MBダウンロード上限は、テクスチャの非圧縮推定値だけでは正確に予測できません。
            // そのため40MB対策の後に、顔・瞳を避けながら低優先度テクスチャをASTC 8x8へ変更し、
            // AssetBundle圧縮後にも余裕が残るよう追加の安全マージンを確保します。
            long beforeDownloadSafetyBytes = totalBytes;
            int downloadSafetyAdjustedCount = ApplyAndroidDownloadSizeSafety(
                items,
                options.targetMode,
                ref totalBytes);

            // SDKの非圧縮サイズにはメッシュ、BlendShape、アニメーションなども含まれます。
            // テクスチャ推定だけで40MBぎりぎりを狙うと数百KBの差で失敗するため、
            // 顔・瞳・髪・肌を避けて、低優先度画像から追加の安全余裕を確保します。
            long beforeUncompressedSafetyBytes = totalBytes;
            int uncompressedSafetyAdjustedCount = ApplyAndroidUncompressedSizeSafety(
                items,
                options.targetMode,
                ref totalBytes);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (adjustedCount > 0)
            {
                result.changes.Add(
                    "Android非圧縮40MB対策: Quest用テクスチャ" + adjustedCount +
                    "件を段階的に縮小（推定 " + FormatBytes(beforeBytes) + " → " + FormatBytes(beforeDownloadSafetyBytes) + "）");
            }

            if (downloadSafetyAdjustedCount > 0)
            {
                result.changes.Add(
                    "Androidダウンロード10MB対策: 低優先度テクスチャ" + downloadSafetyAdjustedCount +
                    "件を追加圧縮（推定 " + FormatBytes(beforeDownloadSafetyBytes) + " → " + FormatBytes(beforeUncompressedSafetyBytes) + "）");
            }

            if (uncompressedSafetyAdjustedCount > 0)
            {
                result.changes.Add(
                    "Android非圧縮サイズの最終安全調整: 低優先度テクスチャ" + uncompressedSafetyAdjustedCount +
                    "件を追加縮小（推定 " + FormatBytes(beforeUncompressedSafetyBytes) + " → " + FormatBytes(totalBytes) + "）");
            }

            if (totalBytes > targetBytes)
            {
                result.warnings.Add(
                    "テクスチャを安全範囲まで縮小しましたが、推定容量が目標を超えています（" +
                    FormatBytes(totalBytes) + " / " + FormatBytes(targetBytes) +
                    "）。メッシュ数・ポリゴン数・アニメーションも容量に含まれるため、SDKのBuild結果を確認してください。");
            }
            else
            {
                result.changes.Add(
                    "Android用テクスチャの推定容量を安全目標内に調整: " +
                    FormatBytes(totalBytes) + " / " + FormatBytes(targetBytes));
            }
        }

        private static int ApplyAndroidDownloadSizeSafety(
            List<GeneratedTextureBudgetItem> items,
            NekoQuestTargetMode mode,
            ref long totalBytes)
        {
            if (items == null || items.Count == 0)
            {
                return 0;
            }

            // 10.00MBぎりぎりではSDKやAssetBundleの差で失敗しやすいため、
            // ASTCの推定値をさらに1～2MB程度削り、ビルド結果に余裕を持たせます。
            long targetSavings;
            switch (mode)
            {
                case NekoQuestTargetMode.Appearance:
                    targetSavings = 2L * 1024L * 1024L;
                    break;
                case NekoQuestTargetMode.Lightweight:
                    targetSavings = 1L * 1024L * 1024L;
                    break;
                default:
                    targetSavings = 1536L * 1024L;
                    break;
            }

            long savedBytes = 0L;
            int adjustedCount = 0;

            // マスク・ノーマル・MatCap・服などから処理し、顔と瞳は対象外にします。
            List<GeneratedTextureBudgetItem> candidates = items
                .Where(item => item != null && item.importer != null && item.priority <= 2)
                .OrderBy(item => item.priority)
                .ThenByDescending(item => item.estimatedBytes)
                .ToList();

            for (int i = 0; i < candidates.Count && savedBytes < targetSavings; i++)
            {
                GeneratedTextureBudgetItem candidate = candidates[i];
                TextureImporterPlatformSettings android = candidate.importer.GetPlatformTextureSettings("Android");
                if (android.overridden && android.format == TextureImporterFormat.ASTC_8x8)
                {
                    continue;
                }

                long before = candidate.estimatedBytes;
                ApplyAndroidTextureFormat(candidate.importer, TextureImporterFormat.ASTC_8x8);
                candidate.estimatedBytes = EstimateAndroidTextureBytes(
                    candidate.texture,
                    candidate.importer,
                    candidate.currentMaxSize);

                long delta = Math.Max(0L, before - candidate.estimatedBytes);
                if (delta <= 0L)
                {
                    continue;
                }

                savedBytes += delta;
                adjustedCount++;
            }

            // ASTC 8x8だけで余裕を作れない場合は、低優先度画像だけをもう1段階縮小します。
            // 顔・瞳・髪・肌はこの追加縮小の対象にしません。
            if (savedBytes < targetSavings)
            {
                List<GeneratedTextureBudgetItem> resizeCandidates = items
                    .Where(item => item != null && item.importer != null && item.priority <= 1 && item.currentMaxSize > 128)
                    .OrderBy(item => item.priority)
                    .ThenByDescending(item => item.estimatedBytes)
                    .ToList();

                for (int i = 0; i < resizeCandidates.Count && savedBytes < targetSavings; i++)
                {
                    GeneratedTextureBudgetItem candidate = resizeCandidates[i];
                    int nextSize = Mathf.Max(128, candidate.currentMaxSize / 2);
                    if (nextSize >= candidate.currentMaxSize)
                    {
                        continue;
                    }

                    long before = candidate.estimatedBytes;
                    ApplyAndroidTextureSizeAndFormat(
                        candidate.importer,
                        nextSize,
                        TextureImporterFormat.ASTC_8x8);
                    candidate.currentMaxSize = nextSize;
                    candidate.estimatedBytes = EstimateAndroidTextureBytes(
                        candidate.texture,
                        candidate.importer,
                        nextSize);

                    long delta = Math.Max(0L, before - candidate.estimatedBytes);
                    if (delta <= 0L)
                    {
                        continue;
                    }

                    savedBytes += delta;
                    adjustedCount++;
                }
            }

            totalBytes = items.Sum(item => item != null ? item.estimatedBytes : 0L);
            return adjustedCount;
        }

        private static int ApplyAndroidUncompressedSizeSafety(
            List<GeneratedTextureBudgetItem> items,
            NekoQuestTargetMode mode,
            ref long totalBytes)
        {
            if (items == null || items.Count == 0)
            {
                return 0;
            }

            // 実ビルドではテクスチャ以外のデータ量に個体差があるため、
            // 40MB上限に対して最低1.5～2MB程度の追加余裕を作ります。
            long targetSavings;
            switch (mode)
            {
                case NekoQuestTargetMode.Lightweight:
                    targetSavings = 1536L * 1024L;
                    break;
                default:
                    targetSavings = 2L * 1024L * 1024L;
                    break;
            }

            long savedBytes = 0L;
            int adjustedCount = 0;

            // マスク、ノーマル、MatCap、服、小物などを優先します。
            // 顔・瞳（priority 3）と髪・肌（priority 2）はこの最終処理では縮小しません。
            List<GeneratedTextureBudgetItem> candidates = items
                .Where(item => item != null && item.importer != null && item.priority <= 1 && item.currentMaxSize > 128)
                .OrderBy(item => item.priority)
                .ThenByDescending(item => item.estimatedBytes)
                .ToList();

            for (int i = 0; i < candidates.Count && savedBytes < targetSavings; i++)
            {
                GeneratedTextureBudgetItem candidate = candidates[i];
                int nextSize = Mathf.Max(128, candidate.currentMaxSize / 2);
                if (nextSize >= candidate.currentMaxSize)
                {
                    continue;
                }

                long before = candidate.estimatedBytes;
                ApplyAndroidTextureSizeAndFormat(
                    candidate.importer,
                    nextSize,
                    TextureImporterFormat.ASTC_8x8);
                candidate.currentMaxSize = nextSize;
                candidate.estimatedBytes = EstimateAndroidTextureBytes(
                    candidate.texture,
                    candidate.importer,
                    nextSize);

                long delta = Math.Max(0L, before - candidate.estimatedBytes);
                if (delta <= 0L)
                {
                    continue;
                }

                savedBytes += delta;
                adjustedCount++;
            }

            totalBytes = items.Sum(item => item != null ? item.estimatedBytes : 0L);
            return adjustedCount;
        }

        private static void ApplyAndroidTextureFormat(
            TextureImporter importer,
            TextureImporterFormat format)
        {
            if (importer == null)
            {
                return;
            }

            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = false;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            if (android.maxTextureSize <= 0)
            {
                android.maxTextureSize = importer.maxTextureSize;
            }
            android.format = format;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void ApplyAndroidTextureSizeAndFormat(
            TextureImporter importer,
            int maxSize,
            TextureImporterFormat format)
        {
            if (importer == null)
            {
                return;
            }

            importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, maxSize);
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = false;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = maxSize;
            android.format = format;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void CollectGeneratedMaterialTextures(
            Material material,
            string outputFolder,
            Dictionary<string, GeneratedTextureBudgetItem> itemsByPath)
        {
            if (material == null || material.shader == null)
            {
                return;
            }

            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }

                string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                Texture texture = null;
                try
                {
                    texture = material.GetTexture(propertyName);
                }
                catch
                {
                    texture = null;
                }

                if (texture == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(path) ||
                    !path.StartsWith(outputFolder + "/Textures/", StringComparison.OrdinalIgnoreCase) ||
                    itemsByPath.ContainsKey(path))
                {
                    continue;
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                int priority = GetTextureBudgetPriority(texture.name, propertyName);
                int minimumSize = GetTextureBudgetMinimumSize(priority);
                itemsByPath[path] = new GeneratedTextureBudgetItem
                {
                    texture = texture,
                    assetPath = path,
                    importer = importer,
                    priority = priority,
                    minimumMaxSize = minimumSize
                };
            }
        }

        private static int GetTextureBudgetPriority(string textureName, string propertyName)
        {
            string combined = ((textureName ?? string.Empty) + " " + (propertyName ?? string.Empty)).ToLowerInvariant();
            if (combined.Contains("mask") || combined.Contains("normal") || combined.Contains("bump") ||
                combined.Contains("matcap") || combined.Contains("ramp") || combined.Contains("shadow") ||
                combined.Contains("outline") || combined.Contains("noise") || combined.Contains("gradation") ||
                combined.Contains("detail") || combined.Contains("metallic") || combined.Contains("smoothness"))
            {
                return 0;
            }

            if (combined.Contains("face") || combined.Contains("eye") || combined.Contains("iris") ||
                combined.Contains("顔") || combined.Contains("目") || combined.Contains("瞳"))
            {
                return 3;
            }

            if (combined.Contains("hair") || combined.Contains("head") || combined.Contains("body") ||
                combined.Contains("skin") || combined.Contains("髪") || combined.Contains("頭") ||
                combined.Contains("肌"))
            {
                return 2;
            }

            return 1;
        }

        private static int GetTextureBudgetMinimumSize(int priority)
        {
            switch (priority)
            {
                case 3:
                    return 1024;
                case 2:
                    return 512;
                default:
                    return 256;
            }
        }

        private static long GetAndroidTextureBudget(NekoQuestTargetMode mode)
        {
            switch (mode)
            {
                case NekoQuestTargetMode.Appearance:
                    return 19L * 1024L * 1024L;
                case NekoQuestTargetMode.Lightweight:
                    return 10L * 1024L * 1024L;
                default:
                    return 15L * 1024L * 1024L;
            }
        }

        private static void RefreshTextureBudgetItem(GeneratedTextureBudgetItem item)
        {
            if (item == null || item.importer == null)
            {
                return;
            }

            TextureImporterPlatformSettings android = item.importer.GetPlatformTextureSettings("Android");
            item.currentMaxSize = android.overridden && android.maxTextureSize > 0
                ? android.maxTextureSize
                : item.importer.maxTextureSize;
            item.currentMaxSize = Mathf.Max(32, item.currentMaxSize);
            item.minimumMaxSize = Mathf.Min(item.currentMaxSize, item.minimumMaxSize);
            item.estimatedBytes = EstimateAndroidTextureBytes(item.texture, item.importer, item.currentMaxSize);
        }

        private static void ApplyAndroidTextureSize(TextureImporter importer, int maxSize)
        {
            if (importer == null)
            {
                return;
            }

            importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, maxSize);
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = false;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = maxSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static long EstimateAndroidTextureBytes(Texture texture, TextureImporter importer, int maxSize)
        {
            if (texture == null)
            {
                return 0L;
            }

            int width = Mathf.Max(1, texture.width);
            int height = Mathf.Max(1, texture.height);
            int largest = Mathf.Max(width, height);
            if (largest > maxSize)
            {
                float scale = (float)maxSize / largest;
                width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            }

            int astcBlockSize = 6;
            if (importer != null)
            {
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                if (android.overridden && android.format == TextureImporterFormat.ASTC_8x8)
                {
                    astcBlockSize = 8;
                }
            }

            long bytes = 0L;
            bool mipmaps = importer != null && importer.mipmapEnabled;
            while (true)
            {
                int blocksWide = Mathf.Max(1, (width + astcBlockSize - 1) / astcBlockSize);
                int blocksHigh = Mathf.Max(1, (height + astcBlockSize - 1) / astcBlockSize);
                bytes += (long)blocksWide * blocksHigh * 16L;

                if (!mipmaps || (width == 1 && height == 1))
                {
                    break;
                }

                width = Mathf.Max(1, width / 2);
                height = Mathf.Max(1, height / 2);
            }

            if (texture is Cubemap)
            {
                bytes *= 6L;
            }
            else if (texture is Texture2DArray)
            {
                bytes *= Mathf.Max(1, ((Texture2DArray)texture).depth);
            }

            return bytes;
        }

        private static Material CreateMaterialAssetFromSource(Material source, string folder)
        {
            string fileName = SanitizeFileName(source.name) + "_Quest.mat";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);
            Material copy = new Material(source) { name = source.name + "_Quest" };
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        private static Material CreateMaterialAssetWithShader(Material source, Shader targetShader, string folder)
        {
            string fileName = SanitizeFileName(source.name) + "_Quest.mat";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);
            Material material = new Material(targetShader)
            {
                name = source.name + "_Quest",
                shaderKeywords = Array.Empty<string>(),
                renderQueue = -1,
                enableInstancing = source.enableInstancing,
                doubleSidedGI = source.doubleSidedGI,
                globalIlluminationFlags = source.globalIlluminationFlags
            };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Shader FindBestMobileShader(
            Material source,
            MaterialSurfaceMode surfaceMode,
            bool matCapDominant,
            bool isParticleMaterial)
        {
            string[] candidates;

            if (isParticleMaterial)
            {
                // Questアバターで使用できるParticleシェーダーは、現在
                // Particles/Additive と Particles/Multiply の2種類です。
                // 旧Alpha Blendedなどは名前がVRChat/Mobile配下でもSDKに拒否されます。
                bool multiply = ShouldUseMultiplyParticleShader(source);
                candidates = multiply
                    ? new[]
                    {
                        "VRChat/Mobile/Particles/Multiply",
                        "VRChat/Mobile/Particles/Additive",
                        "VRChat/Mobile/Toon Standard"
                    }
                    : new[]
                    {
                        "VRChat/Mobile/Particles/Additive",
                        "VRChat/Mobile/Particles/Multiply",
                        "VRChat/Mobile/Toon Standard"
                    };
            }
            else if (matCapDominant)
            {
                // メイン画像を持たずMatCapだけで金属・宝石などの見た目を作る素材は、
                // Toon Standardへ白色だけを渡すと真っ白になります。MatCap Litを優先します。
                candidates = new[]
                {
                    "VRChat/Mobile/MatCap Lit",
                    "VRChat/Mobile/Toon Standard",
                    "VRChat/Mobile/Toon Lit",
                    "VRChat/Mobile/Standard Lite",
                    "VRChat/Mobile/Diffuse"
                };
            }
            else
            {
                // Cutout・Transparentを含む通常メッシュも、現在のQuestアバターで
                // 許可されているToon Standardを優先します。旧Unlit Transparentや
                // Toon Lit (Cutout)はSDKの許可リスト外なので選びません。
                candidates = new[]
                {
                    "VRChat/Mobile/Toon Standard",
                    "VRChat/Mobile/Toon Lit",
                    "VRChat/Mobile/Standard Lite",
                    "VRChat/Mobile/Diffuse"
                };
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                Shader shader = Shader.Find(candidates[i]);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static bool ShouldUseMultiplyParticleShader(Material source)
        {
            if (source == null)
            {
                return false;
            }

            string shaderName = source.shader != null ? source.shader.name : string.Empty;
            string materialName = source.name ?? string.Empty;
            string combined = (shaderName + " " + materialName).ToLowerInvariant();

            if (combined.Contains("multiply") || combined.Contains("smoke") ||
                combined.Contains("shadow") || combined.Contains("dark") ||
                combined.Contains("kage") || combined.Contains("kemuri") ||
                combined.Contains("影") || combined.Contains("煙"))
            {
                return true;
            }

            if (combined.Contains("additive") || combined.Contains("glow") ||
                combined.Contains("light") || combined.Contains("spark") ||
                combined.Contains("kirakira") || combined.Contains("発光"))
            {
                return false;
            }

            // Alpha BlendedはMultiplyへ置き換えると色が極端に暗くなることが多いため、
            // 判断できない場合はAdditiveを安全側の既定値にします。
            return false;
        }

        private static bool HasEnabledAlphaMask(Material material)
        {
            if (material == null)
            {
                return false;
            }

            Texture alphaMask = null;
            for (int i = 0; i < AlphaMaskTexturePropertyNames.Length; i++)
            {
                string propertyName = AlphaMaskTexturePropertyNames[i];
                if (!material.HasProperty(propertyName))
                {
                    continue;
                }

                try
                {
                    alphaMask = material.GetTexture(propertyName);
                }
                catch
                {
                    alphaMask = null;
                }

                if (alphaMask != null)
                {
                    break;
                }
            }

            if (alphaMask == null)
            {
                return false;
            }

            for (int i = 0; i < AlphaMaskTogglePropertyNames.Length; i++)
            {
                string propertyName = AlphaMaskTogglePropertyNames[i];
                if (!material.HasProperty(propertyName))
                {
                    continue;
                }

                try
                {
                    return material.GetFloat(propertyName) > 0.0001f;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasExplicitTransparency(Material material)
        {
            if (material == null)
            {
                return false;
            }

            if (material.HasProperty("_TransparentMode"))
            {
                try
                {
                    if (material.GetFloat("_TransparentMode") >= 2f)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Continue with other indicators.
                }
            }

            if (material.HasProperty("_Mode"))
            {
                try
                {
                    if (material.GetFloat("_Mode") >= 2f)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Continue with other indicators.
                }
            }

            string shaderName = material.shader != null ? material.shader.name.ToLowerInvariant() : string.Empty;
            return shaderName.Contains("transparent") || shaderName.Contains("fade");
        }

        private static MaterialSurfaceMode DetectSurfaceMode(Material material)
        {
            if (material == null || material.shader == null)
            {
                return MaterialSurfaceMode.Opaque;
            }

            string shaderName = material.shader.name.ToLowerInvariant();

            // lilToonはシェーダー名にCutout/Transparentが含まれない構成でも
            // _TransparentModeに描画方式が保存されています。
            if (shaderName.Contains("liltoon") && material.HasProperty("_TransparentMode"))
            {
                int transparentMode = Mathf.RoundToInt(material.GetFloat("_TransparentMode"));
                if (transparentMode == 1)
                {
                    return MaterialSurfaceMode.Cutout;
                }
                if (transparentMode >= 2)
                {
                    return MaterialSurfaceMode.Transparent;
                }
            }

            // lilToon系ではTransparentModeがOpaqueでも、AlphaMaskだけで抜きを作る素材があります。
            // その場合にOpaqueへしてしまうとバッグの金具やレースなどが板状に表示されます。
            if (HasEnabledAlphaMask(material) && material.renderQueue >= 2450)
            {
                return MaterialSurfaceMode.Cutout;
            }

            string fallbackTag = string.Empty;
            try
            {
                fallbackTag = material.GetTag("VRCFallback", false, string.Empty) ?? string.Empty;
            }
            catch
            {
                fallbackTag = string.Empty;
            }

            if (fallbackTag.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MaterialSurfaceMode.Cutout;
            }

            // HiddenTransparentは「透明表現を安全に隠す」目的で設定されることが多いため、
            // 実マテリアル側で透明モードが明示されている場合だけTransparentとして扱います。
            if (fallbackTag.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 &&
                HasExplicitTransparency(material))
            {
                return MaterialSurfaceMode.Transparent;
            }

            if (shaderName.Contains("cutout") || shaderName.Contains("alphatest") || shaderName.Contains("alpha test"))
            {
                return MaterialSurfaceMode.Cutout;
            }

            if (shaderName.Contains("transparent") || shaderName.Contains("fade"))
            {
                return MaterialSurfaceMode.Transparent;
            }

            if (material.HasProperty("_Mode"))
            {
                float mode = material.GetFloat("_Mode");
                if (mode >= 2f) return MaterialSurfaceMode.Transparent;
                if (mode >= 1f) return MaterialSurfaceMode.Cutout;
            }

            if (material.renderQueue >= 3000)
            {
                return MaterialSurfaceMode.Transparent;
            }

            if (material.renderQueue >= 2450 && material.HasProperty("_Cutoff"))
            {
                return MaterialSurfaceMode.Cutout;
            }

            return MaterialSurfaceMode.Opaque;
        }

        private static void ApplySurfaceSettings(Material source, Material target, MaterialSurfaceMode surfaceMode)
        {
            if (source == null || target == null)
            {
                return;
            }

            if (source.HasProperty("_Cutoff") && HasMaterialPropertyType(target, "_Cutoff", ShaderUtil.ShaderPropertyType.Float, ShaderUtil.ShaderPropertyType.Range))
            {
                target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            }

            if (surfaceMode != MaterialSurfaceMode.Opaque)
            {
                if (HasMaterialPropertyType(target, "_ZWrite", ShaderUtil.ShaderPropertyType.Float, ShaderUtil.ShaderPropertyType.Range)) target.SetFloat("_ZWrite", surfaceMode == MaterialSurfaceMode.Cutout ? 1f : 0f);
                if (HasMaterialPropertyType(target, "_AlphaClip", ShaderUtil.ShaderPropertyType.Float, ShaderUtil.ShaderPropertyType.Range)) target.SetFloat("_AlphaClip", surfaceMode == MaterialSurfaceMode.Cutout ? 1f : 0f);
            }
        }

        private static MaterialSnapshot CaptureMaterial(Material material)
        {
            MaterialSnapshot snapshot = new MaterialSnapshot();
            if (material == null || material.shader == null)
            {
                return snapshot;
            }

            int count = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < count; i++)
            {
                string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(material.shader, i);
                try
                {
                    switch (propertyType)
                    {
                        case ShaderUtil.ShaderPropertyType.Color:
                            snapshot.colors[propertyName] = material.GetColor(propertyName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            snapshot.vectors[propertyName] = material.GetVector(propertyName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            snapshot.floats[propertyName] = material.GetFloat(propertyName);
                            break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            snapshot.textures[propertyName] = material.GetTexture(propertyName);
                            snapshot.textureScales[propertyName] = material.GetTextureScale(propertyName);
                            snapshot.textureOffsets[propertyName] = material.GetTextureOffset(propertyName);
                            break;
                    }
                }
                catch
                {
                    // Some custom shaders expose properties that cannot be queried normally.
                }
            }

            CapturePrimaryTexture(material, snapshot);
            CapturePrimaryColor(material, snapshot);
            return snapshot;
        }

        private static void CapturePrimaryTexture(Material material, MaterialSnapshot snapshot)
        {
            for (int i = 0; i < MainTexturePropertyNames.Length; i++)
            {
                string propertyName = MainTexturePropertyNames[i];
                Texture texture;
                if (!snapshot.textures.TryGetValue(propertyName, out texture) || texture == null)
                {
                    continue;
                }

                SetPrimaryTextureSnapshot(snapshot, propertyName, texture);
                return;
            }

            foreach (KeyValuePair<string, Texture> pair in snapshot.textures)
            {
                if (pair.Value == null || IsUtilityTextureProperty(pair.Key))
                {
                    continue;
                }

                string lower = pair.Key.ToLowerInvariant();
                if (lower.Contains("main") || lower.Contains("base") || lower.Contains("albedo") ||
                    lower.Contains("diffuse") || lower.Contains("color"))
                {
                    SetPrimaryTextureSnapshot(snapshot, pair.Key, pair.Value);
                    return;
                }
            }

            try
            {
                if (material.mainTexture != null)
                {
                    string propertyName = material.HasProperty("_MainTex") ? "_MainTex" : string.Empty;
                    SetPrimaryTextureSnapshot(snapshot, propertyName, material.mainTexture);
                    if (string.IsNullOrEmpty(propertyName))
                    {
                        snapshot.primaryTextureScale = material.mainTextureScale;
                        snapshot.primaryTextureOffset = material.mainTextureOffset;
                    }
                    return;
                }
            }
            catch
            {
                // Ignore custom material access errors and try a visible texture fallback.
            }

            foreach (KeyValuePair<string, Texture> pair in snapshot.textures)
            {
                if (pair.Value != null && !IsUtilityTextureProperty(pair.Key))
                {
                    SetPrimaryTextureSnapshot(snapshot, pair.Key, pair.Value);
                    return;
                }
            }
        }

        private static void SetPrimaryTextureSnapshot(MaterialSnapshot snapshot, string propertyName, Texture texture)
        {
            snapshot.primaryTexture = texture;
            snapshot.primaryTextureProperty = propertyName ?? string.Empty;

            Vector2 scale;
            if (!string.IsNullOrEmpty(propertyName) && snapshot.textureScales.TryGetValue(propertyName, out scale))
            {
                snapshot.primaryTextureScale = scale;
            }

            Vector2 offset;
            if (!string.IsNullOrEmpty(propertyName) && snapshot.textureOffsets.TryGetValue(propertyName, out offset))
            {
                snapshot.primaryTextureOffset = offset;
            }
        }

        private static bool IsUtilityTextureProperty(string propertyName)
        {
            string lower = (propertyName ?? string.Empty).ToLowerInvariant();
            return lower.Contains("mask") || lower.Contains("normal") || lower.Contains("bump") ||
                   lower.Contains("matcap") || lower.Contains("ramp") || lower.Contains("shadow") ||
                   lower.Contains("emission") || lower.Contains("outline") || lower.Contains("noise") ||
                   lower.Contains("gradation") || lower.Contains("dissolve");
        }

        private static void CapturePrimaryColor(Material material, MaterialSnapshot snapshot)
        {
            for (int i = 0; i < MainColorPropertyNames.Length; i++)
            {
                string propertyName = MainColorPropertyNames[i];
                Color color;
                if (!snapshot.colors.TryGetValue(propertyName, out color))
                {
                    continue;
                }

                snapshot.primaryColor = color;
                snapshot.primaryColorProperty = propertyName;
                snapshot.hasPrimaryColor = true;
                return;
            }

            try
            {
                if (material.HasProperty("_Color"))
                {
                    snapshot.primaryColor = material.color;
                    snapshot.primaryColorProperty = "_Color";
                    snapshot.hasPrimaryColor = true;
                }
            }
            catch
            {
                // Ignore custom material access errors.
            }
        }

        private static void RestoreMaterial(Material material, MaterialSnapshot snapshot)
        {
            foreach (KeyValuePair<string, float> pair in snapshot.floats)
            {
                if (HasMaterialPropertyType(material, pair.Key,
                    ShaderUtil.ShaderPropertyType.Float,
                    ShaderUtil.ShaderPropertyType.Range))
                {
                    material.SetFloat(pair.Key, pair.Value);
                }
            }
            foreach (KeyValuePair<string, Color> pair in snapshot.colors)
            {
                if (HasMaterialPropertyType(material, pair.Key, ShaderUtil.ShaderPropertyType.Color))
                {
                    material.SetColor(pair.Key, pair.Value);
                }
            }
            foreach (KeyValuePair<string, Vector4> pair in snapshot.vectors)
            {
                if (HasMaterialPropertyType(material, pair.Key, ShaderUtil.ShaderPropertyType.Vector))
                {
                    material.SetVector(pair.Key, pair.Value);
                }
            }
            foreach (KeyValuePair<string, Texture> pair in snapshot.textures)
            {
                if (!HasMaterialPropertyType(material, pair.Key, ShaderUtil.ShaderPropertyType.TexEnv))
                {
                    continue;
                }

                material.SetTexture(pair.Key, pair.Value);
                Vector2 scale;
                if (snapshot.textureScales.TryGetValue(pair.Key, out scale)) material.SetTextureScale(pair.Key, scale);
                Vector2 offset;
                if (snapshot.textureOffsets.TryGetValue(pair.Key, out offset)) material.SetTextureOffset(pair.Key, offset);
            }

            ApplyPrimaryTexture(material, snapshot.primaryTexture, snapshot.primaryTextureScale, snapshot.primaryTextureOffset);
            ApplyPrimaryColor(material, snapshot);
        }

        private static bool IsSourceEmissionEnabled(Material source, MaterialSnapshot snapshot)
        {
            if (source == null)
            {
                return false;
            }

            for (int i = 0; i < EmissionTogglePropertyNames.Length; i++)
            {
                string propertyName = EmissionTogglePropertyNames[i];
                if (source.HasProperty(propertyName))
                {
                    try
                    {
                        return source.GetFloat(propertyName) > 0.5f;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            if (source.IsKeywordEnabled("_EMISSION"))
            {
                return true;
            }

            Texture emissionTexture = FindSnapshotTexture(snapshot, EmissionTexturePropertyNames);
            if (emissionTexture == null)
            {
                return false;
            }

            Color emissionColor;
            bool hasColor = TryFindSnapshotColor(snapshot, EmissionColorPropertyNames, out emissionColor);
            if (hasColor && IsNearlyBlack(emissionColor))
            {
                return false;
            }

            float strength;
            if (TryFindSnapshotFloat(snapshot, EmissionStrengthPropertyNames, out strength) && strength <= 0.0001f)
            {
                return false;
            }

            return true;
        }


        private static Texture TryCreateFallbackPrimaryTexture(
            Material source,
            MaterialSnapshot snapshot,
            string textureFolder,
            NekoQuestGenerationOptions options,
            MaterialSurfaceMode surfaceMode,
            bool sourceMatCapEnabled,
            NekoQuestGenerationResult result)
        {
            if (source == null || snapshot == null || snapshot.primaryTexture != null)
            {
                return null;
            }

            Texture alphaMask = HasEnabledAlphaMask(source)
                ? FindSnapshotTexture(snapshot, AlphaMaskTexturePropertyNames)
                : null;
            bool needsAlpha = alphaMask != null && surfaceMode != MaterialSurfaceMode.Opaque;

            // メイン画像なしでMatCapや反射だけを使う素材は、変換先で白い入力画像が必要です。
            if (!sourceMatCapEnabled && !needsAlpha)
            {
                return null;
            }

            Texture2D readableMask = null;
            Texture2D output = null;
            try
            {
                int width = needsAlpha ? Mathf.Max(4, Mathf.Min(alphaMask.width, options.smallTextureSize)) : 4;
                int height = needsAlpha ? Mathf.Max(4, Mathf.Min(alphaMask.height, options.smallTextureSize)) : 4;
                if (needsAlpha)
                {
                    readableMask = ReadTexture(alphaMask, width, height);
                }

                Color baseColor = sourceMatCapEnabled
                    ? Color.white
                    : snapshot.hasPrimaryColor ? snapshot.primaryColor : Color.white;
                baseColor.a = 1f;

                Color[] pixels = new Color[width * height];
                Color[] maskPixels = readableMask != null ? readableMask.GetPixels() : null;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color pixel = baseColor;
                    if (maskPixels != null)
                    {
                        Color mask = maskPixels[i];
                        pixel.a = Mathf.Clamp01(Mathf.Max(mask.r, Mathf.Max(mask.g, mask.b)) * mask.a);
                    }
                    pixels[i] = pixel;
                }

                output = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
                output.name = source.name + "_QuestFallbackBase";
                output.SetPixels(pixels);
                output.Apply(true, false);

                string destination = AssetDatabase.GenerateUniqueAssetPath(
                    textureFolder + "/" + SanitizeFileName(source.name) + "_QuestFallbackBase.png");
                File.WriteAllBytes(Path.GetFullPath(destination), output.EncodeToPNG());
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter importer = AssetImporter.GetAtPath(destination) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = needsAlpha;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = options.smallTextureSize;

                    TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                    android.name = "Android";
                    android.overridden = true;
                    android.maxTextureSize = options.smallTextureSize;
                    android.format = TextureImporterFormat.ASTC_6x6;
                    android.textureCompression = TextureImporterCompression.Compressed;
                    android.compressionQuality = 50;
                    importer.SetPlatformTextureSettings(android);
                    importer.SaveAndReimport();
                }

                Texture created = AssetDatabase.LoadAssetAtPath<Texture>(destination);
                if (created != null)
                {
                    result.changes.Add(needsAlpha
                        ? "メイン画像のない素材へAlpha Mask付きのQuest用ベース画像を生成: " + source.name
                        : "メイン画像のないMatCap素材へQuest用ベース画像を生成: " + source.name);
                }
                return created;
            }
            catch (Exception exception)
            {
                result.warnings.Add("メイン画像のない素材を安全に変換できませんでした: " + source.name + " (" + exception.Message + ")");
                return null;
            }
            finally
            {
                if (readableMask != null) UnityEngine.Object.DestroyImmediate(readableMask);
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
            }
        }

        private static Texture TryBakeMatCapTexture(
            Material source,
            MaterialSnapshot snapshot,
            string textureFolder,
            NekoQuestGenerationOptions options,
            NekoQuestGenerationResult result)
        {
            if (!IsSourceMatCapEnabled(source, snapshot))
            {
                return null;
            }

            Texture firstTexture = FindSnapshotTexture(snapshot, MatCapTexturePropertyNames);
            if (firstTexture == null)
            {
                return null;
            }

            Texture secondTexture = null;
            bool useSecond = GetMaterialToggle(source, "_UseMatCap2nd");
            if (useSecond)
            {
                secondTexture = FindSnapshotTexture(snapshot, MatCapSecondTexturePropertyNames);
            }

            Color firstColor;
            if (!TryFindSnapshotColor(snapshot, MatCapColorPropertyNames, out firstColor))
            {
                firstColor = Color.white;
            }

            Color secondColor;
            if (!TryFindSnapshotColor(snapshot, MatCapSecondColorPropertyNames, out secondColor))
            {
                secondColor = Color.white;
            }

            float firstBlend = source.HasProperty("_MatCapBlend")
                ? Mathf.Clamp01(source.GetFloat("_MatCapBlend"))
                : 1f;
            float secondBlend = source.HasProperty("_MatCap2ndBlend")
                ? Mathf.Clamp01(source.GetFloat("_MatCap2ndBlend"))
                : 1f;
            int secondBlendMode = source.HasProperty("_MatCap2ndBlendMode")
                ? Mathf.RoundToInt(source.GetFloat("_MatCap2ndBlendMode"))
                : 0;

            bool needsBake = secondTexture != null ||
                             !ApproximatelyWhite(firstColor) ||
                             firstBlend < 0.999f;
            if (!needsBake)
            {
                return null;
            }

            Texture2D readableFirst = null;
            Texture2D readableSecond = null;
            Texture2D readableSecondMask = null;
            Texture2D output = null;
            try
            {
                int maxSize = ChooseTextureSize(firstTexture.name + "_matcap", options);
                int width = Mathf.Max(4, firstTexture.width);
                int height = Mathf.Max(4, firstTexture.height);
                float scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(width, height));
                width = Mathf.Max(4, Mathf.RoundToInt(width * scale));
                height = Mathf.Max(4, Mathf.RoundToInt(height * scale));

                readableFirst = ReadTexture(firstTexture, width, height);
                if (readableFirst == null)
                {
                    return null;
                }

                if (secondTexture != null)
                {
                    readableSecond = ReadTexture(secondTexture, width, height);
                }

                Texture secondMask = secondTexture != null && source.HasProperty("_MatCap2ndBlendMask")
                    ? source.GetTexture("_MatCap2ndBlendMask")
                    : null;
                if (secondMask != null)
                {
                    // BlendMaskはUV空間でMatCapは視線空間のため、そのまま重ねると模様がずれます。
                    // 全体の平均値だけを強度として使い、不自然な模様の貼り付きを防ぎます。
                    readableSecondMask = ReadTexture(secondMask, 64, 64);
                }

                float secondMaskAverage = readableSecondMask != null
                    ? ComputeAverageMask(readableSecondMask)
                    : 1f;

                Color[] firstPixels = readableFirst.GetPixels();
                Color[] secondPixels = readableSecond != null ? readableSecond.GetPixels() : null;
                Color[] pixels = new Color[firstPixels.Length];

                Color firstTint = firstColor;
                firstTint.a = 1f;
                Color secondTint = secondColor;
                secondTint.a = 1f;

                float firstStrength = Mathf.Clamp01(firstBlend * Mathf.Max(0f, firstColor.a));
                float secondStrength = Mathf.Clamp01(secondBlend * Mathf.Max(0f, secondColor.a) * secondMaskAverage);

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color first = MultiplyColor(firstPixels[i], firstTint);
                    first.r *= firstStrength;
                    first.g *= firstStrength;
                    first.b *= firstStrength;
                    first.a = 1f;
                    Color current = first;

                    if (secondPixels != null && secondStrength > 0.0001f)
                    {
                        Color second = MultiplyColor(secondPixels[i], secondTint);
                        second.a = secondStrength;
                        current = BlendLayer(current, second, 1f, secondBlendMode);
                    }

                    current.a = 1f;
                    pixels[i] = ClampColor(current);
                }

                output = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
                output.name = firstTexture.name + "_QuestMatCapBaked";
                output.SetPixels(pixels);
                output.Apply(true, false);

                string destination = AssetDatabase.GenerateUniqueAssetPath(
                    textureFolder + "/" + SanitizeFileName(source.name) + "_QuestMatCapBaked.png");
                File.WriteAllBytes(Path.GetFullPath(destination), output.EncodeToPNG());
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter importer = AssetImporter.GetAtPath(destination) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = maxSize;

                    TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                    android.name = "Android";
                    android.overridden = true;
                    android.maxTextureSize = maxSize;
                    android.format = TextureImporterFormat.ASTC_6x6;
                    android.textureCompression = TextureImporterCompression.Compressed;
                    android.compressionQuality = 50;
                    importer.SetPlatformTextureSettings(android);
                    importer.SaveAndReimport();
                }

                Texture baked = AssetDatabase.LoadAssetAtPath<Texture>(destination);
                if (baked != null)
                {
                    result.changes.Add(secondTexture != null
                        ? "複数MatCapをQuest用の1枚へ統合: " + source.name
                        : "MatCapの色と強度をQuest用画像へ焼き込み: " + source.name);
                }
                return baked;
            }
            catch (Exception exception)
            {
                result.warnings.Add("MatCapの焼き込みに失敗したため通常変換を使用します: " + source.name + " (" + exception.Message + ")");
                return null;
            }
            finally
            {
                if (readableFirst != null) UnityEngine.Object.DestroyImmediate(readableFirst);
                if (readableSecond != null) UnityEngine.Object.DestroyImmediate(readableSecond);
                if (readableSecondMask != null) UnityEngine.Object.DestroyImmediate(readableSecondMask);
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
            }
        }

        private static float ComputeAverageMask(Texture2D texture)
        {
            if (texture == null)
            {
                return 1f;
            }

            Color[] pixels = texture.GetPixels();
            if (pixels == null || pixels.Length == 0)
            {
                return 1f;
            }

            double sum = 0d;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color color = pixels[i];
                sum += Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * color.a;
            }

            return Mathf.Clamp01((float)(sum / pixels.Length));
        }

        private static bool IsSourceMatCapEnabled(Material source, MaterialSnapshot snapshot)
        {
            if (source == null || snapshot == null)
            {
                return false;
            }

            Texture matCapTexture = FindSnapshotTexture(snapshot, MatCapTexturePropertyNames);
            if (matCapTexture == null)
            {
                return false;
            }

            for (int i = 0; i < MatCapTogglePropertyNames.Length; i++)
            {
                string propertyName = MatCapTogglePropertyNames[i];
                if (!source.HasProperty(propertyName))
                {
                    continue;
                }

                try
                {
                    return source.GetFloat(propertyName) > 0.5f;
                }
                catch
                {
                    return false;
                }
            }

            // 明示的なON/OFF項目を持たないMatCapシェーダーは、画像が設定されていれば有効とみなします。
            return true;
        }

        private static bool ConfigureTargetMatCap(
            Material source,
            Material target,
            MaterialSnapshot snapshot,
            string textureFolder,
            NekoQuestGenerationOptions options,
            Dictionary<Texture, Texture> textureMap,
            NekoQuestGenerationResult result,
            ref int copiedTextures,
            bool matCapDominant,
            Texture preBakedMatCap)
        {
            if (!IsSourceMatCapEnabled(source, snapshot) || target == null || target.shader == null)
            {
                return false;
            }

            Texture sourceMatCap = FindSnapshotTexture(snapshot, MatCapTexturePropertyNames);
            if (sourceMatCap == null)
            {
                return false;
            }

            Texture targetMatCap = preBakedMatCap != null ? preBakedMatCap : sourceMatCap;
            if (preBakedMatCap == null && options.duplicateAndCompressTextures)
            {
                targetMatCap = GetOrCreateQuestTexture(
                    sourceMatCap,
                    textureFolder,
                    options,
                    textureMap,
                    result,
                    ref copiedTextures);
            }

            if (targetMatCap == null)
            {
                return false;
            }

            string targetProperty = FindSemanticTextureProperty(
                target,
                TargetMatCapTexturePropertyNames,
                "matcap");
            if (string.IsNullOrEmpty(targetProperty))
            {
                return false;
            }

            target.SetTexture(targetProperty, targetMatCap);
            target.SetTextureScale(targetProperty, Vector2.one);
            target.SetTextureOffset(targetProperty, Vector2.zero);

            Color matCapColor;
            bool hasMatCapColor = TryFindSnapshotColor(snapshot, MatCapColorPropertyNames, out matCapColor);
            if (!hasMatCapColor || preBakedMatCap != null)
            {
                // 色・強度を画像へ焼き込んだ場合は、変換先で二重に色を掛けません。
                matCapColor = Color.white;
            }

            ApplySemanticColor(target, MatCapColorPropertyNames, "matcap", matCapColor);
            EnableSemanticFeature(target, "matcap");

            // MatCap Litは専用の色項目を持たないSDK版があります。
            // その場合は元MatCapの色をメイン色へ移し、白一色になるのを防ぎます。
            string shaderName = target.shader.name ?? string.Empty;
            if (matCapDominant && shaderName.IndexOf("MatCap Lit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (hasMatCapColor)
                {
                    Color safeTint = matCapColor;
                    safeTint.a = 1f;
                    ApplyColorToKnownProperties(target, MainColorPropertyNames, safeTint);
                }
            }

            ApplyMaterialPropertyDrawersSafe(target);
            result.changes.Add("MatCapをQuest対応シェーダーへ引き継ぎ: " + source.name);
            return true;
        }

        private static bool HasEnabledSourceNormalMap(Material source, MaterialSnapshot snapshot)
        {
            string propertyName;
            Texture texture;
            float strength;
            Vector2 scale;
            Vector2 offset;
            return TryGetPreferredSourceNormal(source, snapshot, out propertyName, out texture, out strength, out scale, out offset);
        }

        private static bool ConfigureTargetNormalMap(
            Material source,
            Material target,
            MaterialSnapshot snapshot,
            string textureFolder,
            NekoQuestGenerationOptions options,
            Dictionary<Texture, Texture> textureMap,
            NekoQuestGenerationResult result,
            ref int copiedTextures)
        {
            if (source == null || target == null || snapshot == null)
            {
                return false;
            }

            string sourceProperty;
            Texture sourceTexture;
            float strength;
            Vector2 scale;
            Vector2 offset;
            if (!TryGetPreferredSourceNormal(source, snapshot, out sourceProperty, out sourceTexture, out strength, out scale, out offset))
            {
                return false;
            }

            Texture targetTexture = sourceTexture;
            if (options.duplicateAndCompressTextures)
            {
                targetTexture = GetOrCreateQuestTexture(
                    sourceTexture,
                    textureFolder,
                    options,
                    textureMap,
                    result,
                    ref copiedTextures);
            }

            if (targetTexture == null)
            {
                return false;
            }

            string targetProperty = FindSemanticTextureProperty(
                target,
                TargetNormalTexturePropertyNames,
                "normal");
            if (string.IsNullOrEmpty(targetProperty))
            {
                // Unity標準系ではNormalがBumpという名前の場合があります。
                targetProperty = FindSemanticTextureProperty(
                    target,
                    TargetNormalTexturePropertyNames,
                    "bump");
            }

            if (string.IsNullOrEmpty(targetProperty))
            {
                return false;
            }

            target.SetTexture(targetProperty, targetTexture);
            target.SetTextureScale(targetProperty, scale);
            target.SetTextureOffset(targetProperty, offset);

            for (int i = 0; i < NormalStrengthPropertyNames.Length; i++)
            {
                string propertyName = NormalStrengthPropertyNames[i];
                if (HasMaterialPropertyType(target, propertyName,
                    ShaderUtil.ShaderPropertyType.Float,
                    ShaderUtil.ShaderPropertyType.Range))
                {
                    target.SetFloat(propertyName, strength);
                }
            }

            EnableSemanticFeature(target, "normal");
            EnableSemanticFeature(target, "bump");
            result.changes.Add("ノーマルマップをQuest対応シェーダーへ引き継ぎ: " + source.name);
            return true;
        }

        private static bool TryGetPreferredSourceNormal(
            Material source,
            MaterialSnapshot snapshot,
            out string propertyName,
            out Texture texture,
            out float strength,
            out Vector2 scale,
            out Vector2 offset)
        {
            propertyName = string.Empty;
            texture = null;
            strength = 1f;
            scale = Vector2.one;
            offset = Vector2.zero;

            if (source == null || snapshot == null)
            {
                return false;
            }

            string[] preferred = GetMaterialToggle(source, "_UseBumpMap")
                ? new[] { "_BumpMap", "_NormalMap", "_NormalTex", "_Bump2ndMap", "_DetailNormalMap", "_MatCapBumpMap" }
                : GetMaterialToggle(source, "_UseBump2ndMap")
                    ? new[] { "_Bump2ndMap", "_DetailNormalMap", "_BumpMap", "_NormalMap", "_MatCapBumpMap" }
                    : GetMaterialToggle(source, "_MatCapCustomNormal")
                        ? new[] { "_MatCapBumpMap", "_Bump2ndMap", "_BumpMap", "_NormalMap", "_DetailNormalMap" }
                        : NormalTexturePropertyNames;

            for (int i = 0; i < preferred.Length; i++)
            {
                Texture candidate;
                if (!snapshot.textures.TryGetValue(preferred[i], out candidate) || candidate == null)
                {
                    continue;
                }

                propertyName = preferred[i];
                texture = candidate;
                break;
            }

            if (texture == null)
            {
                return false;
            }

            if (snapshot.textureScales.TryGetValue(propertyName, out scale) == false)
            {
                scale = Vector2.one;
            }
            if (snapshot.textureOffsets.TryGetValue(propertyName, out offset) == false)
            {
                offset = Vector2.zero;
            }

            string strengthProperty = propertyName == "_Bump2ndMap"
                ? "_Bump2ndScale"
                : propertyName == "_DetailNormalMap"
                    ? "_DetailNormalMapScale"
                    : propertyName.IndexOf("MatCap", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "_MatCapBumpScale"
                        : "_BumpScale";

            float sourceStrength;
            if (snapshot.floats.TryGetValue(strengthProperty, out sourceStrength))
            {
                strength = sourceStrength;
            }
            else
            {
                for (int i = 0; i < NormalStrengthPropertyNames.Length; i++)
                {
                    if (snapshot.floats.TryGetValue(NormalStrengthPropertyNames[i], out sourceStrength))
                    {
                        strength = sourceStrength;
                        break;
                    }
                }
            }

            // lilToonのMatCap用法線では負値が反転指定として使われます。
            // Quest側で極端な値になるのを避けつつ向きは維持します。
            strength = Mathf.Clamp(strength, -10f, 10f);
            return true;
        }

        private static void ConfigureTargetCull(Material source, Material target)
        {
            if (source == null || target == null || !source.HasProperty("_Cull"))
            {
                return;
            }

            float cull;
            try
            {
                cull = source.GetFloat("_Cull");
            }
            catch
            {
                return;
            }

            string[] names = { "_Cull", "_Culling", "_CullMode" };
            for (int i = 0; i < names.Length; i++)
            {
                if (HasMaterialPropertyType(target, names[i],
                    ShaderUtil.ShaderPropertyType.Float,
                    ShaderUtil.ShaderPropertyType.Range))
                {
                    target.SetFloat(names[i], cull);
                }
            }
        }

        private static void ConfigureTargetRim(Material source, Material target, MaterialSnapshot snapshot)
        {
            if (source == null || target == null || snapshot == null || !GetMaterialToggle(source, "_UseRim"))
            {
                return;
            }

            Color rimColor;
            if (snapshot.colors.TryGetValue("_RimColor", out rimColor))
            {
                string[] names = { "_RimColor", "_RimTint", "_RimLightColor" };
                ApplySemanticColor(target, names, "rim", rimColor);
            }

            float rimStrength;
            if (!snapshot.floats.TryGetValue("_RimMainStrength", out rimStrength))
            {
                rimStrength = 1f;
            }

            string[] strengthNames = { "_RimStrength", "_RimPower", "_RimIntensity", "_RimMainStrength" };
            for (int i = 0; i < strengthNames.Length; i++)
            {
                if (HasMaterialPropertyType(target, strengthNames[i],
                    ShaderUtil.ShaderPropertyType.Float,
                    ShaderUtil.ShaderPropertyType.Range))
                {
                    target.SetFloat(strengthNames[i], Mathf.Max(0f, rimStrength));
                }
            }

            EnableSemanticFeature(target, "rim");
        }

        private static string FindSemanticTextureProperty(
            Material material,
            string[] preferredNames,
            string semantic)
        {
            string exact = FindFirstMaterialProperty(material, preferredNames, ShaderUtil.ShaderPropertyType.TexEnv);
            if (!string.IsNullOrEmpty(exact))
            {
                return exact;
            }

            if (material == null || material.shader == null)
            {
                return string.Empty;
            }

            string token = (semantic ?? string.Empty).ToLowerInvariant();
            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            string best = string.Empty;
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }

                string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                string lower = propertyName.ToLowerInvariant();
                if (!lower.Contains(token) || lower.Contains("mask") || lower.Contains("normal") ||
                    lower.Contains("bump") || lower.Contains("detail"))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(best) || propertyName.Length < best.Length)
                {
                    best = propertyName;
                }
            }

            return best;
        }

        private static void ApplySemanticColor(
            Material material,
            string[] preferredNames,
            string semantic,
            Color color)
        {
            if (material == null || material.shader == null)
            {
                return;
            }

            bool assigned = false;
            for (int i = 0; i < preferredNames.Length; i++)
            {
                string propertyName = preferredNames[i];
                if (HasMaterialPropertyType(material, propertyName, ShaderUtil.ShaderPropertyType.Color))
                {
                    material.SetColor(propertyName, color);
                    assigned = true;
                }
            }

            if (assigned)
            {
                return;
            }

            string token = (semantic ?? string.Empty).ToLowerInvariant();
            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.Color)
                {
                    continue;
                }

                string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                string lower = propertyName.ToLowerInvariant();
                if (lower.Contains(token) && !lower.Contains("mask"))
                {
                    material.SetColor(propertyName, color);
                }
            }
        }

        private static void ApplyColorToKnownProperties(Material material, string[] propertyNames, Color color)
        {
            if (material == null || propertyNames == null)
            {
                return;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                string propertyName = propertyNames[i];
                if (HasMaterialPropertyType(material, propertyName, ShaderUtil.ShaderPropertyType.Color))
                {
                    material.SetColor(propertyName, color);
                }
            }
        }

        private static void EnableSemanticFeature(Material material, string semantic)
        {
            if (material == null || material.shader == null)
            {
                return;
            }

            string token = (semantic ?? string.Empty).ToLowerInvariant();
            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(material.shader, i);
                if (propertyType != ShaderUtil.ShaderPropertyType.Float &&
                    propertyType != ShaderUtil.ShaderPropertyType.Range)
                {
                    continue;
                }

                string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                string lower = propertyName.ToLowerInvariant();
                if (!lower.Contains(token))
                {
                    continue;
                }

                if (lower.Contains("use") || lower.Contains("enable") || lower.Contains("enabled") ||
                    lower.Contains("toggle") || lower.EndsWith("on", StringComparison.Ordinal))
                {
                    material.SetFloat(propertyName, 1f);
                }
            }
        }

        private static void ApplyMaterialPropertyDrawersSafe(Material material)
        {
            if (material == null)
            {
                return;
            }

            try
            {
                System.Reflection.MethodInfo method = typeof(MaterialEditor).GetMethod(
                    "ApplyMaterialPropertyDrawers",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Material) },
                    null);
                if (method != null)
                {
                    method.Invoke(null, new object[] { material });
                }
            }
            catch
            {
                // SDKやUnityのバージョン差で利用できない場合でも、設定済みプロパティは保持します。
            }
        }

        private static void ConfigureTargetEmission(
            Material source,
            Material target,
            MaterialSnapshot snapshot,
            bool sourceEmissionEnabled,
            NekoQuestGenerationResult result)
        {
            if (target == null || target.shader == null)
            {
                return;
            }

            if (!sourceEmissionEnabled)
            {
                bool hadUnusedBrightValue = HasBrightUnusedEmissionValue(snapshot);
                DisableTargetEmission(target);
                if (hadUnusedBrightValue)
                {
                    result.changes.Add("未使用の発光色を無効化して白飛びを防止: " + source.name);
                }
                return;
            }

            Color emissionColor;
            if (!TryFindSnapshotColor(snapshot, EmissionColorPropertyNames, out emissionColor))
            {
                emissionColor = Color.white;
            }

            for (int i = 0; i < EmissionColorPropertyNames.Length; i++)
            {
                string propertyName = EmissionColorPropertyNames[i];
                if (HasMaterialPropertyType(target, propertyName, ShaderUtil.ShaderPropertyType.Color))
                {
                    target.SetColor(propertyName, emissionColor);
                }
            }

            float strength;
            if (!TryFindSnapshotFloat(snapshot, EmissionStrengthPropertyNames, out strength))
            {
                strength = 1f;
            }

            for (int i = 0; i < EmissionStrengthPropertyNames.Length; i++)
            {
                string propertyName = EmissionStrengthPropertyNames[i];
                if (HasMaterialPropertyType(target, propertyName,
                    ShaderUtil.ShaderPropertyType.Float,
                    ShaderUtil.ShaderPropertyType.Range))
                {
                    target.SetFloat(propertyName, Mathf.Max(0f, strength));
                }
            }

            target.EnableKeyword("_EMISSION");
        }

        private static void DisableTargetEmission(Material material)
        {
            int count = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < count; i++)
            {
                string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                string lower = propertyName.ToLowerInvariant();
                if (!lower.Contains("emission") && !lower.Contains("emissive"))
                {
                    continue;
                }

                ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(material.shader, i);
                try
                {
                    if (propertyType == ShaderUtil.ShaderPropertyType.Color)
                    {
                        material.SetColor(propertyName, new Color(0f, 0f, 0f, 1f));
                    }
                    else if (propertyType == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        material.SetTexture(propertyName, null);
                    }
                    else if ((propertyType == ShaderUtil.ShaderPropertyType.Float ||
                              propertyType == ShaderUtil.ShaderPropertyType.Range) &&
                             (lower.Contains("strength") || lower.Contains("intensity") ||
                              lower.Contains("power") || lower.Contains("enable") ||
                              lower.Contains("use")))
                    {
                        material.SetFloat(propertyName, 0f);
                    }
                }
                catch
                {
                    // Custom inspectors can expose read-only aliases. Ignore them.
                }
            }

            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        private static bool HasBrightUnusedEmissionValue(MaterialSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            Color color;
            return TryFindSnapshotColor(snapshot, EmissionColorPropertyNames, out color) && !IsNearlyBlack(color);
        }

        private static Texture FindSnapshotTexture(MaterialSnapshot snapshot, string[] propertyNames)
        {
            if (snapshot == null || propertyNames == null)
            {
                return null;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                Texture texture;
                if (snapshot.textures.TryGetValue(propertyNames[i], out texture) && texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static bool TryFindSnapshotColor(MaterialSnapshot snapshot, string[] propertyNames, out Color color)
        {
            color = Color.black;
            if (snapshot == null || propertyNames == null)
            {
                return false;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (snapshot.colors.TryGetValue(propertyNames[i], out color))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindSnapshotFloat(MaterialSnapshot snapshot, string[] propertyNames, out float value)
        {
            value = 0f;
            if (snapshot == null || propertyNames == null)
            {
                return false;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (snapshot.floats.TryGetValue(propertyNames[i], out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNearlyBlack(Color color)
        {
            return color.r <= 0.0001f && color.g <= 0.0001f && color.b <= 0.0001f;
        }

        private static bool HasMaterialPropertyType(
            Material material,
            string propertyName,
            params ShaderUtil.ShaderPropertyType[] acceptedTypes)
        {
            if (material == null || material.shader == null || string.IsNullOrEmpty(propertyName) || acceptedTypes == null)
            {
                return false;
            }

            int count = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(ShaderUtil.GetPropertyName(material.shader, i), propertyName, StringComparison.Ordinal))
                {
                    continue;
                }

                ShaderUtil.ShaderPropertyType actualType = ShaderUtil.GetPropertyType(material.shader, i);
                for (int t = 0; t < acceptedTypes.Length; t++)
                {
                    if (actualType == acceptedTypes[t])
                    {
                        return true;
                    }
                }
                return false;
            }

            return false;
        }

        private static void ApplyPrimaryTexture(Material material, Texture texture, Vector2 scale, Vector2 offset)
        {
            if (material == null || texture == null)
            {
                return;
            }

            bool assigned = false;
            for (int i = 0; i < TargetMainTexturePropertyNames.Length; i++)
            {
                string propertyName = TargetMainTexturePropertyNames[i];
                if (!HasMaterialPropertyType(material, propertyName, ShaderUtil.ShaderPropertyType.TexEnv))
                {
                    continue;
                }

                material.SetTexture(propertyName, texture);
                material.SetTextureScale(propertyName, scale);
                material.SetTextureOffset(propertyName, offset);
                assigned = true;
            }

            if (assigned)
            {
                return;
            }

            try
            {
                material.mainTexture = texture;
                material.mainTextureScale = scale;
                material.mainTextureOffset = offset;
            }
            catch
            {
                // The selected shader has no assignable main texture property.
            }
        }

        private static void ApplyPrimaryColor(Material material, MaterialSnapshot snapshot)
        {
            if (material == null || !snapshot.hasPrimaryColor)
            {
                return;
            }

            for (int i = 0; i < MainColorPropertyNames.Length; i++)
            {
                string propertyName = MainColorPropertyNames[i];
                if (HasMaterialPropertyType(material, propertyName, ShaderUtil.ShaderPropertyType.Color))
                {
                    material.SetColor(propertyName, snapshot.primaryColor);
                }
            }
        }

        private static string FindFirstMaterialProperty(
            Material material,
            string[] propertyNames,
            params ShaderUtil.ShaderPropertyType[] acceptedTypes)
        {
            if (material == null || propertyNames == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (HasMaterialPropertyType(material, propertyNames[i], acceptedTypes))
                {
                    return propertyNames[i];
                }
            }

            return string.Empty;
        }

        private static Texture GetAssignedPrimaryTexture(Material material)
        {
            if (material == null)
            {
                return null;
            }

            for (int i = 0; i < TargetMainTexturePropertyNames.Length; i++)
            {
                string propertyName = TargetMainTexturePropertyNames[i];
                if (!HasMaterialPropertyType(material, propertyName, ShaderUtil.ShaderPropertyType.TexEnv))
                {
                    continue;
                }

                Texture texture = material.GetTexture(propertyName);
                if (texture != null)
                {
                    return texture;
                }
            }

            try
            {
                return material.mainTexture;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsurePrimaryMaterialAppearance(
            Material source,
            Material quest,
            MaterialSnapshot snapshot,
            NekoQuestGenerationResult result)
        {
            if (quest == null || snapshot == null)
            {
                return;
            }

            if (snapshot.primaryTexture != null && GetAssignedPrimaryTexture(quest) == null)
            {
                ApplyPrimaryTexture(quest, snapshot.primaryTexture, snapshot.primaryTextureScale, snapshot.primaryTextureOffset);
            }

            if (snapshot.primaryTexture != null && GetAssignedPrimaryTexture(quest) == null)
            {
                result.warnings.Add("メインテクスチャを変換先へ割り当てられませんでした: " + source.name);
            }
        }

        private static int DuplicateMaterialTextures(
            Material material,
            MaterialSnapshot snapshot,
            string textureFolder,
            NekoQuestGenerationOptions options,
            Dictionary<Texture, Texture> textureMap,
            NekoQuestGenerationResult result,
            bool skipPrimaryTexture)
        {
            int copied = 0;
            foreach (KeyValuePair<string, Texture> pair in snapshot.textures)
            {
                if (pair.Value == null || !HasMaterialPropertyType(material, pair.Key, ShaderUtil.ShaderPropertyType.TexEnv))
                {
                    continue;
                }

                if (skipPrimaryTexture && pair.Value == snapshot.primaryTexture &&
                    string.Equals(pair.Key, snapshot.primaryTextureProperty, StringComparison.Ordinal))
                {
                    continue;
                }

                Texture questTexture = GetOrCreateQuestTexture(pair.Value, textureFolder, options, textureMap, result, ref copied);
                if (questTexture == null)
                {
                    continue;
                }

                material.SetTexture(pair.Key, questTexture);
                Vector2 scale;
                if (snapshot.textureScales.TryGetValue(pair.Key, out scale)) material.SetTextureScale(pair.Key, scale);
                Vector2 offset;
                if (snapshot.textureOffsets.TryGetValue(pair.Key, out offset)) material.SetTextureOffset(pair.Key, offset);
            }

            if (!skipPrimaryTexture && snapshot.primaryTexture != null)
            {
                Texture primaryQuestTexture = GetOrCreateQuestTexture(
                    snapshot.primaryTexture,
                    textureFolder,
                    options,
                    textureMap,
                    result,
                    ref copied);

                if (primaryQuestTexture != null)
                {
                    ApplyPrimaryTexture(material, primaryQuestTexture, snapshot.primaryTextureScale, snapshot.primaryTextureOffset);
                }
            }

            return copied;
        }

        private static Texture GetOrCreateQuestTexture(
            Texture source,
            string textureFolder,
            NekoQuestGenerationOptions options,
            Dictionary<Texture, Texture> textureMap,
            NekoQuestGenerationResult result,
            ref int copied)
        {
            Texture questTexture;
            if (textureMap.TryGetValue(source, out questTexture))
            {
                return questTexture;
            }

            questTexture = DuplicateTextureAsset(source, textureFolder, options, result);
            if (questTexture != null)
            {
                textureMap[source] = questTexture;
                copied++;
            }

            return questTexture;
        }

        private static Texture DuplicateTextureAsset(
            Texture source,
            string textureFolder,
            NekoQuestGenerationOptions options,
            NekoQuestGenerationResult result)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                result.warnings.Add("プロジェクト内ファイルではないためテクスチャを複製できませんでした: " + source.name);
                return null;
            }

            string extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".png";
            }

            string destination = AssetDatabase.GenerateUniqueAssetPath(textureFolder + "/" + SanitizeFileName(source.name) + "_Quest" + extension);
            if (!AssetDatabase.CopyAsset(sourcePath, destination))
            {
                result.warnings.Add("テクスチャのコピーに失敗しました: " + source.name);
                return null;
            }

            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(destination) as TextureImporter;
            if (importer != null)
            {
                int maxSize = ChooseTextureSize(source.name, options);
                importer.maxTextureSize = maxSize;
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                android.name = "Android";
                android.overridden = true;
                android.maxTextureSize = maxSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                android.textureCompression = TextureImporterCompression.Compressed;
                android.compressionQuality = 50;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture>(destination);
        }

        private static int ChooseTextureSize(string textureName, NekoQuestGenerationOptions options)
        {
            string lower = (textureName ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("face") || lower.Contains("eye") || lower.Contains("body") ||
                lower.Contains("顔") || lower.Contains("瞳") || lower.Contains("肌"))
            {
                return options.importantTextureSize;
            }

            if (lower.Contains("mask") || lower.Contains("matcap") || lower.Contains("normal") ||
                lower.Contains("ramp") || lower.Contains("icon"))
            {
                return options.smallTextureSize;
            }

            return options.defaultTextureSize;
        }

        private static bool ApplyEmissionTexture(Material material, Texture texture)
        {
            if (material == null || texture == null)
            {
                return false;
            }

            bool assigned = false;
            for (int i = 0; i < TargetEmissionTexturePropertyNames.Length; i++)
            {
                string propertyName = TargetEmissionTexturePropertyNames[i];
                if (!HasMaterialPropertyType(material, propertyName, ShaderUtil.ShaderPropertyType.TexEnv))
                {
                    continue;
                }

                material.SetTexture(propertyName, texture);
                material.SetTextureScale(propertyName, Vector2.one);
                material.SetTextureOffset(propertyName, Vector2.zero);
                assigned = true;
            }

            return assigned;
        }

        private static Texture TryBakeEmissionTexture(
            Material source,
            MaterialSnapshot snapshot,
            string textureFolder,
            NekoQuestGenerationOptions options,
            bool sourceEmissionEnabled,
            NekoQuestGenerationResult result)
        {
            if (!sourceEmissionEnabled || source == null || snapshot == null)
            {
                return null;
            }

            Texture emissionTexture = FindSnapshotTexture(snapshot, EmissionTexturePropertyNames);
            Texture emissionMask = FindSnapshotTexture(snapshot, EmissionBlendMaskPropertyNames);
            if (emissionTexture == null || emissionMask == null)
            {
                return null;
            }

            Texture2D readableEmission = null;
            Texture2D readableMask = null;
            Texture2D output = null;

            try
            {
                int maxSize = ChooseTextureSize(emissionTexture.name, options);
                int width = Mathf.Max(4, emissionTexture.width);
                int height = Mathf.Max(4, emissionTexture.height);
                float sizeScale = Mathf.Min(1f, maxSize / (float)Mathf.Max(width, height));
                width = Mathf.Max(4, Mathf.RoundToInt(width * sizeScale));
                height = Mathf.Max(4, Mathf.RoundToInt(height * sizeScale));

                readableEmission = ReadTexture(emissionTexture, width, height);
                readableMask = ReadTexture(emissionMask, width, height);
                if (readableEmission == null || readableMask == null)
                {
                    return null;
                }

                Color[] emissionPixels = readableEmission.GetPixels();
                Color[] maskPixels = readableMask.GetPixels();
                if (emissionPixels.Length != maskPixels.Length)
                {
                    return null;
                }

                for (int i = 0; i < emissionPixels.Length; i++)
                {
                    Color maskColor = maskPixels[i];
                    float rgbMask = Mathf.Max(maskColor.r, Mathf.Max(maskColor.g, maskColor.b));
                    float mask = Mathf.Clamp01(rgbMask * maskColor.a);

                    Color emission = emissionPixels[i];
                    emission.r *= mask;
                    emission.g *= mask;
                    emission.b *= mask;
                    emission.a = mask;
                    emissionPixels[i] = ClampColor(emission);
                }

                output = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
                output.name = emissionTexture.name + "_QuestMaskedEmission";
                output.SetPixels(emissionPixels);
                output.Apply(true, false);

                string destination = AssetDatabase.GenerateUniqueAssetPath(
                    textureFolder + "/" + SanitizeFileName(emissionTexture.name) + "_QuestMaskedEmission.png");
                File.WriteAllBytes(Path.GetFullPath(destination), output.EncodeToPNG());
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter importer = AssetImporter.GetAtPath(destination) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = false;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = maxSize;

                    TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                    android.name = "Android";
                    android.overridden = true;
                    android.maxTextureSize = maxSize;
                    android.format = TextureImporterFormat.ASTC_6x6;
                    android.textureCompression = TextureImporterCompression.Compressed;
                    android.compressionQuality = 50;
                    importer.SetPlatformTextureSettings(android);
                    importer.SaveAndReimport();
                }

                Texture baked = AssetDatabase.LoadAssetAtPath<Texture>(destination);
                if (baked != null)
                {
                    result.changes.Add("発光マスクをQuest用Emissionへ焼き込み: " + source.name);
                }
                return baked;
            }
            catch (Exception exception)
            {
                result.warnings.Add("発光マスクを焼き込めませんでした: " + source.name + " (" + exception.Message + ")");
                return null;
            }
            finally
            {
                if (readableEmission != null) UnityEngine.Object.DestroyImmediate(readableEmission);
                if (readableMask != null) UnityEngine.Object.DestroyImmediate(readableMask);
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
            }
        }

        private static void SetPrimaryColorWhite(Material material)
        {
            if (material == null)
            {
                return;
            }

            for (int i = 0; i < MainColorPropertyNames.Length; i++)
            {
                string propertyName = MainColorPropertyNames[i];
                if (HasMaterialPropertyType(material, propertyName, ShaderUtil.ShaderPropertyType.Color))
                {
                    material.SetColor(propertyName, Color.white);
                }
            }
        }

        private static Texture TryBakePrimaryTexture(
            Material source,
            MaterialSnapshot snapshot,
            string textureFolder,
            NekoQuestGenerationOptions options,
            MaterialSurfaceMode surfaceMode,
            NekoQuestGenerationResult result)
        {
            if (source == null || snapshot == null || snapshot.primaryTexture == null || source.shader == null)
            {
                return null;
            }

            string shaderName = source.shader.name.ToLowerInvariant();
            bool isLilToon = shaderName.Contains("liltoon");
            if (!isLilToon)
            {
                return null;
            }

            bool usesSecondLayer = GetMaterialToggle(source, "_UseMain2ndTex");
            bool usesThirdLayer = GetMaterialToggle(source, "_UseMain3rdTex");
            Vector4 hsvg = source.HasProperty("_MainTexHSVG")
                ? source.GetVector("_MainTexHSVG")
                : new Vector4(0f, 1f, 1f, 1f);

            float gradationStrength = source.HasProperty("_MainGradationStrength")
                ? Mathf.Clamp01(source.GetFloat("_MainGradationStrength"))
                : 0f;
            Texture gradationTexture = gradationStrength > 0.0001f && source.HasProperty("_MainGradationTex")
                ? source.GetTexture("_MainGradationTex")
                : null;
            bool usesGradation = gradationTexture != null && gradationStrength > 0.0001f;

            bool hasToneAdjustment = Mathf.Abs(hsvg.x) > 0.0001f ||
                                     Mathf.Abs(hsvg.y - 1f) > 0.0001f ||
                                     Mathf.Abs(hsvg.z - 1f) > 0.0001f ||
                                     Mathf.Abs(hsvg.w - 1f) > 0.0001f;
            bool hasTint = snapshot.hasPrimaryColor && !ApproximatelyWhite(snapshot.primaryColor);
            Texture alphaMaskTexture = HasEnabledAlphaMask(source)
                ? FindSnapshotTexture(snapshot, AlphaMaskTexturePropertyNames)
                : null;
            bool usesAlphaMask = alphaMaskTexture != null && surfaceMode != MaterialSurfaceMode.Opaque;
            int alphaMaskMode = source.HasProperty("_AlphaMaskMode")
                ? Mathf.RoundToInt(source.GetFloat("_AlphaMaskMode"))
                : 2;
            float alphaMaskScale = source.HasProperty("_AlphaMaskScale")
                ? source.GetFloat("_AlphaMaskScale")
                : 1f;
            float alphaMaskValue = source.HasProperty("_AlphaMaskValue")
                ? source.GetFloat("_AlphaMaskValue")
                : 0f;

            if (!usesSecondLayer && !usesThirdLayer && !usesGradation && !hasToneAdjustment && !hasTint && !usesAlphaMask)
            {
                return null;
            }

            Texture2D readableMain = null;
            Texture2D readableAdjustMask = null;
            Texture2D readableGradation = null;
            Texture2D readableSecond = null;
            Texture2D readableSecondMask = null;
            Texture2D readableThird = null;
            Texture2D readableThirdMask = null;
            Texture2D readableAlphaMask = null;
            Texture2D output = null;

            try
            {
                int maxSize = ChooseTextureSize(snapshot.primaryTexture.name, options);
                int width = Mathf.Max(4, snapshot.primaryTexture.width);
                int height = Mathf.Max(4, snapshot.primaryTexture.height);
                float sizeScale = Mathf.Min(1f, maxSize / (float)Mathf.Max(width, height));
                width = Mathf.Max(4, Mathf.RoundToInt(width * sizeScale));
                height = Mathf.Max(4, Mathf.RoundToInt(height * sizeScale));

                readableMain = ReadTexture(snapshot.primaryTexture, width, height);
                if (readableMain == null)
                {
                    return null;
                }

                Texture adjustMask = source.HasProperty("_MainColorAdjustMask")
                    ? source.GetTexture("_MainColorAdjustMask")
                    : null;
                if (adjustMask != null)
                {
                    readableAdjustMask = ReadTexture(adjustMask, width, height);
                }

                if (usesGradation)
                {
                    int gradationWidth = Mathf.Max(2, gradationTexture.width);
                    int gradationHeight = Mathf.Max(2, gradationTexture.height);
                    readableGradation = ReadTexture(gradationTexture, gradationWidth, gradationHeight);
                }

                Texture secondTexture = usesSecondLayer && source.HasProperty("_Main2ndTex")
                    ? source.GetTexture("_Main2ndTex")
                    : null;
                Texture secondMask = usesSecondLayer && source.HasProperty("_Main2ndBlendMask")
                    ? source.GetTexture("_Main2ndBlendMask")
                    : null;
                if (secondTexture != null) readableSecond = ReadTexture(secondTexture, width, height);
                if (secondMask != null) readableSecondMask = ReadTexture(secondMask, width, height);

                Texture thirdTexture = usesThirdLayer && source.HasProperty("_Main3rdTex")
                    ? source.GetTexture("_Main3rdTex")
                    : null;
                Texture thirdMask = usesThirdLayer && source.HasProperty("_Main3rdBlendMask")
                    ? source.GetTexture("_Main3rdBlendMask")
                    : null;
                if (thirdTexture != null) readableThird = ReadTexture(thirdTexture, width, height);
                if (thirdMask != null) readableThirdMask = ReadTexture(thirdMask, width, height);
                if (usesAlphaMask)
                {
                    readableAlphaMask = ReadTexture(alphaMaskTexture, width, height);
                }

                Color[] pixels = readableMain.GetPixels();
                Color[] adjustMaskPixels = readableAdjustMask != null ? readableAdjustMask.GetPixels() : null;
                Color[] secondPixels = readableSecond != null ? readableSecond.GetPixels() : null;
                Color[] secondMaskPixels = readableSecondMask != null ? readableSecondMask.GetPixels() : null;
                Color[] thirdPixels = readableThird != null ? readableThird.GetPixels() : null;
                Color[] thirdMaskPixels = readableThirdMask != null ? readableThirdMask.GetPixels() : null;
                Color[] alphaMaskPixels = readableAlphaMask != null ? readableAlphaMask.GetPixels() : null;

                Color mainTint = snapshot.hasPrimaryColor ? snapshot.primaryColor : Color.white;
                Color secondTint = source.HasProperty("_Color2nd") ? source.GetColor("_Color2nd") : Color.white;
                Color thirdTint = source.HasProperty("_Color3rd") ? source.GetColor("_Color3rd") : Color.white;
                int secondBlendMode = source.HasProperty("_Main2ndTexBlendMode")
                    ? Mathf.RoundToInt(source.GetFloat("_Main2ndTexBlendMode"))
                    : 0;
                int thirdBlendMode = source.HasProperty("_Main3rdTexBlendMode")
                    ? Mathf.RoundToInt(source.GetFloat("_Main3rdTexBlendMode"))
                    : 0;

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color original = pixels[i];
                    Color adjusted = ApplyHsvg(original, hsvg);

                    // lilToonのメイングラデーションは、補正後の色を1次元LUTへ通して着色します。
                    // 白～グレーの髪テクスチャをグラデーションマップだけで着色する素材に必要です。
                    if (readableGradation != null && gradationStrength > 0.0001f)
                    {
                        float lookup = Mathf.Clamp01(adjusted.r);
                        Color gradation = readableGradation.GetPixelBilinear(lookup, 0.5f);
                        adjusted.r = Mathf.Lerp(adjusted.r, gradation.r, gradationStrength);
                        adjusted.g = Mathf.Lerp(adjusted.g, gradation.g, gradationStrength);
                        adjusted.b = Mathf.Lerp(adjusted.b, gradation.b, gradationStrength);
                    }

                    float adjustmentWeight = adjustMaskPixels != null ? Mathf.Clamp01(adjustMaskPixels[i].r) : 1f;
                    Color current = Color.Lerp(original, adjusted, adjustmentWeight);
                    current.r *= mainTint.r;
                    current.g *= mainTint.g;
                    current.b *= mainTint.b;
                    current.a *= mainTint.a;

                    if (secondPixels != null)
                    {
                        Color layer = MultiplyColor(secondPixels[i], secondTint);
                        float mask = secondMaskPixels != null ? Mathf.Clamp01(secondMaskPixels[i].r) : 1f;
                        current = BlendLayer(current, layer, mask, secondBlendMode);
                    }

                    if (thirdPixels != null)
                    {
                        Color layer = MultiplyColor(thirdPixels[i], thirdTint);
                        float mask = thirdMaskPixels != null ? Mathf.Clamp01(thirdMaskPixels[i].r) : 1f;
                        current = BlendLayer(current, layer, mask, thirdBlendMode);
                    }

                    if (alphaMaskPixels != null)
                    {
                        Color maskColor = alphaMaskPixels[i];
                        float mask = Mathf.Clamp01(Mathf.Max(maskColor.r, Mathf.Max(maskColor.g, maskColor.b)) *
                                                   maskColor.a * alphaMaskScale + alphaMaskValue);
                        if (alphaMaskMode == 1)
                        {
                            current.a = mask;
                        }
                        else if (alphaMaskMode == 3)
                        {
                            current.a = Mathf.Clamp01(current.a + mask - 1f);
                        }
                        else
                        {
                            current.a *= mask;
                        }
                    }

                    pixels[i] = ClampColor(current);
                }

                output = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
                output.name = snapshot.primaryTexture.name + "_QuestBaked";
                output.SetPixels(pixels);
                output.Apply(true, false);

                string destination = AssetDatabase.GenerateUniqueAssetPath(
                    textureFolder + "/" + SanitizeFileName(snapshot.primaryTexture.name) + "_QuestBaked.png");
                File.WriteAllBytes(Path.GetFullPath(destination), output.EncodeToPNG());
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter importer = AssetImporter.GetAtPath(destination) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = surfaceMode != MaterialSurfaceMode.Opaque;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = maxSize;

                    TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                    android.name = "Android";
                    android.overridden = true;
                    android.maxTextureSize = maxSize;
                    android.format = TextureImporterFormat.ASTC_6x6;
                    android.textureCompression = TextureImporterCompression.Compressed;
                    android.compressionQuality = 50;
                    importer.SetPlatformTextureSettings(android);
                    importer.SaveAndReimport();
                }

                Texture baked = AssetDatabase.LoadAssetAtPath<Texture>(destination);
                if (baked != null)
                {
                    result.changes.Add("lilToonの色調補正・レイヤーをQuest用テクスチャへ焼き込み: " + source.name);
                }
                return baked;
            }
            catch (Exception exception)
            {
                result.warnings.Add("色を焼き込めなかったため通常変換を使用します: " + source.name + " (" + exception.Message + ")");
                return null;
            }
            finally
            {
                if (readableMain != null) UnityEngine.Object.DestroyImmediate(readableMain);
                if (readableAdjustMask != null) UnityEngine.Object.DestroyImmediate(readableAdjustMask);
                if (readableGradation != null) UnityEngine.Object.DestroyImmediate(readableGradation);
                if (readableSecond != null) UnityEngine.Object.DestroyImmediate(readableSecond);
                if (readableSecondMask != null) UnityEngine.Object.DestroyImmediate(readableSecondMask);
                if (readableThird != null) UnityEngine.Object.DestroyImmediate(readableThird);
                if (readableThirdMask != null) UnityEngine.Object.DestroyImmediate(readableThirdMask);
                if (readableAlphaMask != null) UnityEngine.Object.DestroyImmediate(readableAlphaMask);
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
            }
        }

        private static bool GetMaterialToggle(Material material, string propertyName)
        {
            return material != null && material.HasProperty(propertyName) && material.GetFloat(propertyName) > 0.5f;
        }

        private static bool ApproximatelyWhite(Color color)
        {
            return Mathf.Abs(color.r - 1f) < 0.0001f &&
                   Mathf.Abs(color.g - 1f) < 0.0001f &&
                   Mathf.Abs(color.b - 1f) < 0.0001f &&
                   Mathf.Abs(color.a - 1f) < 0.0001f;
        }

        private static Texture2D ReadTexture(Texture source, int width, int height)
        {
            if (source == null)
            {
                return null;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Color ApplyHsvg(Color color, Vector4 hsvg)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            hue = Mathf.Repeat(hue + hsvg.x, 1f);
            saturation = Mathf.Clamp01(saturation * Mathf.Max(0f, hsvg.y));
            value = Mathf.Max(0f, value * Mathf.Max(0f, hsvg.z));
            Color converted = Color.HSVToRGB(hue, saturation, value, true);
            float gamma = Mathf.Max(0.0001f, hsvg.w);
            converted.r = Mathf.Pow(Mathf.Max(0f, converted.r), 1f / gamma);
            converted.g = Mathf.Pow(Mathf.Max(0f, converted.g), 1f / gamma);
            converted.b = Mathf.Pow(Mathf.Max(0f, converted.b), 1f / gamma);
            converted.a = color.a;
            return converted;
        }

        private static Color MultiplyColor(Color textureColor, Color tint)
        {
            return new Color(
                textureColor.r * tint.r,
                textureColor.g * tint.g,
                textureColor.b * tint.b,
                textureColor.a * tint.a);
        }

        private static Color BlendLayer(Color destination, Color source, float mask, int blendMode)
        {
            float alpha = Mathf.Clamp01(source.a * mask);
            Color blended;
            switch (blendMode)
            {
                case 1:
                    blended = new Color(
                        destination.r + source.r,
                        destination.g + source.g,
                        destination.b + source.b,
                        destination.a);
                    break;
                case 2:
                    blended = new Color(
                        1f - (1f - destination.r) * (1f - source.r),
                        1f - (1f - destination.g) * (1f - source.g),
                        1f - (1f - destination.b) * (1f - source.b),
                        destination.a);
                    break;
                case 3:
                    blended = new Color(
                        destination.r * source.r,
                        destination.g * source.g,
                        destination.b * source.b,
                        destination.a);
                    break;
                default:
                    blended = source;
                    break;
            }

            Color result = Color.Lerp(destination, blended, alpha);
            result.a = Mathf.Clamp01(destination.a + alpha * (1f - destination.a));
            return result;
        }

        private static Color ClampColor(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a));
        }

        private static void WriteReport(
            string assetPath,
            NekoQuestAnalysis analysis,
            NekoQuestGenerationOptions options,
            NekoQuestGenerationResult result)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);

            NekoQuestAnalyzer.RecalculatePlanSummary(analysis,
                out int keptComponents,
                out int keptTransforms,
                out int keptColliders,
                out int keptChecks);

            List<string> lines = new List<string>
            {
                "NN - Quest Converter v1.0.0",
                "生成日時: " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                "元アバター: " + analysis.avatarRoot.name,
                "変換方針: " + NekoQuestLimits.ForMode(options.targetMode).label,
                string.Empty,
                "【解析値】",
                "三角形: " + analysis.triangles,
                "Skinned Mesh: " + analysis.skinnedMeshes,
                "Basic Mesh: " + analysis.basicMeshes,
                "マテリアルスロット: " + analysis.materialSlots,
                "推定テクスチャメモリ: " + FormatBytes(analysis.textureMemoryBytes),
                "ボーン: " + analysis.bones,
                string.Empty,
                "【Quest版で残す揺れもの】",
                "PhysBone: " + keptComponents,
                "影響Transform: " + keptTransforms,
                "Collider参照合計: " + keptColliders,
                "Collision Check推定: " + keptChecks,
                string.Empty
            };

            for (int i = 0; i < analysis.physBones.Count; i++)
            {
                NekoPhysBoneItem item = analysis.physBones[i];
                string status = item.keep
                    ? (item.keepCollisions ? "維持" : "揺れのみ維持（当たり判定軽量化）")
                    : "停止";
                lines.Add("- " + item.displayName + ": " + status);
            }

            lines.Add(string.Empty);
            lines.Add("【自動変更】");
            if (result.changes.Count == 0) lines.Add("- なし");
            for (int i = 0; i < result.changes.Count; i++) lines.Add("- " + result.changes[i]);

            lines.Add(string.Empty);
            lines.Add("【確認が必要な点】");
            if (result.warnings.Count == 0) lines.Add("- なし");
            for (int i = 0; i < result.warnings.Count; i++) lines.Add("- " + result.warnings[i]);

            lines.Add(string.Empty);
            lines.Add("注意: このツールはメッシュ結合・テクスチャアトラス化・ポリゴン削減を自動では行いません。");
            lines.Add("VRChat SDKのBuilderで最終的なAndroid判定を必ず確認してください。");

            File.WriteAllLines(absolutePath, lines.ToArray());
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] parts = normalized.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                throw new InvalidOperationException("保存先はAssetsフォルダー内にしてください。" + normalized);
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
            }
            if (bytes >= 1024L)
            {
                return (bytes / 1024f).ToString("0.0") + " KB";
            }
            return bytes + " B";
        }

        private static string SanitizeFileName(string value)
        {
            string result = string.IsNullOrEmpty(value) ? "Avatar" : value;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                result = result.Replace(invalid[i], '_');
            }
            result = result.Replace('/', '_').Replace('\\', '_').Trim();
            return string.IsNullOrEmpty(result) ? "Avatar" : result;
        }
    }
}
#endif
