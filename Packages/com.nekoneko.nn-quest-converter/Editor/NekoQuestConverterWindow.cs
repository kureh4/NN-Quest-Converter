#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NekoNeko.QuestConverter
{
    // v1.0.0: 旧「QuestConverter」フォルダを新規作成で置き換えず、
    // AssetDatabase.MoveAsset() で「NN - Quest Converter」へ移動/名称変更します。
    // v1.0.18で先に作られた新フォルダがある場合も、Generatedを退避・統合してから移行します。
    [InitializeOnLoad]
    internal static class NekoQuestFolderMigration
    {
        private const string OldRoot = "Assets/nekoneko/QuestConverter";
        private const string NewRoot = "Assets/nekoneko/NN - Quest Converter";
        private const string OldEditor = OldRoot + "/Editor";
        private const string NewEditor = NewRoot + "/Editor";
        private const string OldGenerated = OldRoot + "/Generated";
        private const string NewGenerated = NewRoot + "/Generated";
        private const string WindowScriptGuid = "57ee40d71fd44fa997240fafdbbaf9d5";

        static NekoQuestFolderMigration()
        {
            EditorApplication.delayCall += MigrateFolderName;
        }

        private static void MigrateFolderName()
        {
            try
            {
                DeleteIfExists("Assets/!nekoneko_ReadMe");
                DeleteIfExists(OldRoot + "/version.txt");
                DeleteIfExists(NewRoot + "/version.txt");

                bool oldExists = AssetDatabase.IsValidFolder(OldRoot);
                bool newExists = AssetDatabase.IsValidFolder(NewRoot);

                // すでに完全移行済みなら何もしません。
                if (!oldExists)
                {
                    return;
                }

                if (newExists)
                {
                    // v1.0.18で新側に生成されたQuestデータがあれば、先に旧側へ安全に統合します。
                    MergeDirectoryContents(NewGenerated, OldGenerated);

                    // ツール本体(Editor)は、v1.0.0のスクリプトが存在する側を正として旧ルートへ集約します。
                    string activeWindowScriptPath = AssetDatabase.GUIDToAssetPath(WindowScriptGuid).Replace('\\', '/');
                    bool activeScriptIsInNew = activeWindowScriptPath.StartsWith(NewEditor + "/", StringComparison.OrdinalIgnoreCase);

                    if (activeScriptIsInNew)
                    {
                        DeleteIfExists(OldEditor);
                        MoveAssetChecked(NewEditor, OldEditor);
                    }
                    else
                    {
                        // v1.0.0は旧パスへImportされるため、通常はこちらになります。
                        DeleteIfExists(NewEditor);
                    }

                    // Generated / Editor以外に新側へ残っているユーザーデータがあっても捨てずに旧側へ移します。
                    MergeRootExtras(NewRoot, OldRoot);

                    // 中身を移し終えたv1.0.18側の新規フォルダを削除します。
                    if (AssetDatabase.IsValidFolder(NewRoot))
                    {
                        AssetDatabase.DeleteAsset(NewRoot);
                    }
                }

                if (AssetDatabase.IsValidFolder(OldRoot) && !AssetDatabase.IsValidFolder(NewRoot))
                {
                    string error = AssetDatabase.MoveAsset(OldRoot, NewRoot);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogError("[NN - Quest Converter] フォルダ名の変更に失敗しました: " + error);
                        return;
                    }

                    Debug.Log("[NN - Quest Converter] QuestConverter を NN - Quest Converter へ名称変更しました。");
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NN - Quest Converter] フォルダ移行中にエラーが発生しました。\n" + ex);
            }
        }

        private static void MergeRootExtras(string sourceRoot, string destinationRoot)
        {
            if (!AssetDatabase.IsValidFolder(sourceRoot) || !AssetDatabase.IsValidFolder(destinationRoot))
            {
                return;
            }

            string[] directories = Directory.GetDirectories(sourceRoot);
            foreach (string rawPath in directories)
            {
                string path = rawPath.Replace('\\', '/');
                string name = Path.GetFileName(path);
                if (string.Equals(name, "Editor", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Generated", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MovePreserving(path, destinationRoot + "/" + name);
            }

            string[] files = Directory.GetFiles(sourceRoot);
            foreach (string rawPath in files)
            {
                string path = rawPath.Replace('\\', '/');
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MovePreserving(path, destinationRoot + "/" + Path.GetFileName(path));
            }
        }

        private static void MergeDirectoryContents(string sourceDirectory, string destinationDirectory)
        {
            if (!AssetDatabase.IsValidFolder(sourceDirectory))
            {
                return;
            }

            EnsureFolder(destinationDirectory);

            string[] directories = Directory.GetDirectories(sourceDirectory);
            foreach (string rawPath in directories)
            {
                string path = rawPath.Replace('\\', '/');
                MovePreserving(path, destinationDirectory + "/" + Path.GetFileName(path));
            }

            string[] files = Directory.GetFiles(sourceDirectory);
            foreach (string rawPath in files)
            {
                string path = rawPath.Replace('\\', '/');
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MovePreserving(path, destinationDirectory + "/" + Path.GetFileName(path));
            }

            DeleteIfExists(sourceDirectory);
        }

        private static void MovePreserving(string source, string destination)
        {
            if (!AssetDatabase.IsValidFolder(source) && AssetDatabase.LoadMainAssetAtPath(source) == null)
            {
                return;
            }

            string target = destination;
            if (AssetDatabase.IsValidFolder(target) || AssetDatabase.LoadMainAssetAtPath(target) != null)
            {
                target = AssetDatabase.GenerateUniqueAssetPath(target);
            }

            MoveAssetChecked(source, target);
        }

        private static void MoveAssetChecked(string source, string destination)
        {
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("[NN - Quest Converter] Asset移動に失敗しました: " + source + " -> " + destination + "\n" + error);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string normalized = path.Replace('\\', '/');
            string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            string name = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                return;
            }

            EnsureFolder(parent);
            if (AssetDatabase.IsValidFolder(parent) && !AssetDatabase.IsValidFolder(normalized))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    internal sealed class NekoQuestConverterWindow : EditorWindow
    {
        private const string Version = "1.0.0";

        private GameObject _avatar;
        private NekoQuestTargetMode _targetMode = NekoQuestTargetMode.Balanced;
        private NekoQuestAnalysis _analysis;
        private NekoQuestGenerationOptions _options = new NekoQuestGenerationOptions();
        private NekoQuestGenerationResult _result;
        private Vector2 _scroll;
        private Vector2 _physBoneScroll;
        private bool _simpleMode = true;
        private bool _showIssues = true;
        private bool _showAdvancedOptions;
        [NonSerialized] private bool _overrideExplicitlyEnabled;

        [MenuItem("ねこねこツール/NN - Quest Converter")]
        private static void Open()
        {
            NekoQuestConverterWindow window = GetWindow<NekoQuestConverterWindow>();
            window.titleContent = new GUIContent("NN - Quest Converter");
            window.minSize = new Vector2(520f, 640f);
            window.Show();
        }

        private void OnEnable()
        {
            _options.targetMode = _targetMode;

            // Unityのスクリプト再読み込み時に以前のEditorWindowの値が復元される場合があります。
            // Blueprint IDを誤って共有しないよう、起動時は必ず別アバター設定へ戻します。
            _options.setupPerPlatformOverride = false;
            _overrideExplicitlyEnabled = false;

            if (_avatar == null && Selection.activeGameObject != null)
            {
                _avatar = NekoQuestSdkBridge.NormalizeAvatarRoot(Selection.activeGameObject);
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawHeader();
            DrawEnvironment();
            EditorGUILayout.Space(8f);
            DrawAvatarSelection();
            EditorGUILayout.Space(8f);
            DrawTargetMode();
            EditorGUILayout.Space(8f);
            DrawAnalyzeButton();

            if (_analysis != null)
            {
                EditorGUILayout.Space(10f);
                DrawAnalysis();
                EditorGUILayout.Space(10f);
                DrawPhysBones();
                EditorGUILayout.Space(10f);
                DrawOptions();
                EditorGUILayout.Space(12f);
                DrawGenerateButton();
            }

            if (_result != null)
            {
                EditorGUILayout.Space(12f);
                DrawResult();
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleLeft
            };
            EditorGUILayout.LabelField("NN - Quest Converter", title);
            EditorGUILayout.LabelField("PC版を壊さず、Quest用のコピーを作成します。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("v" + Version, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            string[] modes = { "かんたん", "詳細" };
            int selected = GUILayout.Toolbar(_simpleMode ? 0 : 1, modes, GUILayout.Width(190f));
            bool nextSimpleMode = selected == 0;

            // 詳細モードへ切り替えた際も、安全側の「別アバター」を初期値にします。
            // ユーザーが必要な場合だけ、その後にOverrideを手動でONにできます。
            if (_simpleMode && !nextSimpleMode)
            {
                _options.setupPerPlatformOverride = false;
                _overrideExplicitlyEnabled = false;
            }

            if (nextSimpleMode)
            {
                _options.setupPerPlatformOverride = false;
                _overrideExplicitlyEnabled = false;
            }

            _simpleMode = nextSimpleMode;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEnvironment()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("準備状況", EditorStyles.boldLabel);
            DrawStatusLine(NekoQuestSdkBridge.IsSdkInstalled(), "VRChat SDK", "VRChat SDK Avatarsが必要です");

            bool unityVersionOk = Application.unityVersion.StartsWith("2022.3.22f1", StringComparison.Ordinal);
            DrawStatusLine(unityVersionOk, "Unity " + Application.unityVersion,
                "VRChat推奨のUnity 2022.3.22f1での使用を推奨します");

            bool androidSupported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
            DrawStatusLine(androidSupported, "Android Build Support", "Unity HubからAndroid Build Supportを追加してください");
            EditorGUILayout.EndVertical();
        }

        private static void DrawStatusLine(bool ok, string successText, string failedText)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(ok ? "✓" : "△", GUILayout.Width(20f));
            EditorGUILayout.LabelField(ok ? successText : failedText, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAvatarSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. PC版アバターを選ぶ", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            GameObject selected = (GameObject)EditorGUILayout.ObjectField("PC版アバター", _avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                _avatar = NekoQuestSdkBridge.NormalizeAvatarRoot(selected);
                _analysis = null;
                _result = null;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hierarchyの選択中アバターを使う"))
            {
                _avatar = NekoQuestSdkBridge.NormalizeAvatarRoot(Selection.activeGameObject);
                _analysis = null;
                _result = null;
            }
            if (_avatar != null && GUILayout.Button("場所を確認", GUILayout.Width(100f)))
            {
                Selection.activeGameObject = _avatar;
                EditorGUIUtility.PingObject(_avatar);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("元のPC版は直接変更しません。Quest版は別のPrefabとして保存されます。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void DrawTargetMode()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. 変換方針を選ぶ", EditorStyles.boldLabel);

            string[] labels = { "見た目優先", "おすすめ", "軽さ優先" };
            int current = _targetMode == NekoQuestTargetMode.Appearance ? 0 :
                          _targetMode == NekoQuestTargetMode.Balanced ? 1 : 2;
            int next = GUILayout.Toolbar(current, labels);
            NekoQuestTargetMode nextMode = next == 0 ? NekoQuestTargetMode.Appearance :
                                               next == 1 ? NekoQuestTargetMode.Balanced :
                                                           NekoQuestTargetMode.Lightweight;
            if (nextMode != _targetMode)
            {
                _targetMode = nextMode;
                _options.targetMode = nextMode;
                ApplyModeTextureDefaults();
                if (_analysis != null)
                {
                    NekoQuestAnalyzer.AutoPlanPhysBones(_analysis, NekoQuestLimits.ForMode(_targetMode));
                }
            }

            string description;
            switch (_targetMode)
            {
                case NekoQuestTargetMode.Appearance:
                    description = "揺れものと見た目を優先します。QuestではPoor判定になり、初期設定で非表示になる場合があります。";
                    break;
                case NekoQuestTargetMode.Lightweight:
                    description = "表示されやすさを優先します。残せる揺れものとテクスチャ品質は少なくなります。";
                    break;
                default:
                    description = "Mediumを目標にした初心者向けのおすすめ設定です。";
                    break;
            }
            EditorGUILayout.HelpBox(description, MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void ApplyModeTextureDefaults()
        {
            if (_targetMode == NekoQuestTargetMode.Appearance)
            {
                _options.defaultTextureSize = 1024;
                _options.importantTextureSize = 1024;
                _options.smallTextureSize = 512;
            }
            else if (_targetMode == NekoQuestTargetMode.Lightweight)
            {
                _options.defaultTextureSize = 512;
                _options.importantTextureSize = 512;
                _options.smallTextureSize = 256;
            }
            else
            {
                _options.defaultTextureSize = 512;
                _options.importantTextureSize = 1024;
                _options.smallTextureSize = 256;
            }
        }

        private void DrawAnalyzeButton()
        {
            GUI.enabled = _avatar != null && NekoQuestSdkBridge.IsSdkInstalled();
            if (GUILayout.Button("アバターをチェックする", GUILayout.Height(38f)))
            {
                _avatar = NekoQuestSdkBridge.NormalizeAvatarRoot(_avatar);
                _analysis = NekoQuestAnalyzer.Analyze(_avatar, _targetMode);
                _result = null;
            }
            GUI.enabled = true;
        }

        private void DrawAnalysis()
        {
            NekoQuestLimits limits = NekoQuestLimits.ForMode(_targetMode);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("チェック結果", EditorStyles.boldLabel);

            if (!_analysis.isValid)
            {
                EditorGUILayout.HelpBox("このままではQuest版を生成できません。下のエラーを確認してください。", MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox("Quest版のコピーを生成できます。SDKでの最終チェックも必要です。", MessageType.Info);
            }

            DrawStat("三角形", _analysis.triangles.ToString("N0"), _analysis.triangles <= limits.triangles,
                limits.triangles.ToString("N0"));
            DrawStat("Skinned Mesh", _analysis.skinnedMeshes.ToString(), _analysis.skinnedMeshes <= limits.skinnedMeshes,
                limits.skinnedMeshes.ToString());
            DrawStat("マテリアルスロット", _analysis.materialSlots.ToString(), _analysis.materialSlots <= limits.materialSlots,
                limits.materialSlots.ToString());
            DrawStat("推定テクスチャ容量", NekoQuestGenerator.FormatBytes(_analysis.textureMemoryBytes),
                _analysis.textureMemoryBytes <= limits.textureMemoryBytes, NekoQuestGenerator.FormatBytes(limits.textureMemoryBytes));
            DrawStat("ボーン", _analysis.bones.ToString(), _analysis.bones <= limits.bones, limits.bones.ToString());

            _showIssues = EditorGUILayout.Foldout(_showIssues, "見つかった注意点（" + _analysis.issues.Count + "件）", true);
            if (_showIssues)
            {
                for (int i = 0; i < _analysis.issues.Count; i++)
                {
                    NekoQuestIssue issue = _analysis.issues[i];
                    MessageType messageType = issue.severity == NekoQuestIssue.Severity.Error
                        ? MessageType.Error
                        : issue.severity == NekoQuestIssue.Severity.Warning ? MessageType.Warning : MessageType.Info;
                    EditorGUILayout.HelpBox(issue.title + "\n" + issue.detail, messageType);
                    if (issue.context != null && GUILayout.Button("対象を確認", GUILayout.Width(100f)))
                    {
                        Selection.activeObject = issue.context;
                        EditorGUIUtility.PingObject(issue.context);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawStat(string label, string value, bool within, string target)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(within ? "✓" : "△", GUILayout.Width(20f));
            EditorGUILayout.LabelField(label, GUILayout.Width(145f));
            EditorGUILayout.LabelField(value, GUILayout.Width(90f));
            EditorGUILayout.LabelField("目標 " + target, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPhysBones()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("3. 残したい揺れものを確認する", EditorStyles.boldLabel);
            if (GUILayout.Button("おすすめに戻す", GUILayout.Width(115f)))
            {
                NekoQuestAnalyzer.AutoPlanPhysBones(_analysis, NekoQuestLimits.ForMode(_targetMode));
            }
            EditorGUILayout.EndHorizontal();

            if (_analysis.physBones.Count == 0)
            {
                EditorGUILayout.HelpBox("PhysBoneは見つかりませんでした。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _physBoneScroll = EditorGUILayout.BeginScrollView(_physBoneScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(310f));
            for (int i = 0; i < _analysis.physBones.Count; i++)
            {
                NekoPhysBoneItem item = _analysis.physBones[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                item.keep = EditorGUILayout.Toggle(item.keep, GUILayout.Width(18f));
                EditorGUILayout.LabelField(item.displayName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(item.affectedTransforms + " bones", EditorStyles.miniLabel, GUILayout.Width(65f));
                EditorGUILayout.EndHorizontal();

                if (item.keep)
                {
                    if (!_simpleMode)
                    {
                        item.priority = EditorGUILayout.IntSlider("優先度", item.priority, 0, 100);
                        GUI.enabled = item.colliderCount > 0;
                        item.keepCollisions = EditorGUILayout.ToggleLeft("当たり判定も残す", item.keepCollisions);
                        GUI.enabled = true;
                        EditorGUILayout.LabelField("場所: " + (string.IsNullOrEmpty(item.path) ? "アバター直下" : item.path), EditorStyles.miniLabel);
                    }

                    if (!string.IsNullOrEmpty(item.note))
                    {
                        EditorGUILayout.LabelField(item.note, EditorStyles.wordWrappedLabel);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Quest版ではこの揺れを停止します。PC版には残ります。", EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            NekoQuestAnalyzer.RecalculatePlanSummary(_analysis,
                out int keptComponents,
                out int keptTransforms,
                out int keptColliders,
                out int keptChecks);
            NekoQuestLimits limits = NekoQuestLimits.ForMode(_targetMode);

            bool valid = keptComponents <= limits.physBoneComponents &&
                         keptTransforms <= limits.physBoneTransforms &&
                         keptColliders <= limits.physBoneColliders &&
                         keptChecks <= limits.physBoneCollisionChecks;

            string summary = "予定: 揺れもの " + keptComponents + " / " + limits.physBoneComponents +
                             "、揺れるボーン " + keptTransforms + " / " + limits.physBoneTransforms +
                             "、当たり判定 " + keptChecks + " / " + limits.physBoneCollisionChecks;
            EditorGUILayout.HelpBox(summary,
                valid ? MessageType.Info : MessageType.Error);
            if (!valid)
            {
                EditorGUILayout.HelpBox("上限を超えています。「おすすめに戻す」を押すか、残す揺れものを減らしてください。", MessageType.Error);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawOptions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _showAdvancedOptions = EditorGUILayout.Foldout(_showAdvancedOptions || !_simpleMode, "変換内容", true);
            if (_showAdvancedOptions || !_simpleMode)
            {
                _options.convertMaterials = EditorGUILayout.ToggleLeft("Quest対応シェーダーへ変換", _options.convertMaterials);
                _options.duplicateAndCompressTextures = EditorGUILayout.ToggleLeft("Quest用テクスチャを別コピーして圧縮", _options.duplicateAndCompressTextures);
                _options.enforceAndroidUploadSize = EditorGUILayout.ToggleLeft("Androidの40MB・10MB上限に収まりやすいよう自動調整", _options.enforceAndroidUploadSize);
                _options.removeUnsupportedComponents = EditorGUILayout.ToggleLeft("Questで使えない機能をコピーから除去", _options.removeUnsupportedComponents);
                _options.trimContactsToLimit = EditorGUILayout.ToggleLeft("Contactを目標上限まで整理", _options.trimContactsToLimit);
                _options.trimParticlesToLimit = EditorGUILayout.ToggleLeft("パーティクル等を目標上限まで整理", _options.trimParticlesToLimit);

                if (_simpleMode)
                {
                    _options.setupPerPlatformOverride = false;
                    _overrideExplicitlyEnabled = false;
                }
                else
                {
                    bool overrideValue = _overrideExplicitlyEnabled && _options.setupPerPlatformOverride;
                    EditorGUI.BeginChangeCheck();
                    overrideValue = EditorGUILayout.ToggleLeft("PC版と同じアバターとして紐づける（Override）", overrideValue);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _overrideExplicitlyEnabled = overrideValue;
                        _options.setupPerPlatformOverride = overrideValue;
                    }
                    else if (!_overrideExplicitlyEnabled)
                    {
                        _options.setupPerPlatformOverride = false;
                    }
                }

                _options.keepGeneratedAvatarInScene = EditorGUILayout.ToggleLeft("生成したQuest版をHierarchyに残す", _options.keepGeneratedAvatarInScene);

                if (!_simpleMode)
                {
                    _options.defaultTextureSize = EditorGUILayout.IntPopup("通常テクスチャ", _options.defaultTextureSize,
                        new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });
                    _options.importantTextureSize = EditorGUILayout.IntPopup("顔・瞳など", _options.importantTextureSize,
                        new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 });
                    _options.smallTextureSize = EditorGUILayout.IntPopup("マスク・MatCapなど", _options.smallTextureSize,
                        new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
                    _options.outputRoot = EditorGUILayout.TextField("保存先", _options.outputRoot);
                }
            }

            if (_options.setupPerPlatformOverride)
            {
                EditorGUILayout.HelpBox("ON：PC版のAndroid Overrideとして、同じアバターIDへ紐づけます。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("OFF（初期設定）：Pipeline ManagerとBlueprint IDをコピーしないため、別アバターとして新規アップロードできます。", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawGenerateButton()
        {
            NekoQuestAnalyzer.RecalculatePlanSummary(_analysis,
                out int keptComponents,
                out int keptTransforms,
                out int keptColliders,
                out int keptChecks);
            NekoQuestLimits limits = NekoQuestLimits.ForMode(_targetMode);
            bool planValid = keptComponents <= limits.physBoneComponents &&
                             keptTransforms <= limits.physBoneTransforms &&
                             keptColliders <= limits.physBoneColliders &&
                             keptChecks <= limits.physBoneCollisionChecks;

            GUI.enabled = _analysis.isValid && planValid;
            if (GUILayout.Button("Quest版を生成する", GUILayout.Height(46f)))
            {
                _options.targetMode = _targetMode;
                _options.setupPerPlatformOverride = !_simpleMode &&
                                                    _overrideExplicitlyEnabled &&
                                                    _options.setupPerPlatformOverride;
                try
                {
                    _result = NekoQuestGenerator.Generate(_analysis, _options,
                        (value, text) => EditorUtility.DisplayProgressBar("NN - Quest Converter", text, value));
                    if (_result.success && _result.generatedAvatar != null)
                    {
                        Selection.activeGameObject = _result.generatedAvatar;
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }
            GUI.enabled = true;

            EditorGUILayout.HelpBox("メッシュ結合・ポリゴン削減・テクスチャアトラス化は自動化していません。生成後にVRChat SDKのBuilderで最終判定を確認してください。", MessageType.Info);
        }

        private void DrawResult()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(_result.success ? "Quest版が完成しました！" : "生成に失敗しました", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_result.message, _result.success ? MessageType.Info : MessageType.Error);

            if (_result.success)
            {
                if (!_options.setupPerPlatformOverride)
                {
                    EditorGUILayout.LabelField("✓ Blueprint IDを継承していません。別アバターとして新規アップロードできます。", EditorStyles.wordWrappedLabel);
                }
                else if (_result.overrideConfigured)
                {
                    EditorGUILayout.LabelField("✓ Android用Per-Platform Overrideを設定しました。", EditorStyles.wordWrappedLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("△ Overrideは手動設定が必要です。", EditorStyles.wordWrappedLabel);
                }

                if (_result.changes.Count > 0)
                {
                    EditorGUILayout.LabelField("自動で行った変更", EditorStyles.boldLabel);
                    for (int i = 0; i < _result.changes.Count; i++)
                    {
                        EditorGUILayout.LabelField("・" + _result.changes[i], EditorStyles.wordWrappedLabel);
                    }
                }

                if (_result.warnings.Count > 0)
                {
                    EditorGUILayout.LabelField("確認が必要な点", EditorStyles.boldLabel);
                    for (int i = 0; i < _result.warnings.Count; i++)
                    {
                        EditorGUILayout.HelpBox(_result.warnings[i], MessageType.Warning);
                    }
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Quest版を確認"))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_result.prefabPath);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
                if (GUILayout.Button("保存場所を開く"))
                {
                    EditorUtility.RevealInFinder(Path.GetFullPath(_result.outputFolder));
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("VRChat SDKを開く"))
                {
                    NekoQuestSdkBridge.OpenSdkControlPanel();
                }
                bool androidSupported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
                GUI.enabled = androidSupported;
                if (GUILayout.Button("PC + Android マルチにする"))
                {
                    string multiError;
                    bool configured = NekoQuestSdkBridge.TryPreparePcAndroidMultiPlatform(out multiError);
                    if (!configured && !string.IsNullOrEmpty(multiError))
                    {
                        EditorUtility.DisplayDialog(
                            "NN - Quest Converter",
                            multiError + "\n\nVRChat SDKは開いてあります。Platform(s)で Windows と Android の2つにチェックを入れてください。",
                            "OK");
                    }
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
