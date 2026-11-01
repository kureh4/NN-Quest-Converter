# NN - Quest Converter

VRChat向けPCアバターを、できるだけ見た目や揺れものを残したままAndroid / Quest向けへ変換するUnity Editorツールです。

## Version

**v1.0.0**

## 対応環境

- Unity 2022.3
- VRChat Avatar SDK

## 導入

[UnityPackageをダウンロード](./dist/nekoneko_quest_converter_v1.0.0.unitypackage)

Unity上部の `Assets → Import Package → Custom Package...` からUnityPackageをImportしてください。

詳しい手順は [導入方法.txt](./dist/導入方法.txt) を確認してください。

## 開き方

`ねこねこツール → NN - Quest Converter`

## ソースコード

`Assets/nekoneko/NN - Quest Converter/Editor/`

- `NekoQuestAnalyzer.cs`
- `NekoQuestConverterWindow.cs`
- `NekoQuestData.cs`
- `NekoQuestGenerator.cs`
- `NekoQuestSdkBridge.cs`

## Unity内の配置

Import後、ツール本体は最終的に次の場所へ配置されます。

`Assets/nekoneko/NN - Quest Converter/Editor/`

旧版の `Assets/nekoneko/QuestConverter` が存在する場合は、ツールの移行処理によってフォルダ自体を名称変更します。
