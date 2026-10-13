using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    static uint U32(byte[] d, int o) => (uint)d[o] << 24 | (uint)d[o + 1] << 16 | (uint)d[o + 2] << 8 | d[o + 3];
    static void Put32(byte[] d, int o, uint n) { d[o] = (byte)(n >> 24); d[o + 1] = (byte)(n >> 16); d[o + 2] = (byte)(n >> 8); d[o + 3] = (byte)n; }
    static byte[] Collection(byte[] latin, byte[] korean)
    {
        // Test a collection whose first face cannot draw Korean. Face-local table
        // offsets become absolute collection offsets, exactly as in a real TTC.
        int second = 20 + ((latin.Length + 3) & ~3);
        byte[] data = new byte[second + ((korean.Length + 3) & ~3)];
        Put32(data, 0, 0x74746366); Put32(data, 4, 0x00010000); Put32(data, 8, 2);
        Put32(data, 12, 20); Put32(data, 16, (uint)second);
        foreach (var pair in new[] { (latin, 20), (korean, second) })
        {
            Array.Copy(pair.Item1, 0, data, pair.Item2, pair.Item1.Length);
            int tables = pair.Item1[4] << 8 | pair.Item1[5];
            for (int i = 0; i < tables; i++)
            {
                int r = pair.Item2 + 12 + i * 16 + 8;
                Put32(data, r, checked(U32(data, r) + (uint)pair.Item2));
            }
        }
        return data;
    }
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
        byte[] koreanData = File.ReadAllBytes(@"C:\Windows\Fonts\malgun.ttf");
        char[] hangul = Enumerable.Range(0xAC00, 11172).Select(c => (char)c).ToArray();
        byte[] bundledFont = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "Pretendard.ttf"));
        Check(PlatformFonts.SelectFace(bundledFont, hangul) == 0, "bundled Pretendard covers all 11172 Hangul syllables");
        Check(PlatformFonts.SelectFace(koreanData, hangul) == 0, "Windows default covers all 11172 Hangul syllables");
        var macPaths = PlatformFonts.CandidatePaths(true, "unused-Windows-path");
        Check(macPaths[0] == "/System/Library/Fonts/AppleSDGothicNeo.ttc" && macPaths.All(p => p.StartsWith("/")), "Mac candidates use engine-confirmed macOS paths");
        Check(PlatformFonts.CandidatePaths(false, @"C:\Windows\Fonts")[0] == @"C:\Windows\Fonts\malgun.ttf", "Windows candidate remains Malgun Gothic");
        // Mac path resolution is simulated with installed Windows font data;
        // these checks do not claim to test Apple fonts or macOS native execution.
        var macFont = PlatformFonts.Resolve(macPaths, p => p == macPaths[0], p => koreanData, "한글 ABC");
        Check(macFont.Path == macPaths[0], "simulated Mac primary path resolves");
        var fallback = PlatformFonts.Resolve(macPaths, p => true,
            p => p == macPaths[0] ? new byte[12] : p == macPaths[1] ? latinData : koreanData, "한글");
        Check(fallback.Path == macPaths[2], "corrupt and missing-glyph candidates fall back safely");
        bool missing = false;
        try { PlatformFonts.Resolve(macPaths, p => false, p => throw new Exception("must not read absent files"), "한"); }
        catch (FileNotFoundException) { missing = true; }
        Check(missing, "missing Mac font produces explicit diagnostic");
        byte[] collection = Collection(latinData, koreanData);
        Check(PlatformFonts.SelectFace(collection, "한글 ABC") == 1, "TTC skips Latin-only first face");
        byte[] extracted = PlatformFonts.ExtractFace(collection, 1);
        Check(U32(extracted, 0) != 0x74746366 && PlatformFonts.SelectFace(extracted, hangul) == 0, "standalone TTC extraction preserves all Hangul glyph mappings");
        uint checksum = 0;
        for (int i = 0; i < extracted.Length; i += 4) checksum = unchecked(checksum + U32(extracted, i));
        Check(checksum == 0xB1B0AFBA, "extracted SFNT checksum adjustment");
        bool badIndex = false;
        try { PlatformFonts.ExtractFace(collection, 2); } catch (InvalidDataException) { badIndex = true; }
        Check(badIndex, "out-of-range collection face is rejected");
        int collectionSlot = (int)register.Invoke(host, new object[] { "probe/extracted-collection", extracted, 48f, 1 });
        Check(collectionSlot >= 0, "real Waylith FreeType accepts extracted TTC face");
        byte[] batang = File.ReadAllBytes(@"C:\Windows\Fonts\batang.ttc");
        int batangFace = PlatformFonts.SelectFace(batang, "한글 ABC");
        Check(batangFace >= 0, "real multi-face Korean TTC selection");
        Check((int)register.Invoke(host, new object[] { "probe/real-collection", PlatformFonts.ExtractFace(batang, batangFace), 48f, 1 }) >= 0, "real Waylith FreeType accepts extracted real TTC");
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
        Check(((TextMeshPro)meshField.GetValue(labels[0])).font.name == "WorldFeatureTextCompat17/Pretendard", "production world label uses bundled Pretendard");
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
        world.textsCreated = true;
        bool propagated = false;
        try { creator.Invoke(world, null); }
        catch (TargetInvocationException e) { propagated = e.InnerException is InvalidOperationException; }
        Check(propagated, "unrelated creation exception propagates without suppression");
        Check(ReferenceEquals(prefabText.font, originalFont), "Harmony finalizer restores prefab after actual exception");
        Check(!world.textsCreated, "real creation failure resets textsCreated");
        Check(((IList)textsField.GetValue(null)).Count == 0, "real creation failure clears partial text list");
        harmony.Unpatch(creator, throwMethod);
        AccessTools.Field(typeof(LanguageDatabase), "activeLanguage").SetValue(null, LanguageDatabase.defaultLanguage);
        world.features = new List<WorldFeature> { new WorldFeature { name = "Blue Mountains" } };
        creator.Invoke(world, null);
        labels = (IList)textsField.GetValue(null);
        Check(ReferenceEquals(((TextMeshPro)meshField.GetValue(labels[0])).font, originalFont), "English retains original world font");
        Check(labels.Count == 1, "Korean to English world recreation removes obsolete labels");
        AccessTools.Field(typeof(LanguageDatabase), "activeLanguage").SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage)));
        char missingGlyph = '\u26F0';
        Check(PlatformFonts.SelectFace(bundledFont, new[] { missingGlyph }) < 0, "regression fixture is absent from Pretendard");
        string originalName = "한글" + missingGlyph;
        world.features = new List<WorldFeature> { new WorldFeature { name = "푸른 산맥" }, new WorldFeature { name = originalName } };
        creator.Invoke(world, null);
        labels = (IList)textsField.GetValue(null);
        Check(labels.Count == 2, "missing glyph no longer aborts other world names");
        var missingLabel = (WorldFeatureTextMesh_TextMeshPro)labels[1];
        // The native positioner sets Text after CreateOrDestroyTexts has restored the
        // shared prefab. Verify sanitization is scoped to the cloned label's font.
        missingLabel.Text = originalName;
        Check(missingLabel.Text == "한글?", "missing glyph replaced only in rendered name");
        Check(world.features[1].name == originalName, "original world feature name preserved");
        var missingMesh = (TextMeshPro)meshField.GetValue(missingLabel);
        missingMesh.fontSize = 20f; missingMesh.color = Color.white; missingMesh.ForceMeshUpdate();
        Check(missingMesh.mesh.vertices.Length == 12, "replacement glyph generates real visible geometry");
        missingLabel.Text = "한글\U0001F600\n?";
        Check(missingLabel.Text == "한글?\n?", "supplementary scalar replaced once and newline preserved");
        var gameState = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "playSettings").SetValue(gameState, new RimWorld.PlaySettings { showWorldFeatures = false });
        AccessTools.PropertySetter(typeof(Current), "Game").Invoke(null, new object[] { gameState });
        foreach (WorldFeatureTextMesh label in labels) label.SetActive(false);
        world.textsCreated = true;
        var update = AccessTools.Method(typeof(WorldFeatures), "UpdateFeatures");
        update.Invoke(world, null); update.Invoke(world, null);
        Check(labels.Count == world.features.Count, "native repeated updates retain consistent text count");
        // Exercise the actual initial Update failure, before WorldGrid positioning.
        harmony.Patch(creator, postfix: new HarmonyMethod(throwMethod));
        world.textsCreated = false;
        bool firstFailure = false, nextFailure = false;
        try { update.Invoke(world, null); }
        catch (TargetInvocationException e) { firstFailure = e.InnerException is InvalidOperationException; }
        try { update.Invoke(world, null); }
        catch (TargetInvocationException e) { nextFailure = e.InnerException is InvalidOperationException; }
        Check(firstFailure && nextFailure && !world.textsCreated, "failed initial updates retry creation without secondary index exception");
        harmony.Unpatch(creator, throwMethod);
        world.features = new List<WorldFeature> { new WorldFeature { name = "푸른 산맥" } };
        creator.Invoke(world, null);
        Check(((IList)textsField.GetValue(null)).Count == 1, "later valid Korean world recovers after missing-glyph world");
        var required = (ISet<char>)AccessTools.Field(typeof(Patches), "Characters").GetValue(null);
        Check(!required.Contains(missingGlyph) && !required.Contains('한'), "new world requirements exclude prior unsupported and supported characters");
        world.features.Clear(); creator.Invoke(world, null);
        Check(required.Count == 0 && ((IList)textsField.GetValue(null)).Count == 0, "empty world clears requirements and labels");
        GuiBackend.Current = null; TextMeshProBackend.Current = null;
        imgui.GetMethod("DestroyContext", All).Invoke(null, new[] { context });
    }
}
