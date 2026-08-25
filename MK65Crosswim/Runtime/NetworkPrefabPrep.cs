using System;
using System.Reflection;
using Crosswim.Bootstrap;
using Mirage;
using UnityEngine;

namespace Crosswim.Runtime
{
    internal static class NetworkPrefabPrep
    {
        private static readonly FieldInfo? SpawnedFromInstantiateField =
            typeof(NetworkIdentity).GetField("<SpawnedFromInstantiate>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void PrepareTemplate(GameObject root)
        {
            if (root == null)
                return;
            try
            {
                StripChildIdentitiesUnderVisual(root);
                NetworkIdentity[] ids = root.GetComponentsInChildren<NetworkIdentity>(true);
                for (int i = 0; i < ids.Length; i++)
                    ResetUnspawned(ids[i]);
            }
            catch (Exception ex)
            {
                CrosswimPlugin.ModLog?.LogError($"NetworkPrefabPrep.PrepareTemplate: {ex}");
            }
        }

        internal static void ResetUnspawned(NetworkIdentity? identity)
        {
            if (identity == null || identity.IsSpawned)
                return;
            identity.ClearSceneId();
            SpawnedFromInstantiateField?.SetValue(identity, false);
        }

        private static void StripChildIdentitiesUnderVisual(GameObject root)
        {
            NetworkIdentity[] ids = root.GetComponentsInChildren<NetworkIdentity>(true);
            for (int i = ids.Length - 1; i >= 0; i--)
            {
                NetworkIdentity ni = ids[i];
                if (ni == null || ni.gameObject == root)
                    continue;
                if (!PrefabFactory.IsVisualRoot(ni.transform))
                    continue;
                UnityEngine.Object.DestroyImmediate(ni);
            }
        }
    }
}
