using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using RimWorld.Planet;
using TMPro;
using UnityStubs.Gui;
using Verse;

namespace WorldFeatureTextCompat17
{
    public sealed class CompatibilityMod : Mod
    {
        public CompatibilityMod(ModContentPack content) : base(content)
        {
            Patches.Install(new Harmony("local.worldfeaturetextcompat17"), content.RootDir);
        }
    }

    public static class Patches
    {
        private static readonly FieldInfo ForceLegacy = AccessTools.Field(typeof(WorldFeatures), "ForceLegacyText");
        private static readonly FieldInfo LabelText = AccessTools.Field(typeof(WorldFeatureTextMesh_TextMeshPro), "textMesh");
        private static readonly SortedSet<char> Characters = new SortedSet<char>();
        private static MethodInfo bake, bakeRange, getFontBaked, bakeNewFonts, captureAtlas, getIO, getAtlas;
        private static FieldInfo bakedFonts, imFonts, characterList, atlasPixels;
        private static object backend, targetFont;
        private static int fontIndex = -1;
        private static TMP_FontAsset fontAsset;
        private static bool logged;
        private static string fontPath;

        public static void Install(Harmony harmony, string modRoot)
        {
            fontPath = Path.Combine(modRoot, "Fonts", "Pretendard.ttf");
            // These APIs are specific to the Waylith build. Resolve them once, never per frame.
            var type = AccessTools.TypeByName("RimWorldWaylith.Imgui.ImGuiTextMeshProBackend");
            if (type == null || ForceLegacy == null || LabelText == null) throw new NotSupportedException("World text compatibility requires RimWorld 1.7 Waylith.");
            bake = AccessTools.Method(type, "Bake");
            bakeRange = AccessTools.Method(type, "BakeRange");
            bakeNewFonts = AccessTools.Method(type, "BakeNewFonts");
            bakedFonts = AccessTools.Field(type, "fonts");
            imFonts = AccessTools.Field(type, "imFonts");
            characterList = bake == null ? null : AccessTools.Field(bake.ReturnType, "characters");
            getFontBaked = AccessTools.Method(AccessTools.TypeByName("Brutal.ImGuiApi.ImFontExtensions"), "GetFontBaked");
            captureAtlas = AccessTools.Method(type, "TryCaptureAtlas");
            atlasPixels = AccessTools.Field(type, "atlasPixels");
            getIO = AccessTools.Method(AccessTools.TypeByName("Brutal.ImGuiApi.ImGui"), "GetIO");
            getAtlas = AccessTools.PropertyGetter(AccessTools.TypeByName("Brutal.ImGuiApi.ImGuiIOPtr"), "Fonts");
            if (bake == null || bakeRange == null || bakeNewFonts == null || bakedFonts == null || imFonts == null || characterList == null || getFontBaked == null || captureAtlas == null || atlasPixels == null || getIO == null || getAtlas == null)
                throw new NotSupportedException("World text compatibility: Waylith font APIs changed; update this mod.");
            harmony.Patch(bake, postfix: new HarmonyMethod(typeof(Patches), nameof(ExtendBake)));
            harmony.Patch(AccessTools.Method(typeof(WorldFeatures), "CreateOrDestroyTexts"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(PrepareWorldNames)),
                finalizer: new HarmonyMethod(typeof(Patches), nameof(RestorePrefab)));
            harmony.Patch(AccessTools.Method(typeof(WorldFeatures), "HasCharactersUnsupportedByTextMeshPro"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(UsePreparedCharacters)));
            harmony.Patch(AccessTools.Method(typeof(WorldFeatureTextMesh_TextMeshPro), "Init"),
                postfix: new HarmonyMethod(typeof(Patches), nameof(ApplyLabelFont)));
        }

        public sealed class PrefabState
        {
            public TextMeshPro Text;
            public TMP_FontAsset Font;
            public bool Legacy;
        }

        public static void PrepareWorldNames(WorldFeatures __instance, out PrefabState __state)
        {
            __state = null;
            if (__instance.features == null || __instance.features.Count == 0) return;
            // English keeps its native prefab/font, even when loading a formerly Korean world.
            if (LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage) return;
            bool hasHangul = false;
            foreach (var feature in __instance.features)
                foreach (char c in feature.name ?? "")
                    if (IsHangul(c)) hasHangul = true;
            if (!hasHangul) return;

            var text = WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab?.GetComponent<TextMeshPro>();
            if (text == null || GuiBackend.Current == null || TextMeshProBackend.Current == null)
                throw new InvalidOperationException("World text compatibility: world text prefab/font backend is not ready.");
            foreach (var feature in __instance.features)
                foreach (char c in feature.name ?? "")
                    if (!char.IsControl(c)) Characters.Add(c);
            EnsureFont();
            var supported = new HashSet<char>(TMP_FontAsset.GetCharacters(fontAsset));
            foreach (char c in Characters)
                if (!supported.Contains(c)) throw new InvalidOperationException("World text compatibility: system font lacks U+" + ((int)c).ToString("X4") + ".");

            __state = new PrefabState { Text = text, Font = text.font, Legacy = (bool)ForceLegacy.GetValue(null) };
            // The original coverage checker reads this prefab. Asset clone factories
            // can restore the original font, so ApplyLabelFont also updates each clone.
            text.font = fontAsset;
            ForceLegacy.SetValue(null, false);
            if (!logged)
            {
                logged = true;
                Log.Message("[World Feature Text Compatibility 1.7] Korean world names use TextMeshPro; font index " + fontIndex + ".");
            }
        }

