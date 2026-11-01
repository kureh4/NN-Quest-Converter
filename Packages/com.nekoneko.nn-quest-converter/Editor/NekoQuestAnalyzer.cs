#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;

namespace NekoNeko.QuestConverter
{
    internal static class NekoQuestAnalyzer
    {
        private static readonly string[] ImportantKeywords =
        {
            "face", "head", "eye", "eyes", "hair", "bang", "front", "ear", "mimi",
            "顔", "頭", "目", "瞳", "髪", "前髪", "耳"
        };

        private static readonly string[] MediumKeywords =
        {
            "tail", "skirt", "chest", "breast", "bust", "ribbon", "sleeve", "hood", "coat",
            "しっぽ", "尻尾", "スカート", "胸", "リボン", "袖"
        };

        internal static NekoQuestAnalysis Analyze(GameObject selected, NekoQuestTargetMode targetMode)
        {
            NekoQuestAnalysis analysis = new NekoQuestAnalysis();
            GameObject avatarRoot = NekoQuestSdkBridge.NormalizeAvatarRoot(selected);
            analysis.avatarRoot = avatarRoot;

            if (avatarRoot == null)
            {
                analysis.issues.Add(new NekoQuestIssue
                {
                    severity = NekoQuestIssue.Severity.Error,
                    title = "アバターが選択されていません",
                    detail = "HierarchyにあるPC版アバターの一番上を選択してください。"
                });
                return analysis;
            }

            if (EditorUtility.IsPersistent(avatarRoot))
            {
                analysis.issues.Add(new NekoQuestIssue
                {
                    severity = NekoQuestIssue.Severity.Error,
                    title = "Prefabファイルが直接選択されています",
                    detail = "PrefabをHierarchyへ置いてから、Hierarchy上のアバターを選択してください。",
                    context = avatarRoot
                });
                return analysis;
            }

            analysis.avatarDescriptor = NekoQuestSdkBridge.GetAvatarDescriptor(avatarRoot);
            if (analysis.avatarDescriptor == null)
            {
                analysis.issues.Add(new NekoQuestIssue
                {
                    severity = NekoQuestIssue.Severity.Error,
                    title = "VRC Avatar Descriptorが見つかりません",
                    detail = "VRChat用アバターの一番上のオブジェクトを選択してください。",
                    context = avatarRoot
                });
                return analysis;
            }

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            HashSet<Texture> textures = new HashSet<Texture>();
            HashSet<Transform> bones = new HashSet<Transform>();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer is SkinnedMeshRenderer)
                {
                    SkinnedMeshRenderer skinned = (SkinnedMeshRenderer)renderer;
                    analysis.skinnedMeshes++;
                    analysis.triangles += CountTriangles(skinned.sharedMesh);
                    if (skinned.bones != null)
                    {
                        for (int b = 0; b < skinned.bones.Length; b++)
                        {
                            if (skinned.bones[b] != null)
                            {
                                bones.Add(skinned.bones[b]);
                            }
                        }
                    }
                    if (skinned.rootBone != null)
                    {
                        bones.Add(skinned.rootBone);
                    }
                }
                else if (renderer is MeshRenderer)
                {
                    analysis.basicMeshes++;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null)
                    {
                        analysis.triangles += CountTriangles(filter.sharedMesh);
                    }
                }

                Material[] materials = renderer.sharedMaterials;
                analysis.materialSlots += materials != null ? materials.Length : 0;
                if (materials == null)
                {
                    continue;
                }

                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    if (!IsMobileMaterial(material))
                    {
                        analysis.nonMobileMaterials++;
                    }

