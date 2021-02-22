using BepInEx;
using HarmonyLib;
using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace SphereSaveFix
{
    [BepInPlugin("com.brokenmass.plugin.Dyson.SphereSaveFix", "SphereSaveFix", "1.0.0")]
    public class SphereSaveFix : BaseUnityPlugin
    {
        Harmony harmony;

        static readonly char SAVE_FIX_VERSION = 'X';

        void Start()
        {
            harmony = new Harmony("com.brokenmass.plugin.Dyson.SphereSaveFix");
            try
            {
                harmony.PatchAll(typeof(SphereSaveFix));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            harmony.UnpatchSelf();  // For ScriptEngine hot-reloading
        }

        [HarmonyPrefix, HarmonyPatch(typeof(DysonShell), "Export")]
        public static bool DysonShell_Export_Prefix(DysonShell __instance, BinaryWriter w)
        {
            w.Write(SAVE_FIX_VERSION);
            w.Write(__instance.id);
            w.Write(__instance.protoId);
            w.Write(__instance.layerId);
            w.Write(__instance.randSeed);
            w.Write(__instance.polygon.Count);

            for (int i = 0; i < __instance.polygon.Count; i++)
            {
                w.Write(__instance.polygon[i].x);
                w.Write(__instance.polygon[i].y);
                w.Write(__instance.polygon[i].z);
            }

            w.Write(__instance.nodes.Count);
            for (int j = 0; j < __instance.nodes.Count; j++)
            {
                w.Write(__instance.nodes[j].id);
            }

            // saving the following counts and length only for validation purpose
            // there's no need to save the actual contents of those arrays as they can be recalculalted using GenerateGeometry() from just
            // polygon and nodes (and frames but those are inferred from nodes)

            w.Write(__instance.vertexCount);
            w.Write(__instance.triangleCount);

            w.Write(__instance.verts.Length);
            w.Write(__instance.pqArr.Length);
            w.Write(__instance.tris.Length);
            w.Write(__instance.vAdjs.Length);
            w.Write(__instance.vertAttr.Length);
            w.Write(__instance.vertsq.Length);
            w.Write(__instance.vertsqOffset.Length);

            // the nodecps and vertcps (that gets updated every time a new piece of the shell has been completed) must be saved
            // a further optimisation could be to tag a shell as 'complete' and at that point there would be no need to store
            // all this data

            var num = __instance.nodecps.Length;
            w.Write(num);
            for (int num5 = 0; num5 < num; num5++)
            {
                w.Write(__instance.nodecps[num5]);
            }
            num = __instance.vertcps.Length;
            w.Write(num);
            for (int num6 = 0; num6 < num; num6++)
            {
                w.Write(__instance.vertcps[num6]);
            }
            num = __instance.vertRecycle.Length;
            w.Write(num);
            w.Write(__instance.vertRecycleCursor);
            for (int num7 = 0; num7 < __instance.vertRecycleCursor; num7++)
            {
                w.Write(__instance.vertRecycle[num7]);
            }
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(DysonShell), "Import")]
        public static bool DysonShell_Import_Prefix(DysonShell __instance, BinaryReader r, DysonSphere dysonSphere)
        {
            __instance.SetEmpty();
            var version = r.PeekChar();
            if (version != SAVE_FIX_VERSION)
            {
                Debug.Log($"Using native import {version}");
                return true;
            }

            Debug.Log($"Using smart import {version}");

            r.ReadChar();
            __instance.id = r.ReadInt32();
            __instance.protoId = r.ReadInt32();
            __instance.layerId = r.ReadInt32();
            __instance.randSeed = r.ReadInt32();
            int num = r.ReadInt32();
            for (int i = 0; i < num; i++)
            {
                Vector3 item = default(Vector3);
                item.x = r.ReadSingle();
                item.y = r.ReadSingle();
                item.z = r.ReadSingle();
                __instance.polygon.Add(item);
            }
            int num2 = r.ReadInt32();
            for (int j = 0; j < num2; j++)
            {
                int num3 = r.ReadInt32();
                DysonNode dysonNode = dysonSphere.FindNode(__instance.layerId, num3);
                Assert.NotNull(dysonNode);
                if (dysonNode != null)
                {
                    __instance.nodeIndexMap[num3] = __instance.nodes.Count;
                    __instance.nodes.Add(dysonNode);
                    if (!dysonNode.shells.Contains(__instance))
                    {
                        dysonNode.shells.Add(__instance);
                    }
                }
            }
            Assert.True(__instance.nodeIndexMap.Count == __instance.nodes.Count);
            int count = __instance.nodes.Count;
            for (int k = 0; k < count; k++)
            {
                int index = k;
                int index2 = (k + 1) % count;
                DysonFrame dysonFrame = DysonNode.FrameBetween(__instance.nodes[index], __instance.nodes[index2]);
                Assert.NotNull(dysonFrame);
                __instance.frames.Add(dysonFrame);
            }
            var vertexCount = r.ReadInt32();
            var triangleCount = r.ReadInt32();
            int vertsLength = r.ReadInt32();
            var pqArrLength = r.ReadInt32();
            var trisLength = r.ReadInt32();
            var vAdjsLength = r.ReadInt32();
            var vertAttrLength = r.ReadInt32();
            var vertsqLength = r.ReadInt32();
            var vertsqOffsetLength = r.ReadInt32();

            __instance.GenerateGeometry();

            // Verify that generated geometry is 'at least' formally correct from a counting point of view
            Assert.True(vertexCount == __instance.vertexCount);
            Assert.True(triangleCount == __instance.triangleCount);
            Assert.True(vertsLength == __instance.verts.Length);

            Assert.True(pqArrLength == __instance.pqArr.Length);
            Assert.True(trisLength == __instance.tris.Length);
            Assert.True(vAdjsLength == __instance.vAdjs.Length);
            Assert.True(vertAttrLength == __instance.vertAttr.Length);
            Assert.True(vertsqLength == __instance.vertsq.Length);
            Assert.True(vertsqOffsetLength == __instance.verts.Length);


            Assert.True(__instance.vertAttr.Length == __instance.verts.Length);
            Assert.True(__instance.vertsq.Length == __instance.verts.Length);
            Assert.True(__instance.vertsqOffset.Length == __instance.nodes.Count + 1);

            // Restore nodecps, vertcps and vertRecycle from save
            var num4 = r.ReadInt32();
            __instance.nodecps = new int[num4];
            for (int num9 = 0; num9 < num4; num9++)
            {
                __instance.nodecps[num9] = r.ReadInt32();
            }
            Assert.True(__instance.nodecps.Length == __instance.nodes.Count + 1);
            num4 = r.ReadInt32();
            __instance.vertcps = new uint[num4];
            for (int num10 = 0; num10 < num4; num10++)
            {
                __instance.vertcps[num10] = r.ReadUInt32();
            }
            num4 = r.ReadInt32();
            __instance.vertRecycleCursor = r.ReadInt32();
            __instance.vertRecycle = new int[num4];
            for (int num11 = 0; num11 < __instance.vertRecycleCursor; num11++)
            {
                __instance.vertRecycle[num11] = r.ReadInt32();
            }
            Assert.True(__instance.vertRecycle.Length == __instance.verts.Length);


            __instance.buffer.SetData(__instance.vertcps);

            return false;
        }

    }
}