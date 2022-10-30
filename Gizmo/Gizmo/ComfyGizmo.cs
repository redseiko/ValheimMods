﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

using UnityEngine;

using static Gizmo.PluginConfig;

namespace Gizmo {
  [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
  public class ComfyGizmo : BaseUnityPlugin {
    public const string PluginGUID = "com.rolopogo.gizmo.comfy";
    public const string PluginName = "ComfyGizmo";
    public const string PluginVersion = "1.5.0";

    static GameObject _gizmoPrefab = null;
    static Transform _gizmoRoot;

    static Transform _xGizmo;
    static Transform _yGizmo;
    static Transform _zGizmo;

    static Transform _xGizmoRoot;
    static Transform _yGizmoRoot;
    static Transform _zGizmoRoot;

    static GameObject _comfyGizmo;
    static Transform _comfyGizmoRoot;

    static Vector3 _eulerAngles;

    static bool _localFrame;

    static float _snapAngle;

    Harmony _harmony;

    public void Awake() {
      BindConfig(Config);

      SnapDivisions.SettingChanged += (sender, eventArgs) => _snapAngle = 180f / SnapDivisions.Value;
      _snapAngle = 180f / SnapDivisions.Value;

      _gizmoPrefab = LoadGizmoPrefab();
     
      _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);
    }

    public void OnDestroy() {
      _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(Game))]
    static class GamePatch {
      [HarmonyPostfix]
      [HarmonyPatch(nameof(Game.Start))]
      static void StartPostfix() {
        Destroy(_gizmoRoot);
        _gizmoRoot = CreateGizmoRoot();

        Destroy(_comfyGizmo);
        _comfyGizmo = new("ComfyGizmo");
        _comfyGizmoRoot = _comfyGizmo.transform;
        _localFrame = false;
      }
    }