        public static void RestorePrefab(PrefabState __state)
        {
            if (__state == null) return;
            __state.Text.font = __state.Font;
            ForceLegacy.SetValue(null, __state.Legacy);
        }

        public static bool UsePreparedCharacters(ref bool __result)
        {
            // PrepareWorldNames verified all printable characters before swapping the
            // prefab. Native coverage tests also reject newline/tab control characters.
            var text = WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab?.GetComponent<TextMeshPro>();
            if (fontAsset == null || text == null || !ReferenceEquals(text.font, fontAsset)) return true;
            __result = false;
            return false;
        }

        public static void ApplyLabelFont(WorldFeatureTextMesh_TextMeshPro __instance)
        {
            var prefab = WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab?.GetComponent<TextMeshPro>();
            if (fontAsset == null || prefab == null || !ReferenceEquals(prefab.font, fontAsset)) return;
            var label = (TextMeshPro)LabelText.GetValue(__instance);
            label.font = fontAsset;
            label.ForceMeshUpdate(); // Creates the lazily initialized font material.
            // Waylith TMP draws its own font material. Keep the native world overlay
            // queue on that material as well as on the component's renderer.
            label.fontMaterial.renderQueue = 3610;
            label.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = label.fontMaterial;
        }

        public static bool IsHangul(char c) => (c >= '\uAC00' && c <= '\uD7A3') ||
            (c >= '\u1100' && c <= '\u11FF') || (c >= '\u3130' && c <= '\u318F') ||
            (c >= '\uA960' && c <= '\uA97F') || (c >= '\uD7B0' && c <= '\uD7FF');

        private static void EnsureFont()
        {
            var current = TextMeshProBackend.Current;
            if (!ReferenceEquals(backend, current))
            {
                byte[] data = File.ReadAllBytes(fontPath);
                string hash = Convert.ToHexString(SHA256.HashData(data));
                int index = GuiBackend.Current.RegisterFont("WorldFeatureTextCompat17/" + hash + "/48", data, 48f, 1);
                if (index < 0) throw new InvalidDataException("World text compatibility: Pretendard registration failed.");
                backend = current;
                fontIndex = index;
                // ImGuiHost exposes a List<ImFontPtr> through IReadOnlyList<ImFontPtr>.
                targetFont = ((IList)imFonts.GetValue(backend))[fontIndex];
                fontAsset = new TMP_FontAsset(fontIndex) { name = "WorldFeatureTextCompat17/Pretendard" };
            }
            bakeNewFonts.Invoke(backend, null);
            // New worlds may contain new syllables. Rebuild only this dedicated slot.
            ((IList)bakedFonts.GetValue(backend))[fontIndex] = bake.Invoke(backend, new[] { targetFont });
            // Sync atlas dimensions/UVs after new glyphs are allocated. Engine rebakes
            // invoke ExtendBake too, so Korean glyphs survive atlas growth and UV changes.
            // New pixels can occupy an existing atlas without changing its dimensions.
            // Invalidate the engine's pixel-pointer cache so it uploads those pixels too.
            atlasPixels.SetValue(backend, IntPtr.Zero);
            captureAtlas.Invoke(backend, new[] { getAtlas.Invoke(getIO.Invoke(null, null), null) });
            if (current.GetAtlasTexture(fontIndex) == null)
                throw new InvalidOperationException("World text compatibility: font atlas capture failed.");
        }

        public static void ExtendBake(object font, object __result)
        {
            if (targetFont == null || !targetFont.Equals(font) || Characters.Count == 0) return;
            object baked = getFontBaked.Invoke(null, new[] { font, (object)48f, -1f });
            var text = new StringBuilder((string)characterList.GetValue(__result));
            int first = -1, last = -1;
            foreach (char c in Characters)
            {
                // Native Bake already covers these ranges.
                if ((c >= 32 && c <= 126) || (c >= 160 && c <= 255)) continue;
                if (first < 0) first = last = c;
                else if (c == last + 1) last = c;
                else
                {
                    bakeRange.Invoke(null, new[] { baked, __result, text, (object)first, last });
                    first = last = c;
                }
            }
            if (first >= 0) bakeRange.Invoke(null, new[] { baked, __result, text, (object)first, last });
            characterList.SetValue(__result, text.ToString());
        }
    }
}
