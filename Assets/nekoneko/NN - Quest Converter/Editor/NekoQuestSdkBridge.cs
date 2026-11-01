#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NekoNeko.QuestConverter
{
    internal static class NekoQuestSdkBridge
    {
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly string[] AvatarDescriptorTypeNames =
        {
            "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"
        };

        internal static Type FindType(params string[] fullNames)
        {
            if (fullNames == null)
            {
                return null;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (string fullName in fullNames)
            {
                if (string.IsNullOrEmpty(fullName))
                {
                    continue;
                }

                Type direct = Type.GetType(fullName, false);
                if (direct != null)
                {
                    return direct;
                }

                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type type = assemblies[i].GetType(fullName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        internal static bool IsSdkInstalled()
        {
            return FindType(AvatarDescriptorTypeNames) != null;
        }

        internal static Component GetAvatarDescriptor(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                return null;
            }

            Type descriptorType = FindType(AvatarDescriptorTypeNames);
            if (descriptorType == null)
            {
                return null;
            }

            Component direct = avatarRoot.GetComponent(descriptorType);
            if (direct != null)
            {
                return direct;
            }

            return avatarRoot.GetComponentInChildren(descriptorType, true);
        }

        internal static GameObject NormalizeAvatarRoot(GameObject selected)
        {
            if (selected == null)
            {
                return null;
            }

            Type descriptorType = FindType(AvatarDescriptorTypeNames);
            if (descriptorType == null)
            {
                return selected;
            }

            Transform cursor = selected.transform;
            while (cursor != null)
            {
                if (cursor.GetComponent(descriptorType) != null)
                {
                    return cursor.gameObject;
                }
                cursor = cursor.parent;
            }

            Component childDescriptor = selected.GetComponentInChildren(descriptorType, true);
            return childDescriptor != null ? childDescriptor.gameObject : selected;
        }

        internal static List<Component> GetComponentsByTypeName(GameObject root, Predicate<Type> predicate)
        {
            List<Component> result = new List<Component>();
            if (root == null || predicate == null)
            {
                return result;
            }

            Component[] all = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Component component = all[i];
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                if (predicate(type))
                {
                    result.Add(component);
                }
            }

            return result;
        }

        internal static bool IsPhysBone(Type type)
        {
            string name = type != null ? type.FullName : string.Empty;
            return !string.IsNullOrEmpty(name) &&
                   (name.EndsWith(".VRCPhysBone", StringComparison.Ordinal) || name == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
        }

        internal static bool IsPhysBoneCollider(Type type)
        {
            string name = type != null ? type.FullName : string.Empty;
            return !string.IsNullOrEmpty(name) && name.IndexOf("VRCPhysBoneCollider", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsContact(Type type)
        {
            string name = type != null ? type.FullName : string.Empty;
            return !string.IsNullOrEmpty(name) &&
                   (name.IndexOf("VRCContactSender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("VRCContactReceiver", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool IsVrcConstraint(Type type)
        {
            string name = type != null ? type.FullName : string.Empty;
            return !string.IsNullOrEmpty(name) && name.IndexOf("VRCConstraint", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsDynamicBone(Type type)
        {
            string name = type != null ? type.FullName : string.Empty;
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf("DynamicBone", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   name.IndexOf("PhysBone", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal static bool IsFinalIk(Type type)
        {
            string name = type != null ? type.FullName : string.Empty;
            return !string.IsNullOrEmpty(name) &&
                   (name.StartsWith("RootMotion.FinalIK", StringComparison.Ordinal) ||
                    name.IndexOf("FinalIK", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static object GetMemberValue(object target, params string[] names)
        {
            if (target == null || names == null)
            {
                return null;
            }

            Type type = target.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                FieldInfo field = type.GetField(name, AnyInstance);
                if (field != null)
                {
                    return field.GetValue(target);
                }

                PropertyInfo property = type.GetProperty(name, AnyInstance);
                if (property != null && property.CanRead)
                {
                    try
                    {
                        return property.GetValue(target, null);
                    }
                    catch
                    {
                        // Ignore SDK proxy/property failures and try another name.
                    }
                }
            }

            return null;
        }

        internal static bool SetMemberValue(object target, object value, params string[] names)
        {
            if (target == null || names == null)
            {
                return false;
            }

            Type type = target.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                FieldInfo field = type.GetField(name, AnyInstance);
                if (field != null)
                {
                    try
                    {
                        field.SetValue(target, value);
                        return true;
                    }
                    catch
                    {
                        // Try property or another candidate name.
                    }
                }

                PropertyInfo property = type.GetProperty(name, AnyInstance);
                if (property != null && property.CanWrite)
                {
                    try
                    {
                        property.SetValue(target, value, null);
                        return true;
                    }
                    catch
                    {
                        // Try another candidate name.
                    }
                }
            }

            return false;
        }

        internal static Transform GetPhysBoneRoot(Component physBone)
        {
            if (physBone == null)
            {
                return null;
            }

            Transform root = GetMemberValue(physBone, "rootTransform", "RootTransform") as Transform;
            return root != null ? root : physBone.transform;
        }

        internal static HashSet<Transform> GetPhysBoneIgnoredTransforms(Component physBone)
        {
            HashSet<Transform> ignored = new HashSet<Transform>();
            object value = GetMemberValue(physBone, "ignoreTransforms", "IgnoreTransforms");
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
            {
                return ignored;
            }

            foreach (object item in enumerable)
            {
                Transform transform = item as Transform;
                if (transform != null)
                {
                    ignored.Add(transform);
                }
            }

            return ignored;
        }

        internal static List<Component> GetPhysBoneColliders(Component physBone)
        {
            List<Component> colliders = new List<Component>();
            object value = GetMemberValue(physBone, "colliders", "Colliders");
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
            {
                return colliders;
            }

            foreach (object item in enumerable)
            {
                Component component = item as Component;
                if (component != null)
                {
                    colliders.Add(component);
                }
            }

            return colliders;
        }

        internal static int CountPhysBoneTransforms(Component physBone)
        {
            Transform root = GetPhysBoneRoot(physBone);
            if (root == null)
            {
                return 0;
            }

            HashSet<Transform> ignored = GetPhysBoneIgnoredTransforms(physBone);
            int count = 0;
            CountTransformRecursive(root, ignored, ref count);
            return count;
        }

        private static void CountTransformRecursive(Transform current, HashSet<Transform> ignored, ref int count)
        {
            if (current == null || ignored.Contains(current))
            {
                return;
            }

            count++;
            for (int i = 0; i < current.childCount; i++)
            {
                CountTransformRecursive(current.GetChild(i), ignored, ref count);
            }
        }

        internal static bool ClearPhysBoneColliders(Component physBone)
        {
            if (physBone == null)
            {
                return false;
            }

            try
            {
                SerializedObject serializedObject = new SerializedObject(physBone);
                SerializedProperty property = serializedObject.FindProperty("colliders");
                if (property != null && property.isArray)
                {
                    property.arraySize = 0;
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(physBone);
                    return true;
                }
            }
            catch
            {
                // Reflection fallback below.
            }

            object value = GetMemberValue(physBone, "colliders", "Colliders");
            IList list = value as IList;
            if (list != null)
            {
                try
                {
                    list.Clear();
                    EditorUtility.SetDirty(physBone);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        internal static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (root == target)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Transform cursor = target;
            while (cursor != null && cursor != root)
            {
                parts.Add(cursor.name);
                cursor = cursor.parent;
            }

            if (cursor != root)
            {
                return string.Empty;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        internal static Transform FindByRelativePath(Transform root, string path)
        {
            if (root == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(path))
            {
                return root;
            }

            return root.Find(path);
        }

        internal static Component FindMatchingComponent(GameObject avatarRoot, NekoPhysBoneItem item)
        {
            if (avatarRoot == null || item == null)
            {
                return null;
            }

            Transform target = FindByRelativePath(avatarRoot.transform, item.path);
            if (target == null)
            {
                return null;
            }

            Component[] components = target.GetComponents<Component>();
            int index = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.GetType().FullName != item.componentTypeName)
                {
                    continue;
                }

                if (index == item.componentIndex)
                {
                    return component;
                }
                index++;
            }

            return null;
        }

        internal static bool TryConfigureAndroidOverride(GameObject pcAvatar, GameObject androidAvatar, out string error)
        {
            error = null;
            if (pcAvatar == null || androidAvatar == null)
            {
                error = "PC版またはQuest版アバターが見つかりません。";
                return false;
            }

            Component androidDescriptor = GetAvatarDescriptor(androidAvatar);
            if (androidDescriptor == null)
            {
                error = "Quest版のVRC Avatar Descriptorが見つかりません。";
                return false;
            }

            Type overridesType = FindType("VRC.SDK3A.Editor.PerPlatformOverrides");
            if (overridesType == null)
            {
                error = "VRChat SDKのPer-Platform Overrides APIが見つかりません。SDKを最新版へ更新してください。";
                return false;
            }

            Type optionType = overridesType.GetNestedType("Option", BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getMethod = overridesType.GetMethod("GetPlatformOverrides", AnyStatic);
            MethodInfo setMethod = overridesType.GetMethod("SetPlatformOverrides", AnyStatic);
            if (optionType == null || setMethod == null)
            {
                error = "Per-Platform Overrides APIの形式を確認できませんでした。SDK画面から手動でAndroid版を指定してください。";
                return false;
            }

            try
            {
                Type listType = typeof(List<>).MakeGenericType(optionType);
                IList options = (IList)Activator.CreateInstance(listType);

                if (getMethod != null)
                {
                    IEnumerable existing = getMethod.Invoke(null, new object[] { pcAvatar }) as IEnumerable;
                    if (existing != null)
                    {
                        foreach (object option in existing)
                        {
                            object platformValue = GetMemberValue(option, "platform", "Platform");
                            if (platformValue is BuildTarget && (BuildTarget)platformValue == BuildTarget.Android)
                            {
                                continue;
                            }
                            options.Add(option);
                        }
                    }
                }

                object androidOption = Activator.CreateInstance(optionType);
                bool platformSet = SetMemberValue(androidOption, BuildTarget.Android, "platform", "Platform");
                bool avatarSet = SetMemberValue(androidOption, androidDescriptor, "avatar", "Avatar");
                if (!platformSet || !avatarSet)
                {
                    error = "Android用Overrideの情報を設定できませんでした。SDK画面から手動で指定してください。";
                    return false;
                }

                options.Add(androidOption);
                setMethod.Invoke(null, new object[] { pcAvatar, options });
                EditorUtility.SetDirty(pcAvatar);
                return true;
            }
            catch (Exception exception)
            {
                error = "Per-Platform Overrideの設定に失敗しました: " + exception.GetBaseException().Message;
                return false;
            }
        }

        internal static bool TryPreparePcAndroidMultiPlatform(out string error)
        {
            error = null;

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                error = "Android Build Supportが見つかりません。Unity Hubから追加してください。";
                return false;
            }

            BuildTarget desktopTarget = GetDesktopBuildTarget();
            BuildTargetGroup desktopGroup = BuildPipeline.GetBuildTargetGroup(desktopTarget);

            // VRChatのMulti-Platform BuildはPCを含める場合、PC側から開始するのが最も安定します。
            if (EditorUserBuildSettings.activeBuildTarget != desktopTarget)
            {
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(desktopGroup, desktopTarget);
                if (!switched)
                {
                    error = "PC向けのビルドターゲットへ切り替えられませんでした。";
                    return false;
                }
            }

            OpenSdkControlPanel();

            // SDK 3.7.6以降にはWindows + Androidを同時に選べるMulti-Platform Buildがあります。
            // SDK内部の保存名はバージョンによって変わるため、既知の選択メンバーを安全に探索します。
            bool selectionSet = TrySetSdkSelectedBuildTargets(desktopTarget, BuildTarget.Android);
            if (!selectionSet)
            {
                error = "VRChat SDKのPlatform(s)を自動選択できませんでした。SDKのバージョンによって内部形式が異なる可能性があります。";
                return false;
            }

            return true;
        }

        private static BuildTarget GetDesktopBuildTarget()
        {
#if UNITY_EDITOR_WIN
            return BuildTarget.StandaloneWindows64;
#elif UNITY_EDITOR_OSX
            return BuildTarget.StandaloneOSX;
#else
            return BuildTarget.StandaloneLinux64;
#endif
        }

        private static bool TrySetSdkSelectedBuildTargets(BuildTarget desktopTarget, BuildTarget androidTarget)
        {
            BuildTarget[] targets = { desktopTarget, androidTarget };
            bool changed = false;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Assembly assembly = assemblies[a];
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (!assemblyName.StartsWith("VRC.", StringComparison.OrdinalIgnoreCase) &&
                    assemblyName.IndexOf("VRChat", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException loadException)
                {
                    types = loadException.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type == null)
                    {
                        continue;
                    }

                    string fullName = type.FullName ?? type.Name;
                    if (fullName.IndexOf("SdkControlPanel", StringComparison.OrdinalIgnoreCase) < 0 &&
                        fullName.IndexOf("Builder", StringComparison.OrdinalIgnoreCase) < 0 &&
                        fullName.IndexOf("BuildTarget", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    changed |= TrySetBuildTargetMembers(null, type, AnyStatic, targets);

                    if (typeof(EditorWindow).IsAssignableFrom(type))
                    {
                        UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(type);
                        for (int w = 0; w < windows.Length; w++)
                        {
                            changed |= TrySetBuildTargetMembers(windows[w], type, AnyInstance, targets);
                        }
                    }
                }
            }

            // UIを更新させるためSDKウィンドウを再描画します。
            EditorWindow[] editorWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            for (int i = 0; i < editorWindows.Length; i++)
            {
                EditorWindow window = editorWindows[i];
                string name = window != null ? window.GetType().FullName : string.Empty;
                if (!string.IsNullOrEmpty(name) && name.IndexOf("VRCSdkControlPanel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    window.Repaint();
                }
            }

            return changed;
        }

        private static bool TrySetBuildTargetMembers(object instance, Type type, BindingFlags flags, BuildTarget[] targets)
        {
            bool changed = false;

            FieldInfo[] fields;
            try { fields = type.GetFields(flags); }
            catch { fields = new FieldInfo[0]; }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!LooksLikeSelectedPlatformMember(field.Name))
                {
                    continue;
                }

                object value;
                if (!TryCreatePlatformSelectionValue(field.FieldType, targets, out value))
                {
                    continue;
                }

                try
                {
                    field.SetValue(instance, value);
                    changed = true;
                }
                catch { }
            }

            PropertyInfo[] properties;
            try { properties = type.GetProperties(flags); }
            catch { properties = new PropertyInfo[0]; }

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (!property.CanWrite || property.GetIndexParameters().Length != 0 ||
                    !LooksLikeSelectedPlatformMember(property.Name))
                {
                    continue;
                }

                object value;
                if (!TryCreatePlatformSelectionValue(property.PropertyType, targets, out value))
                {
                    continue;
                }

                try
                {
                    property.SetValue(instance, value, null);
                    changed = true;
                }
                catch { }
            }

            MethodInfo[] methods;
            try { methods = type.GetMethods(flags); }
            catch { methods = new MethodInfo[0]; }

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                string methodName = method.Name ?? string.Empty;
                if (methodName.IndexOf("Set", StringComparison.OrdinalIgnoreCase) < 0 ||
                    (methodName.IndexOf("Platform", StringComparison.OrdinalIgnoreCase) < 0 &&
                     methodName.IndexOf("BuildTarget", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    continue;
                }

                object value;
                if (!TryCreatePlatformSelectionValue(parameters[0].ParameterType, targets, out value))
                {
                    continue;
                }

                try
                {
                    method.Invoke(instance, new[] { value });
                    changed = true;
                }
                catch { }
            }

            return changed;
        }

        private static bool LooksLikeSelectedPlatformMember(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            bool platformWord = lower.Contains("platform") || lower.Contains("buildtarget") || lower.Contains("buildtargets");
            bool selectionWord = lower.Contains("selected") || lower.Contains("selection") || lower.Contains("targetstobuild") ||
                                 lower == "platforms" || lower == "buildtargets" || lower == "targets";
            return platformWord && selectionWord;
        }

        private static bool TryCreatePlatformSelectionValue(Type targetType, BuildTarget[] targets, out object value)
        {
            value = null;
            if (targetType == null)
            {
                return false;
            }

            if (targetType == typeof(BuildTarget[]))
            {
                value = targets.ToArray();
                return true;
            }

            if (targetType == typeof(BuildTarget))
            {
                // 単一ターゲットしか持てないメンバーにはPCを設定します。
                value = targets[0];
                return true;
            }

            if (targetType.IsGenericType)
            {
                Type generic = targetType.GetGenericTypeDefinition();
                Type[] args = targetType.GetGenericArguments();
                if (args.Length == 1 && args[0] == typeof(BuildTarget))
                {
                    Type concrete = null;
                    if (generic == typeof(List<>) || generic == typeof(IList<>) || generic == typeof(ICollection<>) || generic == typeof(IEnumerable<>))
                    {
                        concrete = typeof(List<BuildTarget>);
                    }
                    else if (generic == typeof(HashSet<>))
                    {
                        concrete = typeof(HashSet<BuildTarget>);
                    }

                    if (concrete != null)
                    {
                        if (concrete == typeof(List<BuildTarget>))
                        {
                            value = new List<BuildTarget>(targets);
                        }
                        else
                        {
                            value = new HashSet<BuildTarget>(targets);
                        }
                        return true;
                    }
                }
            }

            if (typeof(IList).IsAssignableFrom(targetType) && !targetType.IsAbstract && targetType.GetConstructor(Type.EmptyTypes) != null)
            {
                try
                {
                    IList list = (IList)Activator.CreateInstance(targetType);
                    list.Add(targets[0]);
                    list.Add(targets[1]);
                    value = list;
                    return true;
                }
                catch { }
            }

            return false;
        }

        internal static void OpenSdkControlPanel()
        {
            if (!EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel"))
            {
                EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel...");
            }
        }
    }
}
#endif
