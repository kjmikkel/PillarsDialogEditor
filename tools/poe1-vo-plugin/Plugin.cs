using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace PillarsDialogEditor.Poe1VoOverride;

/// SPIKE (issue 4, option 3): PoE1 plays a conversation line from a loose file when the game has
/// no voice of its own for it.
///
/// The game's route (decompiled): Conversation.PlayVO asks GameResources.GetDialogueAudio for the
/// line's VOAsset, then GlobalAudioPlayer.StreamClipAtSource streams VOAsset.VOClip.clip through
/// Resources.LoadAsync in Coroutine_StreamClip. Two patches hook in:
///   - GetDialogueAudio postfix: when the game found no VOAsset, or one with an empty clip, and
///     "VO Override/conversation/conversation_node[_fem].ogg|wav" exists under the game folder,
///     hand back a runtime VOAsset whose clip carries a marker plus that file's path.
///   - Coroutine_StreamClip prefix: a marked path is loaded from disk instead of Resources; the
///     rest copies the game's coroutine so the speaker, line timer and pause behave the same.
/// The game's own voice always wins (vanilla lines are never replaced).
[BepInPlugin("io.github.kjmikkel.pillarsdialogeditor.poe1vo", "PoE1 VO override (spike)", "0.0.1")]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string Marker = "pde-vo-override:";
    private static readonly string[] Extensions = [".ogg", ".wav"];

    internal static ManualLogSource Log = null!;
    internal static string Root = "";
    private static readonly Dictionary<string, VOAsset> Made = new(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        Log = Logger;
        Root = Path.Combine(Paths.GameRootPath, "VO Override");
        new Harmony("io.github.kjmikkel.pillarsdialogeditor.poe1vo").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"Looking for override voice lines in {Root}");
    }

    /// The runtime VOAsset for a line's loose file, or null when there is no such file.
    internal static VOAsset OverrideFor(string dialogName, int node, bool female)
    {
        // Same name the game builds in GetDialogueAudio (_fem only for English).
        var conv = Path.GetFileNameWithoutExtension(dialogName);
        var name = conv + "_" + node.ToString("0000");
        if (female && StringTableManager.CurrentLanguage?.EnumLanguage == OEICommon.Language.english) name += "_fem";

        foreach (var ext in Extensions)
        {
            var file = Path.Combine(Path.Combine(Root, conv), name + ext);
            if (!File.Exists(file)) continue;
            if (Made.TryGetValue(file, out var cached) && cached) return cached;

            var asset = ScriptableObject.CreateInstance<VOAsset>();
            asset.name = name;
            asset.VOClip = new VOBankClip { clip = Marker + file };
            UnityEngine.Object.DontDestroyOnLoad(asset);
            Made[file] = asset;
            Log.LogInfo($"Override voice for {name}: {file}");
            return asset;
        }
        return null;
    }
}

[HarmonyPatch(typeof(GameResources), nameof(GameResources.GetDialogueAudio))]
internal static class GetDialogueAudioPatch
{
    private static void Postfix(string dialogName, int node, bool useFemaleVersion, ref VOAsset __result)
    {
        if (__result != null && !string.IsNullOrEmpty(__result.VOClip?.clip)) return;   // the game's voice wins
        if (string.IsNullOrEmpty(dialogName)) return;
        var over = Plugin.OverrideFor(dialogName, node, useFemaleVersion);
        if (over != null) __result = over;
    }
}

[HarmonyPatch(typeof(GlobalAudioPlayer), "Coroutine_StreamClip")]
internal static class StreamClipPatch
{
    private static bool Prefix(string clipFilename, GameObject tempObject, AudioSource source,
        bool ignoreListenerVolume, bool bIs3DSound, GlobalAudioPlayer.ClipLoaded onClipLoaded, object tag,
        ref IEnumerator __result)
    {
        if (clipFilename == null || !clipFilename.StartsWith(Plugin.Marker, StringComparison.Ordinal)) return true;
        __result = StreamFile(clipFilename.Substring(Plugin.Marker.Length), tempObject, source,
            ignoreListenerVolume, bIs3DSound, onClipLoaded, tag);
        return false;
    }

    // Mirrors GlobalAudioPlayer.Coroutine_StreamClip after its Resources load.
    private static IEnumerator StreamFile(string path, GameObject tempObject, AudioSource source,
        bool ignoreListenerVolume, bool bIs3DSound, GlobalAudioPlayer.ClipLoaded onClipLoaded, object tag)
    {
        var type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS;
        AudioClip clip = null;
        using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
        {
            yield return req.SendWebRequest();
            if (req.isNetworkError || req.isHttpError)
                Plugin.Log.LogError($"Could not load override voice '{path}': {req.error}");
            else
                clip = DownloadHandlerAudioClip.GetContent(req);
        }
        if (clip == null)
        {
            if (tempObject) GameUtilities.Destroy(tempObject);
            yield break;
        }
        clip.name = Path.GetFileNameWithoutExtension(path);

        onClipLoaded?.Invoke(clip, tag);
        if (source != null)
        {
            source.clip = clip;
            source.ignoreListenerVolume = ignoreListenerVolume;
            var volume = source.gameObject.GetComponent<VolumeAsCategory>();
            if (volume != null) volume.UpdateVolume();
            GlobalAudioPlayer.Play(source, bIs3DSound);
        }
        while ((source && source.isPlaying) || (TimeController.Instance && TimeController.Instance.Paused))
            yield return null;
        if (source) source.clip = null;
        UnityEngine.Object.Destroy(clip);   // a runtime clip, not an asset: Resources.UnloadAsset refuses it
        if (tempObject) GameUtilities.Destroy(tempObject);
    }
}
