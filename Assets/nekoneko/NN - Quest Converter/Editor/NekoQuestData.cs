#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoNeko.QuestConverter
{
    internal enum NekoQuestTargetMode
    {
        Appearance,
        Balanced,
        Lightweight
    }

    [Serializable]
    internal sealed class NekoQuestLimits
    {
        public string label;
        public int triangles;
        public long textureMemoryBytes;
        public int skinnedMeshes;
        public int basicMeshes;
        public int materialSlots;
        public int animators;
        public int bones;
        public int physBoneComponents;
        public int physBoneTransforms;
        public int physBoneColliders;
        public int physBoneCollisionChecks;
        public int contacts;
        public int particles;
        public int trailRenderers;
        public int lineRenderers;

        public static NekoQuestLimits ForMode(NekoQuestTargetMode mode)
        {
            switch (mode)
            {
                case NekoQuestTargetMode.Appearance:
                    return new NekoQuestLimits
                    {
                        label = "見た目優先（Poor以内）",
                        triangles = 20000,
                        textureMemoryBytes = 40L * 1024L * 1024L,
                        skinnedMeshes = 2,
                        basicMeshes = 2,
                        materialSlots = 4,
                        animators = 2,
                        bones = 150,
                        physBoneComponents = 8,
                        physBoneTransforms = 64,
                        physBoneColliders = 16,
                        physBoneCollisionChecks = 64,
                        contacts = 16,
                        particles = 2,
                        trailRenderers = 1,
                        lineRenderers = 1
                    };

                case NekoQuestTargetMode.Lightweight:
                    return new NekoQuestLimits
                    {
                        label = "軽さ優先（Good目標）",
                        triangles = 10000,
                        textureMemoryBytes = 18L * 1024L * 1024L,
                        skinnedMeshes = 1,
                        basicMeshes = 1,
                        materialSlots = 1,
                        animators = 1,
                        bones = 90,
                        physBoneComponents = 4,
                        physBoneTransforms = 16,
                        physBoneColliders = 4,
                        physBoneCollisionChecks = 16,
                        contacts = 4,
                        particles = 0,
                        trailRenderers = 0,
                        lineRenderers = 0
                    };

                default:
                    return new NekoQuestLimits
                    {
                        label = "おすすめ（Medium目標）",
                        triangles = 15000,
                        textureMemoryBytes = 25L * 1024L * 1024L,
                        skinnedMeshes = 2,
                        basicMeshes = 2,
                        materialSlots = 2,
                        animators = 1,
                        bones = 150,
                        physBoneComponents = 6,
                        physBoneTransforms = 32,
                        physBoneColliders = 8,
                        physBoneCollisionChecks = 32,
                        contacts = 8,
                        particles = 0,
                        trailRenderers = 0,
                        lineRenderers = 0
                    };
            }
        }
    }

    [Serializable]
    internal sealed class NekoPhysBoneItem
    {
        public string path;
        public string displayName;
        public string category;
        public string componentTypeName;
        public int componentIndex;
        public int priority;
        public bool keep = true;
        public bool keepCollisions = true;
        public int affectedTransforms;
        public int colliderCount;
        public int collisionChecks;
        public string note;
    }

    [Serializable]
    internal sealed class NekoQuestIssue
    {
        public enum Severity
        {
            Info,
            Warning,
            Error
        }

        public Severity severity;
        public string title;
        public string detail;
        public UnityEngine.Object context;
    }

    [Serializable]
    internal sealed class NekoQuestAnalysis
    {
        public GameObject avatarRoot;
        public Component avatarDescriptor;
        public bool isValid;
        public int triangles;
        public long textureMemoryBytes;
        public int skinnedMeshes;
        public int basicMeshes;
        public int materialSlots;
        public int animators;
        public int bones;
        public int physBoneComponents;
        public int physBoneTransforms;
        public int physBoneColliders;
        public int physBoneCollisionChecks;
        public int contacts;
        public int particleSystems;
        public int trailRenderers;
        public int lineRenderers;
        public int unsupportedComponents;
        public int nonMobileMaterials;
        public readonly List<NekoPhysBoneItem> physBones = new List<NekoPhysBoneItem>();
        public readonly List<NekoQuestIssue> issues = new List<NekoQuestIssue>();
    }

    [Serializable]
    internal sealed class NekoQuestGenerationOptions
    {
        public NekoQuestTargetMode targetMode = NekoQuestTargetMode.Balanced;
        public bool convertMaterials = true;
        public bool duplicateAndCompressTextures = true;
        public bool enforceAndroidUploadSize = true;
        public bool removeUnsupportedComponents = true;
        public bool trimContactsToLimit = true;
        public bool trimParticlesToLimit = true;
        public bool setupPerPlatformOverride = false;
        public bool keepGeneratedAvatarInScene = true;
        public int defaultTextureSize = 1024;
        public int importantTextureSize = 2048;
        public int smallTextureSize = 512;
        public string outputRoot = "Assets/nekoneko/NN - Quest Converter/Generated";
    }

    internal sealed class NekoQuestGenerationResult
    {
        public bool success;
        public string message;
        public GameObject generatedAvatar;
        public string prefabPath;
        public string reportPath;
        public string outputFolder;
        public bool overrideConfigured;
        public readonly List<string> changes = new List<string>();
        public readonly List<string> warnings = new List<string>();
    }
}
#endif
