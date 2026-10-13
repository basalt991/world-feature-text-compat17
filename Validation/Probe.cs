using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using HarmonyLib;
using RimWorld.Planet;
using TMPro;
using UnityEngine;
using UnityStubs.Gui;
using Verse;
using WorldFeatureTextCompat17;
using Patches = WorldFeatureTextCompat17.Patches;

[assembly: SupportedOSPlatform("windows")]

class Probe
{
    const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label); checks++;
    }
    public static bool MuteLog() => false;
    public static void ThrowAfterPreparation() => throw new InvalidOperationException("probe intentional failure");
    static int Main()
    {
        try
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "0Harmony.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "WorldFeatureTextCompat17.dll"));
            Run(); Console.WriteLine("PASS: " + checks + " checks; runtime " + Environment.Version); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Run()
    {
        var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Engine.dll"));
        var brutal = Assembly.Load("Brutal.ImGui");
        var imgui = brutal.GetType("Brutal.ImGuiApi.ImGui", true);
        var create = imgui.GetMethod("CreateContext", All);
        object context = create.Invoke(null, new[] { Activator.CreateInstance(create.GetParameters()[0].ParameterType) });
        object io = imgui.GetMethod("GetIO", All).Invoke(null, null);
        var hostType = game.GetType("RimWorldWaylith.Imgui.ImGuiHost", true);
        object host = RuntimeHelpers.GetUninitializedObject(hostType);
        foreach (var field in hostType.GetFields(All))
        {
            if (field.IsStatic) continue;
            if (field.FieldType.IsGenericType && (field.FieldType.GetGenericTypeDefinition() == typeof(List<>) || field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
                field.SetValue(host, Activator.CreateInstance(field.FieldType));
        }
        hostType.GetField("fallbackFonts", All).SetValue(host, Array.Empty<string>());
        hostType.GetField("context", All).SetValue(host, context);
        Check((bool)hostType.GetMethod("TryEnableFreeTypeRasterizer", All).Invoke(null, new[] { io }), "real FreeType rasterizer");
        var register = hostType.GetMethod("RegisterFont", All);
        byte[] latinData = File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf");
        Check((int)register.Invoke(host, new object[] { "probe/latin", latinData, 48f, 1 }) == 0, "baseline Latin font registration");
        var backendType = game.GetType("RimWorldWaylith.Imgui.ImGuiGuiBackend", true);
        GuiBackend.Current = (IGuiBackend)Activator.CreateInstance(backendType, All, null, new[] { host, null }, null);
        var tmpType = game.GetType("RimWorldWaylith.Imgui.ImGuiTextMeshProBackend", true);
        var tmpBackend = (ITextMeshProBackend)Activator.CreateInstance(tmpType, All, null, new[] { hostType.GetField("fonts", All).GetValue(host) }, null);
        TextMeshProBackend.Current = tmpBackend;
        tmpType.GetMethod("BakeNewFonts", All).Invoke(tmpBackend, null);
        Check(!TMP_FontAsset.GetCharacters(new TMP_FontAsset(0)).Contains("한"), "unpatched world font excludes Hangul");

        // Actual compatibility world and actual GameObject components, isolated from the running game.
        var runtime = new Waylith.UnityCompat.UnityRuntime(null);
        var prefab = new GameObject("Probe world text prefab");
        prefab.AddComponent<MeshFilter>();
        prefab.AddComponent<MeshRenderer>();
        var prefabText = prefab.AddComponent<TextMeshPro>();
        prefabText.font = new TMP_FontAsset(0);
        prefabText.UpdateFontAsset();
        AccessTools.Field(typeof(WorldFeatureTextMesh_TextMeshPro), "WorldTextPrefab").SetValue(null, prefab);
        AccessTools.Field(typeof(LanguageDatabase), "defaultLanguage").SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage)));
        AccessTools.Field(typeof(LanguageDatabase), "activeLanguage").SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage)));
        var world = (WorldFeatures)RuntimeHelpers.GetUninitializedObject(typeof(WorldFeatures));
        world.features = new List<WorldFeature>();
        var originalFont = prefabText.font;
        runtime.World.RegisterCloneFactory(prefab, () =>
        {
            var clone = new GameObject("Probe world text clone");
            clone.AddComponent<MeshFilter>(); clone.AddComponent<MeshRenderer>();
            var component = clone.AddComponent<TextMeshPro>();
            component.font = originalFont;
            return clone;
        });
        var creator = AccessTools.Method(typeof(WorldFeatures), "CreateOrDestroyTexts");
        var textsField = AccessTools.Field(typeof(WorldFeatures), "texts");
        var meshField = AccessTools.Field(typeof(WorldFeatureTextMesh_TextMeshPro), "textMesh");
        var harmony = new Harmony("worldtextprobe");
        foreach (var method in typeof(Log).GetMethods(All))
            if (method.Name == "Message" || method.Name == "Warning" || method.Name == "Error")
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(Probe), nameof(MuteLog)));
        Patches.Install(harmony, AppContext.BaseDirectory);
        Check(Harmony.GetPatchInfo(creator).Prefixes.Count > 0, "Harmony patches actual world creator");
        string[] names = { "푸른 산맥", "붉은 강", "ABC 123 해안", "오래된\n숲" };
        foreach (string name in names) world.features.Add(new WorldFeature { name = name });
        creator.Invoke(world, null);
        var labels = (IList)textsField.GetValue(null);
        Check(labels.Count == names.Length, "native creator completes with one label per Korean feature");
        Check(ReferenceEquals(prefabText.font, originalFont), "shared prefab restored after creation");
        for (int i = 0; i < labels.Count; i++)
        {
            Check(labels[i] is WorldFeatureTextMesh_TextMeshPro, "Korean feature uses native TMP " + i);
            var text = (TextMeshPro)meshField.GetValue(labels[i]);
            Check(!ReferenceEquals(text, prefabText), "asset factory creates independent label " + i);
            text.text = names[i]; text.fontSize = 20f; text.color = Color.white;
            text.ForceMeshUpdate();
            int visible = 0, expected = 0;
            foreach (char c in names[i]) if (!char.IsWhiteSpace(c)) expected++;
            for (int c = 0; c < text.textInfo.characterCount; c++) if (text.textInfo.characterInfo[c].isVisible) visible++;
            Console.WriteLine("Mesh " + i + ": font=" + AccessTools.Field(typeof(TMP_FontAsset), "fontIndex").GetValue(text.font) + ", visible=" + visible + "/" + expected);
            Check(visible == expected, "every Korean/Latin glyph generates visible geometry " + i);
            Check(text.mesh.vertices.Length == expected * 4, "real glyph mesh vertex count " + i);
            Check(text.font.atlasTexture != null, "real world font atlas texture " + i);
            Check(ReferenceEquals(text.fontMaterial.mainTexture, text.font.atlasTexture), "label material uses captured font pixels " + i);
            Check(text.GetComponent<MeshRenderer>().sharedMaterial.renderQueue == 3610, "native world text render queue " + i);
        }
        // New syllables plus font/atlas growth must not drop earlier Korean glyphs.
        world.features.Add(new WorldFeature { name = "끝없는 협곡" });
        register.Invoke(host, new object[] { "probe/later-font", latinData, 14f, 1 });
        creator.Invoke(world, null);
        labels = (IList)textsField.GetValue(null);
        Check(labels.Count == world.features.Count, "recreation/new syllables keeps label count consistent");
        var rebuilt = (TextMeshPro)meshField.GetValue(labels[labels.Count - 1]);
        Check(TMP_FontAsset.GetCharacters(rebuilt.font).Contains("끝") && TMP_FontAsset.GetCharacters(rebuilt.font).Contains("푸"), "atlas rebake preserves old and new syllables");
        rebuilt.text = world.features[world.features.Count - 1].name; rebuilt.fontSize = 20f; rebuilt.color = Color.white;
        rebuilt.ForceMeshUpdate();
        Check(rebuilt.mesh.vertices.Length == 5 * 4, "newly added syllables generate real geometry after atlas growth");
        var throwMethod = AccessTools.Method(typeof(Probe), nameof(ThrowAfterPreparation));
        harmony.Patch(creator, postfix: new HarmonyMethod(throwMethod));
        bool propagated = false;
        try { creator.Invoke(world, null); }
        catch (TargetInvocationException e) { propagated = e.InnerException is InvalidOperationException; }
        Check(propagated, "unrelated creation exception propagates without suppression");
        Check(ReferenceEquals(prefabText.font, originalFont), "Harmony finalizer restores prefab after actual exception");
        harmony.Unpatch(creator, throwMethod);
        AccessTools.Field(typeof(LanguageDatabase), "activeLanguage").SetValue(null, LanguageDatabase.defaultLanguage);
        world.features = new List<WorldFeature> { new WorldFeature { name = "Blue Mountains" } };
        creator.Invoke(world, null);
        labels = (IList)textsField.GetValue(null);
        Check(ReferenceEquals(((TextMeshPro)meshField.GetValue(labels[0])).font, originalFont), "English retains original world font");
        Check(labels.Count == 1, "Korean to English world recreation removes obsolete labels");
        GuiBackend.Current = null; TextMeshProBackend.Current = null;
        imgui.GetMethod("DestroyContext", All).Invoke(null, new[] { context });
    }
}