                    CollectMaterialTextures(material, textures);
                }
            }

            foreach (Texture texture in textures)
            {
                analysis.textureMemoryBytes += GetTextureMemorySize(texture);
            }

            Animator[] animators = avatarRoot.GetComponentsInChildren<Animator>(true);
            analysis.animators = animators.Length;
            analysis.bones = bones.Count > 0 ? bones.Count : CountLikelyBones(avatarRoot.transform);

            List<Component> physBones = NekoQuestSdkBridge.GetComponentsByTypeName(avatarRoot, NekoQuestSdkBridge.IsPhysBone);
            List<Component> physBoneColliders = NekoQuestSdkBridge.GetComponentsByTypeName(avatarRoot, NekoQuestSdkBridge.IsPhysBoneCollider);
            List<Component> contacts = NekoQuestSdkBridge.GetComponentsByTypeName(avatarRoot, NekoQuestSdkBridge.IsContact);

            analysis.physBoneComponents = physBones.Count;
            analysis.physBoneColliders = physBoneColliders.Count;
            analysis.contacts = contacts.Count;

            Dictionary<GameObject, Dictionary<string, int>> typeIndices = new Dictionary<GameObject, Dictionary<string, int>>();
            for (int i = 0; i < physBones.Count; i++)
            {
                Component physBone = physBones[i];
                int affected = NekoQuestSdkBridge.CountPhysBoneTransforms(physBone);
                int colliderCount = NekoQuestSdkBridge.GetPhysBoneColliders(physBone).Count;
                int checks = affected * colliderCount;
                analysis.physBoneTransforms += affected;
                analysis.physBoneCollisionChecks += checks;

                string typeName = physBone.GetType().FullName;
                Dictionary<string, int> perType;
                if (!typeIndices.TryGetValue(physBone.gameObject, out perType))
                {
                    perType = new Dictionary<string, int>();
                    typeIndices[physBone.gameObject] = perType;
                }

                int componentIndex;
                if (!perType.TryGetValue(typeName, out componentIndex))
                {
                    componentIndex = 0;
                }
                perType[typeName] = componentIndex + 1;

                NekoPhysBoneItem item = new NekoPhysBoneItem
                {
                    path = NekoQuestSdkBridge.GetRelativePath(avatarRoot.transform, physBone.transform),
                    displayName = BuildFriendlyName(physBone),
                    category = DetectCategory(physBone),
                    componentTypeName = typeName,
                    componentIndex = componentIndex,
                    priority = DetectPriority(physBone),
                    affectedTransforms = affected,
                    colliderCount = colliderCount,
                    collisionChecks = checks,
                    keep = true,
                    keepCollisions = true
                };
                analysis.physBones.Add(item);
            }

            analysis.particleSystems = avatarRoot.GetComponentsInChildren<ParticleSystem>(true).Length;
            analysis.trailRenderers = avatarRoot.GetComponentsInChildren<TrailRenderer>(true).Length;
            analysis.lineRenderers = avatarRoot.GetComponentsInChildren<LineRenderer>(true).Length;
            analysis.unsupportedComponents = CountUnsupportedComponents(avatarRoot);

            AutoPlanPhysBones(analysis, NekoQuestLimits.ForMode(targetMode));
            AddIssues(analysis, NekoQuestLimits.ForMode(targetMode));
            analysis.isValid = !analysis.issues.Any(issue => issue.severity == NekoQuestIssue.Severity.Error);
            return analysis;
        }

        internal static void AutoPlanPhysBones(NekoQuestAnalysis analysis, NekoQuestLimits limits)
        {
            if (analysis == null || limits == null)
            {
                return;
            }

            List<NekoPhysBoneItem> ordered = analysis.physBones
                .OrderByDescending(item => item.priority)
                .ThenBy(item => item.affectedTransforms)
                .ThenBy(item => item.path)
                .ToList();

            int componentCount = 0;
            int transformCount = 0;
            int colliderCount = 0;
            int collisionChecks = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                NekoPhysBoneItem item = ordered[i];
                item.note = string.Empty;

                bool componentFits = componentCount + 1 <= limits.physBoneComponents;
                bool transformsFit = transformCount + item.affectedTransforms <= limits.physBoneTransforms;
                if (!componentFits || !transformsFit)
                {
                    item.keep = false;
                    item.keepCollisions = false;
                    item.note = !componentFits ? "個数上限のためQuest版では停止" : "揺れるボーン数の上限のためQuest版では停止";
                    continue;
                }

                item.keep = true;
                componentCount++;
                transformCount += item.affectedTransforms;

                bool collisionsFit = colliderCount + item.colliderCount <= limits.physBoneColliders &&
                                     collisionChecks + item.collisionChecks <= limits.physBoneCollisionChecks;
                if (collisionsFit)
                {
                    item.keepCollisions = true;
                    colliderCount += item.colliderCount;
                    collisionChecks += item.collisionChecks;
                }
                else
                {
                    item.keepCollisions = false;
                    item.note = "揺れは残し、当たり判定だけ軽量化";
                }
            }
        }

        internal static void RecalculatePlanSummary(NekoQuestAnalysis analysis, out int keptComponents, out int keptTransforms, out int keptColliders, out int keptChecks)
        {
            keptComponents = 0;
            keptTransforms = 0;
            keptColliders = 0;
            keptChecks = 0;
            if (analysis == null)
            {
                return;
            }

            for (int i = 0; i < analysis.physBones.Count; i++)
            {
                NekoPhysBoneItem item = analysis.physBones[i];
                if (!item.keep)
                {
                    continue;
                }

                keptComponents++;
                keptTransforms += item.affectedTransforms;
                if (item.keepCollisions)
                {
                    keptColliders += item.colliderCount;
                    keptChecks += item.collisionChecks;
                }
            }
        }

        internal static bool IsMobileMaterial(Material material)
        {
            if (material == null || material.shader == null)
            {
                return false;
            }

            // 「VRChat/Mobile/」配下の全シェーダーがQuestアバターで使えるわけではありません。
            // SDKの現行許可リストを明示し、旧Alpha Blendedや旧Unlit Transparentを
            // 対応済みと誤判定しないようにします。
            switch (material.shader.name)
            {
                case "VRChat/Mobile/Toon Standard":
                case "VRChat/Mobile/Toon Standard (Outline)":
                case "VRChat/Mobile/Standard Lite":
                case "VRChat/Mobile/Bumped Diffuse":
                case "VRChat/Mobile/Bumped Mapped Specular":
                case "VRChat/Mobile/Diffuse":
                case "VRChat/Mobile/MatCap Lit":
                case "VRChat/Mobile/Toon Lit":
                case "VRChat/Mobile/Particles/Additive":
                case "VRChat/Mobile/Particles/Multiply":
                    return true;
                default:
                    return false;
            }
        }

        private static void AddIssues(NekoQuestAnalysis analysis, NekoQuestLimits limits)
        {
            if (analysis.nonMobileMaterials > 0)
            {
                analysis.issues.Add(new NekoQuestIssue
                {
                    severity = NekoQuestIssue.Severity.Warning,
                    title = "Quest非対応シェーダーを検出",
                    detail = analysis.nonMobileMaterials + "個のマテリアルをQuest用シェーダーへ変換します。"
                });
            }

            if (analysis.unsupportedComponents > 0)
            {
                analysis.issues.Add(new NekoQuestIssue
                {
                    severity = NekoQuestIssue.Severity.Warning,
                    title = "Questで使えない機能を検出",
                    detail = analysis.unsupportedComponents + "個のコンポーネントをQuest版のコピーから除去します。PC版は変更しません。"
                });
            }

            AddLimitIssue(analysis, "三角形", analysis.triangles, limits.triangles,
                "このバージョンではポリゴン自動削減を行いません。Blenderなどでの軽量化が必要になる場合があります。");
            AddLimitIssue(analysis, "Skinned Mesh", analysis.skinnedMeshes, limits.skinnedMeshes,
                "メッシュ結合は自動では行いません。SDKの判定で目標ランクを超える可能性があります。");
            AddLimitIssue(analysis, "マテリアルスロット", analysis.materialSlots, limits.materialSlots,
                "シェーダー変換は行いますが、テクスチャアトラス化やマテリアル結合は行いません。");
            AddLimitIssue(analysis, "ボーン", analysis.bones, limits.bones,
                "基本のArmatureは保持します。Quest版とPC版の互換性を壊さないため、自動削除しません。");

            if (analysis.textureMemoryBytes > limits.textureMemoryBytes)
            {
                analysis.issues.Add(new NekoQuestIssue
                {
                    severity = NekoQuestIssue.Severity.Warning,
                    title = "テクスチャ容量が目標を超えています",
                    detail = "Quest用コピーを圧縮します。実際のSDK表示容量はUnityのビルド結果で確認してください。"
                });
            }
        }

        private static void AddLimitIssue(NekoQuestAnalysis analysis, string label, int value, int limit, string guidance)
        {
            if (value <= limit)
            {
                return;
            }

            analysis.issues.Add(new NekoQuestIssue
            {
                severity = NekoQuestIssue.Severity.Warning,
                title = label + "が目標値を超えています（" + value + " / " + limit + "）",
                detail = guidance
            });
        }

        private static int CountUnsupportedComponents(GameObject root)
        {
            int count = 0;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                if (NekoQuestSdkBridge.IsVrcConstraint(type))
                {
                    continue;
                }

                if (component is Light || component is Camera || component is AudioSource || component is Cloth ||
                    component is Rigidbody || component is Collider || component is Joint ||
                    component is IConstraint || NekoQuestSdkBridge.IsDynamicBone(type) || NekoQuestSdkBridge.IsFinalIk(type))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountTriangles(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            long total = 0;
            try
            {
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (mesh.GetTopology(i) == MeshTopology.Triangles)
                    {
                        total += (long)mesh.GetIndexCount(i) / 3L;
                    }
                }
            }
            catch
            {
                try
                {
                    total = mesh.triangles.Length / 3L;
                }
                catch
                {
                    total = 0;
                }
            }

            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private static int CountLikelyBones(Transform root)
        {
            if (root == null)
            {
                return 0;
            }

            int count = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                string lower = transforms[i].name.ToLowerInvariant();
                if (lower.Contains("bone") || lower.Contains("armature") || lower.Contains("hips") ||
                    lower.Contains("spine") || lower.Contains("head") || lower.Contains("hand") ||
                    lower.Contains("foot") || lower.Contains("hair") || lower.Contains("tail"))
                {
                    count++;
                }
            }
            return count;
        }

        private static void CollectMaterialTextures(Material material, HashSet<Texture> textures)
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
                Texture texture = material.GetTexture(propertyName);
                if (texture != null)
                {
                    textures.Add(texture);
                }
            }
        }

        private static long GetTextureMemorySize(Texture texture)
        {
            if (texture == null)
            {
                return 0;
            }

            try
            {
                Type textureUtil = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil");
                MethodInfo method = textureUtil != null
                    ? textureUtil.GetMethod("GetStorageMemorySizeLong", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                if (method != null)
                {
                    object result = method.Invoke(null, new object[] { texture });
                    if (result is long)
                    {
                        return (long)result;
                    }
                }
            }
            catch
            {
                // Use estimate below.
            }

            long baseBytes = (long)Mathf.Max(1, texture.width) * Mathf.Max(1, texture.height) * 4L;
            return (long)(baseBytes * 1.3334f);
        }

        private static string BuildFriendlyName(Component physBone)
        {
            string category = DetectCategory(physBone);
            string objectName = physBone != null ? physBone.gameObject.name : "揺れもの";
            return category + " — " + objectName;
        }

        private static string DetectCategory(Component physBone)
        {
            string value = BuildSearchText(physBone);
            if (ContainsAny(value, new[] { "hair", "bang", "髪", "前髪" })) return "髪";
            if (ContainsAny(value, new[] { "ear", "mimi", "耳" })) return "耳";
            if (ContainsAny(value, new[] { "tail", "しっぽ", "尻尾" })) return "しっぽ";
            if (ContainsAny(value, new[] { "skirt", "スカート" })) return "スカート";
            if (ContainsAny(value, new[] { "chest", "breast", "bust", "胸" })) return "胸";
            if (ContainsAny(value, new[] { "ribbon", "リボン" })) return "リボン";
            if (ContainsAny(value, new[] { "sleeve", "袖" })) return "袖";
            if (ContainsAny(value, new[] { "cloth", "coat", "hood", "服", "コート" })) return "衣装";
            return "その他";
        }

        private static int DetectPriority(Component physBone)
        {
            string value = BuildSearchText(physBone);
            if (ContainsAny(value, ImportantKeywords)) return 100;
            if (ContainsAny(value, MediumKeywords)) return 80;
            return 50;
        }

        private static string BuildSearchText(Component physBone)
        {
            if (physBone == null)
            {
                return string.Empty;
            }

            Transform root = NekoQuestSdkBridge.GetPhysBoneRoot(physBone);
            string rootName = root != null ? root.name : string.Empty;
            return (physBone.gameObject.name + " " + rootName).ToLowerInvariant();
        }

        private static bool ContainsAny(string value, IEnumerable<string> keywords)
        {
            foreach (string keyword in keywords)
            {
                if (value.IndexOf(keyword.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