    [HarmonyPatch(typeof(Player))]
    static class PlayerPatch {
      [HarmonyTranspiler]
      [HarmonyPatch(nameof(Player.UpdatePlacementGhost))]
      static IEnumerable<CodeInstruction> UpdatePlacementGhostTranspiler(IEnumerable<CodeInstruction> instructions) {
        return new CodeMatcher(instructions)
            .MatchForward(
                useEnd: false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), nameof(Player.m_placeRotation))),
                new CodeMatch(OpCodes.Conv_R4),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Call),
                new CodeMatch(OpCodes.Stloc_S))
            .Advance(offset: 5)
            .InsertAndAdvance(Transpilers.EmitDelegate<Func<Quaternion, Quaternion>>(_ => _comfyGizmoRoot.rotation))
            .InstructionEnumeration();
      }

      [HarmonyPostfix]
      [HarmonyPatch(nameof(Player.UpdatePlacement))]
      static void UpdatePlacementPostfix(ref Player __instance, ref bool takeInput) {
        if (__instance.m_placementMarkerInstance) {
          _gizmoRoot.gameObject.SetActive(ShowGizmoPrefab.Value && __instance.m_placementMarkerInstance.activeSelf);
          _gizmoRoot.position = __instance.m_placementMarkerInstance.transform.position + (Vector3.up * 0.5f);
        }

        if (!__instance.m_buildPieces || !takeInput) {
          return;
        }

        if (Input.GetKey(CopyPieceRotationKey.Value.MainKey) && __instance.m_hoveringPiece != null) {
          MatchPieceRotation(__instance.m_hoveringPiece);
        }

        // Change Rotation Frames
        if (Input.GetKeyDown(ChangeRotationModeKey.Value.MainKey)) {
          if(_localFrame) {
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Default rotation mode enabled");
            if (ResetRotation.Value) {
              ResetRotationsLocalFrame();
            } else {
              _eulerAngles = _comfyGizmo.transform.eulerAngles;
              ResetGizmoRoot();
              RotateGizmoComponents(_eulerAngles);
            }
            
          } else {
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Local frame rotation mode enabled");
            if (ResetRotation.Value) {
              ResetRotations();
            } else {
              Quaternion currentRotation = _comfyGizmoRoot.rotation;
              ResetGizmoComponents();
              _gizmoRoot.rotation = currentRotation;
            }
          }
          
          _localFrame = !_localFrame;
          return;
        }

        _xGizmo.localScale = Vector3.one;
        _yGizmo.localScale = Vector3.one;
        _zGizmo.localScale = Vector3.one;

        if (!_localFrame) {
          Rotate();
        } else {
          int direction = Math.Sign(Input.GetAxis("Mouse ScrollWheel"));
          RotateLocalFrame(direction);
        }
      }
    }

    static void Rotate() {
      if (Input.GetKey(ResetAllRotationKey.Value.MainKey)) {
        ResetRotations();
      } else if (Input.GetKey(XRotationKey.Value.MainKey)) {
        _xGizmo.localScale = Vector3.one * 1.5f;
        _eulerAngles.x = GetRotationAngle(_eulerAngles.x);
      } else if (Input.GetKey(ZRotationKey.Value.MainKey)) {
        _zGizmo.localScale = Vector3.one * 1.5f;
        _eulerAngles.z = GetRotationAngle(_eulerAngles.z);
      } else {
        _yGizmo.localScale = Vector3.one * 1.5f;
        _eulerAngles.y = GetRotationAngle(_eulerAngles.y);
      }

      _comfyGizmo.transform.localRotation = Quaternion.Euler(_eulerAngles);
      RotateGizmoComponents(_eulerAngles);
    }

    static void RotateLocalFrame(int direction) {
      if (Input.GetKey(ResetAllRotationKey.Value.MainKey)) {
        ResetRotationsLocalFrame();
        return;
      }

      float rotation = direction * _snapAngle;
      Vector3 rotVector;

      if (Input.GetKey(XRotationKey.Value.MainKey)) {
        _xGizmo.localScale = Vector3.one * 1.5f;
        rotVector = Vector3.right;
        HandleAxisInputLocalFrame(rotVector, _xGizmo);
      } else if (Input.GetKey(ZRotationKey.Value.MainKey)) {
        _zGizmo.localScale = Vector3.one * 1.5f;
        rotVector = Vector3.forward;
        HandleAxisInputLocalFrame(rotVector, _zGizmo);
      } else {
        _yGizmo.localScale = Vector3.one * 1.5f;
        rotVector = Vector3.up;
        HandleAxisInputLocalFrame(rotVector, _yGizmo);
      }

      RotateAxes(rotation, rotVector);
    }

    static void RotateAxes(float rotation, Vector3 rotVector) {
      _comfyGizmo.transform.rotation *= Quaternion.AngleAxis(rotation, rotVector);
      _gizmoRoot.rotation *= Quaternion.AngleAxis(rotation, rotVector);
    }

    static float GetRotationAngle(float rotation) {
      rotation += Math.Sign(Input.GetAxis("Mouse ScrollWheel")) * _snapAngle;

      if (Input.GetKey(ResetRotationKey.Value.MainKey)) {
        rotation = 0f;
      }
      return rotation;
    }

    static void HandleAxisInputLocalFrame(Vector3 rotVector, Transform gizmo) {
      gizmo.localScale = Vector3.one * 1.5f;

      if (Input.GetKey(ResetRotationKey.Value.MainKey)) {
        _comfyGizmo.transform.rotation = Quaternion.AngleAxis(0f, rotVector);
        _xGizmoRoot.rotation = Quaternion.AngleAxis(0f, rotVector);
        _yGizmoRoot.rotation = Quaternion.AngleAxis(0f, rotVector);
        _zGizmoRoot.rotation = Quaternion.AngleAxis(0f, rotVector);
      }
    }
    static void MatchPieceRotation(Piece target) {
      if (_localFrame) {
        _comfyGizmo.transform.rotation = target.GetComponent<Transform>().localRotation;
        _gizmoRoot.rotation = target.GetComponent<Transform>().localRotation;
      } else {
        _eulerAngles = target.GetComponent<Transform>().eulerAngles;
        Rotate();
      }
    }
    static void ResetRotations() {
      _comfyGizmo.transform.localRotation = Quaternion.Euler(Vector3.zero);
      RotateGizmoComponents(Vector3.zero);
    }

    static void ResetGizmoComponents() {
      _eulerAngles = Vector3.zero;
      RotateGizmoComponents(Vector3.zero);
    }

    static void ResetGizmoRoot() {
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.up);
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.right);
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.forward);
    }

    static void RotateGizmoComponents(Vector3 eulerAngles) {
      _xGizmoRoot.localRotation = Quaternion.Euler(eulerAngles.x, 0f, 0f);
      _yGizmoRoot.localRotation = Quaternion.Euler(0f, eulerAngles.y, 0f);
      _zGizmoRoot.localRotation = Quaternion.Euler(0f, 0f, eulerAngles.z);
    }

    static void ResetRotationsLocalFrame() {
      _comfyGizmo.transform.rotation = Quaternion.AngleAxis(0f, Vector3.up);
      _comfyGizmo.transform.rotation = Quaternion.AngleAxis(0f, Vector3.right);
      _comfyGizmo.transform.rotation = Quaternion.AngleAxis(0f, Vector3.forward);

      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.up);
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.right);
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.forward);
    }

    static GameObject LoadGizmoPrefab() {
      AssetBundle bundle = AssetBundle.LoadFromMemory(
          GetResource(Assembly.GetExecutingAssembly(), "Gizmo.Resources.gizmos"));

      GameObject prefab = bundle.LoadAsset<GameObject>("GizmoRoot");
      bundle.Unload(unloadAllLoadedObjects: false);

      return prefab;
    }

    static byte[] GetResource(Assembly assembly, string resourceName) {
      Stream stream = assembly.GetManifestResourceStream(resourceName);

      byte[] data = new byte[stream.Length];
      stream.Read(data, offset: 0, count: (int) stream.Length);

      return data;
    }

    static Transform CreateGizmoRoot() {
      _gizmoRoot = Instantiate(_gizmoPrefab).transform;

      // ??? Something about quaternions.
      _xGizmo = _gizmoRoot.Find("YRoot/ZRoot/XRoot/X");
      _yGizmo = _gizmoRoot.Find("YRoot/Y");
      _zGizmo = _gizmoRoot.Find("YRoot/ZRoot/Z");

      _xGizmoRoot = _gizmoRoot.Find("YRoot/ZRoot/XRoot");
      _yGizmoRoot = _gizmoRoot.Find("YRoot");
      _zGizmoRoot = _gizmoRoot.Find("YRoot/ZRoot");

      return _gizmoRoot.transform;
    }
  }
}
